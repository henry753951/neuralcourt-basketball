using System;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballReferenceBackend : IBasketballInferenceBackend
    {
        private static readonly ProfilerMarker InferenceMarker = new("Basketball.Inference");

        private readonly float[][] expertW0 = new float[BasketballModelAsset.ExpertCount][];
        private readonly float[][] expertB0 = new float[BasketballModelAsset.ExpertCount][];
        private readonly float[][] expertW1 = new float[BasketballModelAsset.ExpertCount][];
        private readonly float[][] expertB1 = new float[BasketballModelAsset.ExpertCount][];
        private readonly float[][] expertW2 = new float[BasketballModelAsset.ExpertCount][];
        private readonly float[][] expertB2 = new float[BasketballModelAsset.ExpertCount][];

        private float[] xMean;
        private float[] xStd;
        private float[] yMean;
        private float[] yStd;

        private float[] gatingW0;
        private float[] gatingB0;
        private float[] gatingW1;
        private float[] gatingB1;
        private float[] gatingW2;
        private float[] gatingB2;

        private float[] normalizedInput;
        private float[] gatingHidden0;
        private float[] gatingHidden1;
        private float[] gatingWeights;
        private float[] blendedW0;
        private float[] blendedB0;
        private float[] blendedW1;
        private float[] blendedB1;
        private float[] blendedW2;
        private float[] blendedB2;
        private float[] hidden0;
        private float[] hidden1;
        private float[] normalizedOutput;

        private bool initialized;

        public int InputSize => BasketballModelAsset.InputFeatureCount;
        public int OutputSize => BasketballModelAsset.OutputFeatureCount;
        public bool IsInitialized => initialized;

        public void Initialize(BasketballModelAsset model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (!model.Validate(out string reason))
            {
                throw new InvalidOperationException($"Invalid BasketballModelAsset: {reason}");
            }

            xMean = Require(model, "Xmean");
            xStd = Require(model, "Xstd");
            yMean = Require(model, "Ymean");
            yStd = Require(model, "Ystd");

            gatingW0 = Require(model, "wc000_w");
            gatingB0 = Require(model, "wc000_b");
            gatingW1 = Require(model, "wc010_w");
            gatingB1 = Require(model, "wc010_b");
            gatingW2 = Require(model, "wc020_w");
            gatingB2 = Require(model, "wc020_b");

            for (int expert = 0; expert < BasketballModelAsset.ExpertCount; expert++)
            {
                expertW0[expert] = Require(model, $"wc10{expert}_w");
                expertB0[expert] = Require(model, $"wc10{expert}_b");
                expertW1[expert] = Require(model, $"wc11{expert}_w");
                expertB1[expert] = Require(model, $"wc11{expert}_b");
                expertW2[expert] = Require(model, $"wc12{expert}_w");
                expertB2[expert] = Require(model, $"wc12{expert}_b");
            }

            normalizedInput = new float[InputSize];
            gatingHidden0 = new float[128];
            gatingHidden1 = new float[128];
            gatingWeights = new float[BasketballModelAsset.ExpertCount];
            blendedW0 = new float[512 * BasketballModelAsset.MainFeatureCount];
            blendedB0 = new float[512];
            blendedW1 = new float[512 * 512];
            blendedB1 = new float[512];
            blendedW2 = new float[OutputSize * 512];
            blendedB2 = new float[OutputSize];
            hidden0 = new float[512];
            hidden1 = new float[512];
            normalizedOutput = new float[OutputSize];
            initialized = true;
        }

        public void Evaluate(ReadOnlySpan<float> input, Span<float> output)
        {
            if (!initialized)
            {
                throw new InvalidOperationException("The basketball inference backend is not initialized.");
            }

            if (input.Length != InputSize)
            {
                throw new ArgumentException($"Expected {InputSize} input floats but received {input.Length}.", nameof(input));
            }

            if (output.Length < OutputSize)
            {
                throw new ArgumentException($"Expected at least {OutputSize} output floats but received {output.Length}.", nameof(output));
            }

            using (InferenceMarker.Auto())
            {
                EvaluateInternal(input, output);
            }
        }

        public void CopyGatingWeights(Span<float> destination)
        {
            if (!initialized)
            {
                destination.Clear();
                return;
            }

            int count = Mathf.Min(destination.Length, gatingWeights.Length);
            gatingWeights.AsSpan(0, count).CopyTo(destination);
            if (destination.Length > count)
            {
                destination.Slice(count).Clear();
            }
        }

        private void EvaluateInternal(ReadOnlySpan<float> input, Span<float> output)
        {
            for (int i = 0; i < InputSize; i++)
            {
                normalizedInput[i] = (input[i] - xMean[i]) / xStd[i];
            }

            ReadOnlySpan<float> gatingInput = normalizedInput.AsSpan(
                BasketballModelAsset.MainFeatureCount,
                BasketballModelAsset.GatingFeatureCount);

            Dense(gatingInput, gatingW0, gatingB0, gatingHidden0, BasketballModelAsset.GatingFeatureCount, 128);
            Elu(gatingHidden0);
            Dense(gatingHidden0, gatingW1, gatingB1, gatingHidden1, 128, 128);
            Elu(gatingHidden1);
            Dense(gatingHidden1, gatingW2, gatingB2, gatingWeights, 128, BasketballModelAsset.ExpertCount);
            SoftmaxStable(gatingWeights);

            Blend(expertW0, gatingWeights, blendedW0);
            Blend(expertB0, gatingWeights, blendedB0);
            Blend(expertW1, gatingWeights, blendedW1);
            Blend(expertB1, gatingWeights, blendedB1);
            Blend(expertW2, gatingWeights, blendedW2);
            Blend(expertB2, gatingWeights, blendedB2);

            Dense(
                normalizedInput.AsSpan(0, BasketballModelAsset.MainFeatureCount),
                blendedW0,
                blendedB0,
                hidden0,
                BasketballModelAsset.MainFeatureCount,
                512);
            Elu(hidden0);

            Dense(hidden0, blendedW1, blendedB1, hidden1, 512, 512);
            Elu(hidden1);
            Dense(hidden1, blendedW2, blendedB2, normalizedOutput, 512, OutputSize);

            for (int i = 0; i < OutputSize; i++)
            {
                output[i] = normalizedOutput[i] * yStd[i] + yMean[i];
            }
        }

        private static float[] Require(BasketballModelAsset model, string id)
        {
            if (!model.TryGetBuffer(id, out float[] values))
            {
                throw new InvalidOperationException($"Required model buffer '{id}' is missing.");
            }

            return values;
        }

        private static void Dense(
            ReadOnlySpan<float> input,
            ReadOnlySpan<float> weights,
            ReadOnlySpan<float> bias,
            Span<float> output,
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

        private static void Blend(float[][] experts, ReadOnlySpan<float> weights, Span<float> output)
        {
            float[] e0 = experts[0];
            float[] e1 = experts[1];
            float[] e2 = experts[2];
            float[] e3 = experts[3];
            float[] e4 = experts[4];
            float[] e5 = experts[5];
            float[] e6 = experts[6];
            float[] e7 = experts[7];

            for (int i = 0; i < output.Length; i++)
            {
                output[i] =
                    weights[0] * e0[i] +
                    weights[1] * e1[i] +
                    weights[2] * e2[i] +
                    weights[3] * e3[i] +
                    weights[4] * e4[i] +
                    weights[5] * e5[i] +
                    weights[6] * e6[i] +
                    weights[7] * e7[i];
            }
        }

        private static void Elu(Span<float> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                values[i] = Mathf.Max(value, 0f) + Mathf.Exp(Mathf.Min(value, 0f)) - 1f;
            }
        }

        private static void SoftmaxStable(Span<float> values)
        {
            float maximum = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                maximum = Mathf.Max(maximum, values[i]);
            }

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                // Softmax(x) == Softmax(x - max(x)). The max shift preserves the
                // model definition while preventing float overflow in exp().
                float value = Mathf.Exp(values[i] - maximum);
                values[i] = value;
                sum += value;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] /= sum;
            }
        }
    }
}
