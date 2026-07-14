using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Synchronous Burst reference-compatible backend. It preserves the original
    /// per-tick model ordering while compiling the scalar hot loops to optimized
    /// native CPU code. Multi-agent scheduling remains a later phase.
    /// </summary>
    public sealed class BasketballBurstBackend : IBasketballInferenceBackend
    {
        private const int HiddenCount = 512;
        private const int GatingHiddenCount = 128;
        private const int BlendBatchSize = 256;
        private static readonly ProfilerMarker InferenceMarker =
            new("Basketball.Inference");
        private static readonly ProfilerMarker BurstMarker =
            new("Basketball.Inference.Burst");
        private static readonly ProfilerMarker GatingMarker =
            new("Basketball.Inference.Burst.Gating");
        private static readonly ProfilerMarker BlendMarker =
            new("Basketball.Inference.Burst.Blend");
        private static readonly ProfilerMarker DenseMarker =
            new("Basketball.Inference.Burst.Dense");

        private BasketballBurstModelData modelData;
        private BasketballModelAsset modelAsset;

        private NativeArray<float> nativeInput;
        private NativeArray<float> nativeOutput;
        private NativeArray<float> normalizedInput;
        private NativeArray<float> gatingHidden0;
        private NativeArray<float> gatingHidden1;
        private NativeArray<float> gatingWeights;
        private NativeArray<float> blendedW0;
        private NativeArray<float> blendedB0;
        private NativeArray<float> blendedW1;
        private NativeArray<float> blendedB1;
        private NativeArray<float> blendedW2;
        private NativeArray<float> blendedB2;
        private NativeArray<float> hidden0;
        private NativeArray<float> hidden1;
        private NativeArray<float> normalizedOutput;
        private bool initialized;

        public string Name => BurstCompiler.IsEnabled ? "BURST CPU" : "BURST DISABLED";
        public int InputSize => BasketballModelAsset.InputFeatureCount;
        public int OutputSize => BasketballModelAsset.OutputFeatureCount;

        public void Initialize(BasketballModelAsset model)
        {
            if (initialized)
            {
                throw new InvalidOperationException("The Burst backend is already initialized.");
            }

            modelAsset = model;
            modelData = BasketballBurstModelCache.Acquire(model);
            try
            {
                nativeInput = Allocate(InputSize);
                nativeOutput = Allocate(OutputSize);
                normalizedInput = Allocate(InputSize);
                gatingHidden0 = Allocate(GatingHiddenCount);
                gatingHidden1 = Allocate(GatingHiddenCount);
                gatingWeights = Allocate(BasketballModelAsset.ExpertCount);
                blendedW0 = Allocate(HiddenCount * BasketballModelAsset.MainFeatureCount);
                blendedB0 = Allocate(HiddenCount);
                blendedW1 = Allocate(HiddenCount * HiddenCount);
                blendedB1 = Allocate(HiddenCount);
                blendedW2 = Allocate(OutputSize * HiddenCount);
                blendedB2 = Allocate(OutputSize);
                hidden0 = Allocate(HiddenCount);
                hidden1 = Allocate(HiddenCount);
                normalizedOutput = Allocate(OutputSize);
                initialized = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Evaluate(ReadOnlySpan<float> input, Span<float> output)
        {
            if (!initialized)
            {
                throw new InvalidOperationException("The Burst backend is not initialized.");
            }
            if (input.Length != InputSize)
            {
                throw new ArgumentException(
                    $"Expected {InputSize} input floats but received {input.Length}.",
                    nameof(input));
            }
            if (output.Length < OutputSize)
            {
                throw new ArgumentException(
                    $"Expected at least {OutputSize} output floats but received {output.Length}.",
                    nameof(output));
            }

            for (int index = 0; index < InputSize; index++)
            {
                nativeInput[index] = input[index];
            }

            GatingJob gatingJob = new()
            {
                Input = nativeInput,
                XMean = modelData.XMean,
                XStd = modelData.XStd,
                GatingW0 = modelData.GatingW0,
                GatingB0 = modelData.GatingB0,
                GatingW1 = modelData.GatingW1,
                GatingB1 = modelData.GatingB1,
                GatingW2 = modelData.GatingW2,
                GatingB2 = modelData.GatingB2,
                NormalizedInput = normalizedInput,
                GatingHidden0 = gatingHidden0,
                GatingHidden1 = gatingHidden1,
                GatingWeights = gatingWeights
            };

            using (InferenceMarker.Auto())
            using (BurstMarker.Auto())
            {
                using (GatingMarker.Auto())
                {
                    gatingJob.Run();
                }

                using (BlendMarker.Auto())
                {
                    JobHandle blendW0 = ScheduleBlend(
                        modelData.ExpertW0, blendedW0, gatingWeights);
                    JobHandle blendW1 = ScheduleBlend(
                        modelData.ExpertW1, blendedW1, gatingWeights);
                    JobHandle blendW2 = ScheduleBlend(
                        modelData.ExpertW2, blendedW2, gatingWeights);
                    JobHandle blendBias = new BlendBiasJob
                    {
                        ExpertB0 = modelData.ExpertB0,
                        ExpertB1 = modelData.ExpertB1,
                        ExpertB2 = modelData.ExpertB2,
                        Weights = gatingWeights,
                        BlendedB0 = blendedB0,
                        BlendedB1 = blendedB1,
                        BlendedB2 = blendedB2
                    }.Schedule();

                    JobHandle combined = JobHandle.CombineDependencies(blendW0, blendW1);
                    combined = JobHandle.CombineDependencies(combined, blendW2);
                    combined = JobHandle.CombineDependencies(combined, blendBias);
                    combined.Complete();
                }

                DenseNetworkJob denseJob = new()
                {
                    NormalizedInput = normalizedInput,
                    BlendedW0 = blendedW0,
                    BlendedB0 = blendedB0,
                    BlendedW1 = blendedW1,
                    BlendedB1 = blendedB1,
                    BlendedW2 = blendedW2,
                    BlendedB2 = blendedB2,
                    Hidden0 = hidden0,
                    Hidden1 = hidden1,
                    NormalizedOutput = normalizedOutput,
                    YMean = modelData.YMean,
                    YStd = modelData.YStd,
                    Output = nativeOutput
                };
                using (DenseMarker.Auto())
                {
                    denseJob.Run();
                }
            }

            for (int index = 0; index < OutputSize; index++)
            {
                output[index] = nativeOutput[index];
            }
        }

        private static JobHandle ScheduleBlend(
            NativeArray<float> experts,
            NativeArray<float> output,
            NativeArray<float> weights)
        {
            return new BlendJob
            {
                Experts = experts,
                Weights = weights,
                Output = output
            }.Schedule(output.Length, BlendBatchSize);
        }

        public void CopyGatingWeights(Span<float> destination)
        {
            if (!initialized)
            {
                destination.Clear();
                return;
            }

            int count = math.min(destination.Length, gatingWeights.Length);
            for (int index = 0; index < count; index++)
            {
                destination[index] = gatingWeights[index];
            }
            if (destination.Length > count)
            {
                destination.Slice(count).Clear();
            }
        }

        public void Dispose()
        {
            DisposeArray(ref nativeInput);
            DisposeArray(ref nativeOutput);
            DisposeArray(ref normalizedInput);
            DisposeArray(ref gatingHidden0);
            DisposeArray(ref gatingHidden1);
            DisposeArray(ref gatingWeights);
            DisposeArray(ref blendedW0);
            DisposeArray(ref blendedB0);
            DisposeArray(ref blendedW1);
            DisposeArray(ref blendedB1);
            DisposeArray(ref blendedW2);
            DisposeArray(ref blendedB2);
            DisposeArray(ref hidden0);
            DisposeArray(ref hidden1);
            DisposeArray(ref normalizedOutput);

            if (modelData != null)
            {
                BasketballBurstModelCache.Release(modelAsset, modelData);
                modelData = null;
            }
            modelAsset = null;
            initialized = false;
        }

        private static NativeArray<float> Allocate(int length) => new(
            length,
            Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);

        private static void DisposeArray(ref NativeArray<float> values)
        {
            if (values.IsCreated)
            {
                values.Dispose();
            }
            values = default;
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High,
            CompileSynchronously = true)]
        private struct GatingJob : IJob
        {
            [ReadOnly] public NativeArray<float> Input;

            [ReadOnly] public NativeArray<float> XMean;
            [ReadOnly] public NativeArray<float> XStd;
            [ReadOnly] public NativeArray<float> GatingW0;
            [ReadOnly] public NativeArray<float> GatingB0;
            [ReadOnly] public NativeArray<float> GatingW1;
            [ReadOnly] public NativeArray<float> GatingB1;
            [ReadOnly] public NativeArray<float> GatingW2;
            [ReadOnly] public NativeArray<float> GatingB2;

            public NativeArray<float> NormalizedInput;
            public NativeArray<float> GatingHidden0;
            public NativeArray<float> GatingHidden1;
            public NativeArray<float> GatingWeights;

            public void Execute()
            {
                for (int index = 0; index < BasketballModelAsset.InputFeatureCount; index++)
                {
                    NormalizedInput[index] = (Input[index] - XMean[index]) / XStd[index];
                }

                Dense(
                    NormalizedInput,
                    BasketballModelAsset.MainFeatureCount,
                    GatingW0,
                    GatingB0,
                    GatingHidden0,
                    BasketballModelAsset.GatingFeatureCount,
                    GatingHiddenCount);
                Elu(GatingHidden0);
                Dense(
                    GatingHidden0, 0, GatingW1, GatingB1, GatingHidden1,
                    GatingHiddenCount, GatingHiddenCount);
                Elu(GatingHidden1);
                Dense(
                    GatingHidden1, 0, GatingW2, GatingB2, GatingWeights,
                    GatingHiddenCount, BasketballModelAsset.ExpertCount);
                SoftmaxStable(GatingWeights);
            }

            private static void Dense(
                NativeArray<float> input,
                int inputOffset,
                NativeArray<float> weights,
                NativeArray<float> bias,
                NativeArray<float> output,
                int inputCount,
                int outputCount)
            {
                for (int row = 0; row < outputCount; row++)
                {
                    float sum = bias[row];
                    int weightOffset = row * inputCount;
                    for (int column = 0; column < inputCount; column++)
                    {
                        sum += weights[weightOffset + column] *
                            input[inputOffset + column];
                    }
                    output[row] = sum;
                }
            }

            private static void Elu(NativeArray<float> values)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    float value = values[index];
                    values[index] = math.max(value, 0f) +
                        math.exp(math.min(value, 0f)) - 1f;
                }
            }

            private static void SoftmaxStable(NativeArray<float> values)
            {
                float maximum = values[0];
                for (int index = 1; index < values.Length; index++)
                {
                    maximum = math.max(maximum, values[index]);
                }

                float sum = 0f;
                for (int index = 0; index < values.Length; index++)
                {
                    float value = math.exp(values[index] - maximum);
                    values[index] = value;
                    sum += value;
                }
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] /= sum;
                }
            }
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High,
            CompileSynchronously = true)]
        private struct BlendJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Experts;
            [ReadOnly] public NativeArray<float> Weights;
            [WriteOnly] public NativeArray<float> Output;

            public void Execute(int index)
            {
                int expertLength = Output.Length;
                Output[index] =
                    Weights[0] * Experts[index] +
                    Weights[1] * Experts[expertLength + index] +
                    Weights[2] * Experts[2 * expertLength + index] +
                    Weights[3] * Experts[3 * expertLength + index] +
                    Weights[4] * Experts[4 * expertLength + index] +
                    Weights[5] * Experts[5 * expertLength + index] +
                    Weights[6] * Experts[6 * expertLength + index] +
                    Weights[7] * Experts[7 * expertLength + index];
            }
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High,
            CompileSynchronously = true)]
        private struct BlendBiasJob : IJob
        {
            [ReadOnly] public NativeArray<float> ExpertB0;
            [ReadOnly] public NativeArray<float> ExpertB1;
            [ReadOnly] public NativeArray<float> ExpertB2;
            [ReadOnly] public NativeArray<float> Weights;
            [WriteOnly] public NativeArray<float> BlendedB0;
            [WriteOnly] public NativeArray<float> BlendedB1;
            [WriteOnly] public NativeArray<float> BlendedB2;

            public void Execute()
            {
                Blend(ExpertB0, BlendedB0);
                Blend(ExpertB1, BlendedB1);
                Blend(ExpertB2, BlendedB2);
            }

            private void Blend(
                NativeArray<float> experts,
                NativeArray<float> output)
            {
                int expertLength = output.Length;
                for (int index = 0; index < expertLength; index++)
                {
                    output[index] =
                        Weights[0] * experts[index] +
                        Weights[1] * experts[expertLength + index] +
                        Weights[2] * experts[2 * expertLength + index] +
                        Weights[3] * experts[3 * expertLength + index] +
                        Weights[4] * experts[4 * expertLength + index] +
                        Weights[5] * experts[5 * expertLength + index] +
                        Weights[6] * experts[6 * expertLength + index] +
                        Weights[7] * experts[7 * expertLength + index];
                }
            }
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High,
            CompileSynchronously = true)]
        private struct DenseNetworkJob : IJob
        {
            [ReadOnly] public NativeArray<float> NormalizedInput;
            [ReadOnly] public NativeArray<float> BlendedW0;
            [ReadOnly] public NativeArray<float> BlendedB0;
            [ReadOnly] public NativeArray<float> BlendedW1;
            [ReadOnly] public NativeArray<float> BlendedB1;
            [ReadOnly] public NativeArray<float> BlendedW2;
            [ReadOnly] public NativeArray<float> BlendedB2;
            [ReadOnly] public NativeArray<float> YMean;
            [ReadOnly] public NativeArray<float> YStd;

            public NativeArray<float> Hidden0;
            public NativeArray<float> Hidden1;
            public NativeArray<float> NormalizedOutput;
            [WriteOnly] public NativeArray<float> Output;

            public void Execute()
            {
                Dense(
                    NormalizedInput, BlendedW0, BlendedB0, Hidden0,
                    BasketballModelAsset.MainFeatureCount, HiddenCount);
                Elu(Hidden0);
                Dense(
                    Hidden0, BlendedW1, BlendedB1, Hidden1,
                    HiddenCount, HiddenCount);
                Elu(Hidden1);
                Dense(
                    Hidden1, BlendedW2, BlendedB2, NormalizedOutput,
                    HiddenCount, BasketballModelAsset.OutputFeatureCount);

                for (int index = 0;
                    index < BasketballModelAsset.OutputFeatureCount;
                    index++)
                {
                    Output[index] = NormalizedOutput[index] * YStd[index] + YMean[index];
                }
            }

            private static void Dense(
                NativeArray<float> input,
                NativeArray<float> weights,
                NativeArray<float> bias,
                NativeArray<float> output,
                int inputCount,
                int outputCount)
            {
                for (int row = 0; row < outputCount; row++)
                {
                    float sum = bias[row];
                    int weightOffset = row * inputCount;
                    for (int column = 0; column < inputCount; column++)
                    {
                        sum += weights[weightOffset + column] * input[column];
                    }
                    output[row] = sum;
                }
            }

            private static void Elu(NativeArray<float> values)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    float value = values[index];
                    values[index] = math.max(value, 0f) +
                        math.exp(math.min(value, 0f)) - 1f;
                }
            }
        }
    }
}
