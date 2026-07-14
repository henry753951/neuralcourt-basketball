using System;
using Unity.InferenceEngine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using SentisModelAsset = Unity.InferenceEngine.ModelAsset;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Runs the three recurrent Basketball agents as one fixed-size Sentis batch.
    /// Only inference is asynchronous: each controller's closed-loop state is
    /// committed after the GPU readback completes and before another tick starts.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class BasketballSentisBatchScheduler : MonoBehaviour
    {
        public const int BatchSize = 3;
        public const int CombinedOutputCount =
            BasketballModelAsset.OutputFeatureCount + BasketballModelAsset.ExpertCount;

        private const string DefaultResourcePath = "Models/BasketballMoEBatch3";
        private const string InputName = "input";
        private const string OutputName = "batch_output";

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
            new float[BatchSize * BasketballModelAsset.InputFeatureCount];

        private Worker worker;
        private Tensor<float> inputTensor;
        private Tensor<float> pendingOutput;
        private SchedulerState schedulerState;
        private float accumulator;
        private int preparedControllerCount;
        private double inferenceStartTime;
        private bool controllersAttached;
        private float tickInterval = 1f / BasketballAgentState.Framerate;
        private int maximumBufferedTicks = 4;

        public string BackendName => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
            ? "SENTIS DML BATCH 3"
            : "SENTIS GPU BATCH 3";
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
                if (!pendingOutput.IsReadbackRequestDone())
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
                if (!SystemInfo.supportsComputeShaders)
                {
                    throw new NotSupportedException(
                        "This device does not support the compute shaders required by " +
                        "the Sentis GPUCompute backend.");
                }
                ResolveControllers();
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
                    new TensorShape(BatchSize, BasketballModelAsset.InputFeatureCount),
                    batchInput);
                worker = new Worker(model, BackendType.GPUCompute);
                BeginWarmup();
            }
            catch (Exception exception)
            {
                FailToLocalBackends("initialization", exception, completePrepared: false);
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
                if (!controller.WantsSentisBatch)
                {
                    throw new InvalidOperationException(
                        $"Player {index + 1} is not configured for SentisGpuBatch.");
                }
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
                pendingOutput.ReadbackRequest();
            }
            schedulerState = SchedulerState.WarmingUp;
        }

        private void PollWarmup()
        {
            if (pendingOutput == null || !pendingOutput.IsReadbackRequestDone())
            {
                return;
            }
            try
            {
                using Tensor<float> warmupOutput = pendingOutput.ReadbackAndClone();
                ValidateOutputTensor(warmupOutput);
                pendingOutput = null;
                AttachControllers();
                accumulator = 0f;
                LastRoundTripMilliseconds = -1f;
                schedulerState = SchedulerState.Running;
            }
            catch (Exception exception)
            {
                FailToLocalBackends("warmup", exception, completePrepared: false);
            }
        }

        private void BeginBatch()
        {
            preparedControllerCount = 0;
            try
            {
                for (int index = 0; index < BatchSize; index++)
                {
                    Span<float> row = batchInput.AsSpan(
                        index * BasketballModelAsset.InputFeatureCount,
                        BasketballModelAsset.InputFeatureCount);
                    if (!controllers[index].PrepareExternalTick(row))
                    {
                        throw new InvalidOperationException(
                            $"Player {index + 1} produced an invalid neural input.");
                    }
                    preparedControllerCount++;
                }

                using (InferenceMarker.Auto())
                using (ScheduleMarker.Auto())
                {
                    inputTensor.Upload(batchInput);
                    worker.Schedule(inputTensor);
                    pendingOutput = worker.PeekOutput(OutputName) as Tensor<float>;
                    ValidateOutputTensor(pendingOutput);
                    pendingOutput.ReadbackRequest();
                }
                inferenceStartTime = Time.realtimeSinceStartupAsDouble;
            }
            catch (Exception exception)
            {
                FailToLocalBackends("schedule", exception, completePrepared: true);
            }
        }

        private bool CompleteBatch()
        {
            try
            {
                using (ReadbackMarker.Auto())
                using (Tensor<float> cpuOutput = pendingOutput.ReadbackAndClone())
                {
                    ValidateOutputTensor(cpuOutput);
                    ReadOnlySpan<float> values = cpuOutput.AsReadOnlySpan();
                    ValidateFinite(values);
                    for (int index = 0; index < BatchSize; index++)
                    {
                        int rowStart = index * CombinedOutputCount;
                        controllers[index].CompleteExternalTick(
                            values.Slice(
                                rowStart,
                                BasketballModelAsset.OutputFeatureCount),
                            values.Slice(
                                rowStart + BasketballModelAsset.OutputFeatureCount,
                                BasketballModelAsset.ExpertCount));
                    }
                }

                pendingOutput = null;
                preparedControllerCount = 0;
                accumulator = Mathf.Max(0f, accumulator - tickInterval);
                LastRoundTripMilliseconds = (float)(
                    (Time.realtimeSinceStartupAsDouble - inferenceStartTime) * 1000.0);
                return true;
            }
            catch (Exception exception)
            {
                FailToLocalBackends("readback", exception, completePrepared: false);
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

        private void FailToLocalBackends(
            string stage,
            Exception exception,
            bool completePrepared)
        {
            if (completePrepared)
            {
                for (int index = 0; index < preparedControllerCount; index++)
                {
                    try
                    {
                        controllers[index]?.CompletePreparedTickLocally();
                    }
                    catch (Exception fallbackException)
                    {
                        Debug.LogException(fallbackException, controllers[index]);
                    }
                }
            }

            pendingOutput = null;
            preparedControllerCount = 0;
            DetachControllers();
            DisposeSentis();
            schedulerState = SchedulerState.Failed;
            Debug.LogWarning(
                $"Basketball Sentis GPU batch failed during {stage}; all players remain on " +
                $"their local Burst fallback. {exception.GetType().Name}: {exception.Message}",
                this);
        }

        private void DisposeSentis()
        {
            worker?.Dispose();
            worker = null;
            inputTensor?.Dispose();
            inputTensor = null;
            pendingOutput = null;
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
