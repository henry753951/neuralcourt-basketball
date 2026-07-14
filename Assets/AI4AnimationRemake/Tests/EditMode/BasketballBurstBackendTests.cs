using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballBurstBackendTests
    {
        private const string ModelPath =
            "Assets/AI4AnimationRemake/Models/BasketballModel.asset";

        private BasketballModelAsset model;

        [SetUp]
        public void SetUp()
        {
            model = AssetDatabase.LoadAssetAtPath<BasketballModelAsset>(ModelPath);
            Assert.That(model, Is.Not.Null, $"Missing model wrapper at {ModelPath}.");
        }

        [Test]
        public void BurstBackend_ZeroInputMatchesReference()
        {
            float[] input = new float[BasketballModelAsset.InputFeatureCount];
            CompareBackends(input);
        }

        [Test]
        public void BurstBackend_DeterministicSignalMatchesReference()
        {
            float[] input = new float[BasketballModelAsset.InputFeatureCount];
            for (int index = 0; index < input.Length; index++)
            {
                input[index] =
                    0.35f * Mathf.Sin(index * 0.173f) +
                    0.15f * Mathf.Cos(index * 0.071f);
            }
            CompareBackends(input);
        }

        [Test]
        public void BurstBackend_RepeatedEvaluationIsFiniteAndDeterministic()
        {
            using var backend = new BasketballBurstBackend();
            backend.Initialize(model);
            float[] input = new float[backend.InputSize];
            float[] first = new float[backend.OutputSize];
            float[] second = new float[backend.OutputSize];

            backend.Evaluate(input, first);
            backend.Evaluate(input, second);

            for (int index = 0; index < first.Length; index++)
            {
                Assert.That(float.IsFinite(first[index]), Is.True,
                    $"Non-finite Burst output at index {index}.");
                Assert.That(second[index], Is.EqualTo(first[index]),
                    $"Non-deterministic Burst output at index {index}.");
            }
        }

        private void CompareBackends(float[] input)
        {
            using var reference = new BasketballReferenceBackend();
            using var burst = new BasketballBurstBackend();
            reference.Initialize(model);
            burst.Initialize(model);

            float[] expected = new float[reference.OutputSize];
            float[] actual = new float[burst.OutputSize];
            reference.Evaluate(input, expected);
            burst.Evaluate(input, actual);

            float maximumAbsoluteError = 0f;
            int maximumErrorIndex = -1;
            for (int index = 0; index < expected.Length; index++)
            {
                float error = Mathf.Abs(actual[index] - expected[index]);
                if (error > maximumAbsoluteError)
                {
                    maximumAbsoluteError = error;
                    maximumErrorIndex = index;
                }

                float tolerance = 2e-5f + 2e-5f * Mathf.Abs(expected[index]);
                Assert.That(
                    actual[index],
                    Is.EqualTo(expected[index]).Within(tolerance),
                    $"Burst mismatch at output {index}; max error so far " +
                    $"{maximumAbsoluteError} at {maximumErrorIndex}.");
            }

            float[] referenceGating = new float[BasketballModelAsset.ExpertCount];
            float[] burstGating = new float[BasketballModelAsset.ExpertCount];
            reference.CopyGatingWeights(referenceGating);
            burst.CopyGatingWeights(burstGating);
            for (int index = 0; index < referenceGating.Length; index++)
            {
                Assert.That(
                    burstGating[index],
                    Is.EqualTo(referenceGating[index]).Within(2e-6f),
                    $"Gating mismatch at expert {index}.");
            }
        }
    }
}
