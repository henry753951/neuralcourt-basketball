using System;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using SentisModelAsset = Unity.InferenceEngine.ModelAsset;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Runs the ten recurrent Basketball agents as one fixed-size Sentis batch.
    /// Only inference is asynchronous: each controller's closed-loop state is
    /// committed after the GPU readback completes and before another tick starts.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class BasketballSentisBatchScheduler : MonoBehaviour
    {
        public const int BatchSize = 10;
        public const int CombinedOutputCount =
            BasketballModelContract.PackedOutputFeatureCount;

        private const string DefaultResourcePath = "Models/BasketballMoEBatch10";
        private const string InputName = "input";
        private const string OutputName = "batch_output";
        private const int MaximumReadbackRetries = 2;

        private static readonly ProfilerMarker InferenceMarker =
            new("Basketball.Inference");
        private static readonly ProfilerMarker ScheduleMarker =
            new("Basketball.Inference.Sentis.Schedule");
        private static readonly ProfilerMarker ReadbackMarker =
            new("Basketball.Inference.Sentis.Readback");

        [SerializeField] private BasketballTeamMember[] players;
        [SerializeField] private SentisModelAsset modelAsset;

        private readonly BasketballNeuralController[] controllers =
            new BasketballNeuralController[BatchSize];
        private readonly float[] batchInput =
            new float[BatchSize * BasketballModelContract.InputFeatureCount];
        private readonly float[] batchOutput =
            new float[BatchSize * CombinedOutputCount];

        private Worker worker;
        private Tensor<float> inputTensor;
        private Tensor<float> pendingOutput;
        private NativeArray<float> readbackBuffer;
        private AsyncGPUReadbackRequest readbackRequest;
        private SchedulerState schedulerState;
        private float accumulator;
        private double inferenceStartTime;
        private bool controllersAttached;
        private bool readbackInFlight;
        private int readbackRetryCount;
        private float tickInterval = 1f / BasketballAgentState.Framerate;
        private int maximumBufferedTicks = 4;

        public string BackendName => schedulerState switch
        {
            SchedulerState.WarmingUp => "SENTIS GPU WARMUP",
            SchedulerState.Failed => "SENTIS GPU ERROR",
            SchedulerState.Disposed => "SENTIS GPU STOPPED",
            SchedulerState.Running when
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 =>
                "SENTIS DML BATCH 10",
            SchedulerState.Running => "SENTIS GPU BATCH 10",
            _ => "SENTIS GPU INITIALIZING"
        };
        public float LastRoundTripMilliseconds { get; private set; } = -1f;
        public bool IsReadbackPending => schedulerState == SchedulerState.Running &&
                                         pendingOutput != null;
        public bool IsOperational => schedulerState == SchedulerState.Running;

        private enum SchedulerState
        {
            Uninitialized,
            WarmingUp,
            Running,
            Failed,
            Disposed
        }

        private void OnEnable()
        {
            if ((schedulerState == SchedulerState.Disposed ||
                 schedulerState == SchedulerState.Failed) &&
                players != null && players.Length > 0)
            {
                schedulerState = SchedulerState.Uninitialized;
                InitializeScheduler();
            }
        }

        private void Update()
        {
            if (schedulerState == SchedulerState.WarmingUp)
            {
                PollWarmup();
                return;
            }
            if (schedulerState != SchedulerState.Running)
            {
                return;
            }

            ResolveSchedulingSettings();
            accumulator += Mathf.Min(
                Time.deltaTime,
                tickInterval * maximumBufferedTicks);
            accumulator = Mathf.Min(accumulator, tickInterval * maximumBufferedTicks);

            if (pendingOutput != null)
            {
                if (!PollOutputReadback("readback"))
                {
                    UpdateInterpolationAlpha();
                    return;
                }
                if (!CompleteBatch())
                {
                    return;
                }
            }

            if (accumulator >= tickInterval)
            {
                BeginBatch();
            }
            UpdateInterpolationAlpha();
        }

        private void OnDisable()
        {
            DetachControllers();
            DisposeSentis();
            if (schedulerState != SchedulerState.Failed)
            {
                schedulerState = SchedulerState.Disposed;
            }
        }

        private void OnDestroy()
        {
            DetachControllers();
            DisposeSentis();
            schedulerState = SchedulerState.Disposed;
        }

        public void ConfigureRuntime(BasketballTeamMember[] teamMembers)
        {
            if (schedulerState != SchedulerState.Uninitialized)
            {
                return;
            }
            players = teamMembers;
            InitializeScheduler();
        }

        private void InitializeScheduler()
        {
            try
            {
                ResolveControllers();
                AttachControllers();
                if (!SystemInfo.supportsComputeShaders)
                {
                    throw new NotSupportedException(
                        "This device does not support the compute shaders required by " +
                        "the Sentis GPUCompute backend.");
                }
                ResolveSchedulingSettings();
                modelAsset = modelAsset != null
                    ? modelAsset
                    : Resources.Load<SentisModelAsset>(DefaultResourcePath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Sentis model was not found at Resources/{DefaultResourcePath}.onnx.");
                }

                Model model = ModelLoader.Load(modelAsset);
                ValidateModelContract(model);
                inputTensor = new Tensor<float>(
                    new TensorShape(BatchSize, BasketballModelContract.InputFeatureCount),
                    batchInput);
                readbackBuffer = new NativeArray<float>(
                    BatchSize * CombinedOutputCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                worker = new Worker(model, BackendType.GPUCompute);
                BeginWarmup();
            }
            catch (Exception exception)
            {
                FailGpu("initialization", exception);
            }
        }

        private void ResolveControllers()
        {
            if (players == null || players.Length != BatchSize)
            {
                throw new InvalidOperationException(
                    $"Sentis batch requires exactly {BatchSize} players.");
            }
            for (int index = 0; index < BatchSize; index++)
            {
                BasketballTeamMember member = players[index];
                BasketballNeuralController controller = member != null
                    ? member.Controller
                    : null;
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        $"Sentis batch player {index + 1} has no neural controller.");
                }
                controller.Initialize();
                controllers[index] = controller;
            }
        }

        private static void ValidateModelContract(Model model)
        {
            if (model == null || model.inputs.Count != 1 ||
                model.inputs[0].name != InputName)
            {
                throw new InvalidOperationException(
                    "Basketball Sentis model must contain one input named 'input'.");
            }
            if (model.outputs.Count != 1 || model.outputs[0].name != OutputName)
            {
                throw new InvalidOperationException(
                    "Basketball Sentis model must contain one output named 'batch_output'.");
            }
        }

        private void ResolveSchedulingSettings()
        {
            int tickRate = controllers[0].NeuralTickRate;
            int catchUpTicks = controllers[0].RuntimeSettings != null
                ? controllers[0].RuntimeSettings.MaximumCatchUpTicks
                : 4;
            for (int index = 1; index < BatchSize; index++)
            {
                if (controllers[index].NeuralTickRate != tickRate)
                {
                    throw new InvalidOperationException(
                        "All Sentis batch players must share one Neural Tick Rate profile.");
                }
            }
            float updatedInterval = 1f / Mathf.Max(1, tickRate);
            int updatedBufferedTicks = Mathf.Max(1, catchUpTicks);
            if (!Mathf.Approximately(updatedInterval, tickInterval) ||
                updatedBufferedTicks != maximumBufferedTicks)
            {
                tickInterval = updatedInterval;
                maximumBufferedTicks = updatedBufferedTicks;
                accumulator = Mathf.Min(
                    accumulator,
                    tickInterval * maximumBufferedTicks);
            }
        }

        private void BeginWarmup()
        {
            using (ScheduleMarker.Auto())
            {
                worker.Schedule(inputTensor);
                pendingOutput = worker.PeekOutput(OutputName) as Tensor<float>;
                ValidateOutputTensor(pendingOutput);
                readbackRetryCount = 0;
                RequestOutputReadback();
            }
            schedulerState = SchedulerState.WarmingUp;
        }

        private void PollWarmup()
        {
            if (pendingOutput == null || !PollOutputReadback("warmup"))
            {
                return;
            }
            try
            {
                CopyAndValidateReadback();
                pendingOutput = null;
                accumulator = 0f;
                LastRoundTripMilliseconds = -1f;
                schedulerState = SchedulerState.Running;
            }
            catch (Exception exception)
            {
                FailGpu("warmup", exception);
            }
        }

        private void BeginBatch()
        {
            try
            {
                for (int index = 0; index < BatchSize; index++)
                {
                    Span<float> row = batchInput.AsSpan(
                        index * BasketballModelContract.InputFeatureCount,
                        BasketballModelContract.InputFeatureCount);
                    if (!controllers[index].PrepareExternalTick(row))
                    {
                        throw new InvalidOperationException(
                            $"Player {index + 1} produced an invalid neural input.");
                    }
                }

                using (InferenceMarker.Auto())
                using (ScheduleMarker.Auto())
                {
                    inputTensor.Upload(batchInput);
                    worker.Schedule(inputTensor);
                    pendingOutput = worker.PeekOutput(OutputName) as Tensor<float>;
                    ValidateOutputTensor(pendingOutput);
                    readbackRetryCount = 0;
                    RequestOutputReadback();
                }
                inferenceStartTime = Time.realtimeSinceStartupAsDouble;
            }
            catch (Exception exception)
            {
                FailGpu("schedule", exception);
            }
        }

        private bool CompleteBatch()
        {
            try
            {
                using (ReadbackMarker.Auto())
                {
                    CopyAndValidateReadback();
                    ReadOnlySpan<float> values = batchOutput;
                    for (int index = 0; index < BatchSize; index++)
                    {
                        int rowStart = index * CombinedOutputCount;
                        controllers[index].CompleteExternalTick(
                            values.Slice(
                                rowStart,
                                BasketballModelContract.OutputFeatureCount),
                            values.Slice(
                                rowStart + BasketballModelContract.OutputFeatureCount,
                                BasketballModelContract.ExpertCount));
                    }
                }

                pendingOutput = null;
                accumulator = Mathf.Max(0f, accumulator - tickInterval);
                LastRoundTripMilliseconds = (float)(
                    (Time.realtimeSinceStartupAsDouble - inferenceStartTime) * 1000.0);
                return true;
            }
            catch (Exception exception)
            {
                FailGpu("readback", exception);
                return false;
            }
        }

        private static void ValidateOutputTensor(Tensor<float> tensor)
        {
            int expected = BatchSize * CombinedOutputCount;
            if (tensor == null || tensor.count != expected)
            {
                throw new InvalidOperationException(
                    $"Sentis batch output contains {tensor?.count ?? 0} values; " +
                    $"expected {expected}.");
            }
        }

        private static void ValidateFinite(ReadOnlySpan<float> values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!float.IsFinite(values[index]))
                {
                    throw new InvalidOperationException(
                        $"Sentis batch output is non-finite at index {index}.");
                }
            }
        }

        private void RequestOutputReadback()
        {
            if (!readbackBuffer.IsCreated)
            {
                throw new InvalidOperationException(
                    "Basketball GPU readback buffer is not allocated.");
            }
            if (pendingOutput?.dataOnBackend is not ComputeTensorData computeData)
            {
                throw new InvalidOperationException(
                    "Basketball GPU output is not backed by ComputeTensorData.");
            }

            readbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref readbackBuffer,
                computeData.buffer,
                readbackBuffer.Length * sizeof(float),
                0);
            readbackInFlight = true;
        }

        private bool PollOutputReadback(string stage)
        {
            if (!readbackInFlight || !readbackRequest.done)
            {
                return false;
            }
            if (!readbackRequest.hasError)
            {
                readbackInFlight = false;
                return true;
            }

            readbackInFlight = false;
            if (readbackRetryCount < MaximumReadbackRetries)
            {
                readbackRetryCount++;
                if (readbackRetryCount == 1)
                {
                    Debug.LogWarning(
                        $"Basketball GPU readback reported a transient error during {stage}; " +
                        $"retrying without advancing recurrent state.",
                        this);
                }
                RequestOutputReadback();
                return false;
            }

            FailGpu(
                stage,
                new InvalidOperationException(
                    $"GPU readback failed after {MaximumReadbackRetries + 1} attempts."));
            return false;
        }

        private void CopyAndValidateReadback()
        {
            readbackBuffer.CopyTo(batchOutput);
            ValidateFinite(batchOutput);
        }

        private void AttachControllers()
        {
            for (int index = 0; index < BatchSize; index++)
            {
                controllers[index].SetExternalBatchScheduler(this);
            }
            controllersAttached = true;
            UpdateInterpolationAlpha();
        }

        private void DetachControllers()
        {
            if (!controllersAttached)
            {
                return;
            }
            for (int index = 0; index < BatchSize; index++)
            {
                if (controllers[index] != null)
                {
                    controllers[index].SetExternalBatchScheduler(null);
                }
            }
            controllersAttached = false;
        }

        private void UpdateInterpolationAlpha()
        {
            float alpha = Mathf.Clamp01(accumulator / tickInterval);
            for (int index = 0; index < BatchSize; index++)
            {
                if (controllers[index] != null)
                {
                    controllers[index].SetExternalInterpolationAlpha(alpha);
                }
            }
        }

        private void FailGpu(string stage, Exception exception)
        {
            DisposeSentis();
            schedulerState = SchedulerState.Failed;
            Debug.LogError(
                $"Basketball GPU-only inference failed during {stage}; neural simulation " +
                $"has stopped. {exception.GetType().Name}: {exception.Message}",
                this);
        }

        private void DisposeSentis()
        {
            if (readbackInFlight && !readbackRequest.done)
            {
                readbackRequest.WaitForCompletion();
            }
            readbackInFlight = false;
            worker?.Dispose();
            worker = null;
            inputTensor?.Dispose();
            inputTensor = null;
            pendingOutput = null;
            if (readbackBuffer.IsCreated)
            {
                readbackBuffer.Dispose();
            }
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballTeamMember[] teamMembers,
            SentisModelAsset importedModel)
        {
            players = teamMembers;
            modelAsset = importedModel;
        }
#endif
    }
}
