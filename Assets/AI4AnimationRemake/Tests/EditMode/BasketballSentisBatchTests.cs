using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballSentisBatchTests
    {
        private const string ReferenceModelPath =
            "Assets/AI4AnimationRemake/Models/BasketballModel.asset";
        private const string SentisModelPath =
            "Assets/AI4AnimationRemake/Resources/Models/BasketballMoEBatch3.onnx";

        private BasketballModelAsset referenceModel;
        private ModelAsset sentisModel;

        [SetUp]
        public void SetUp()
        {
            referenceModel = AssetDatabase.LoadAssetAtPath<BasketballModelAsset>(
                ReferenceModelPath);
            sentisModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(SentisModelPath);
            Assert.That(referenceModel, Is.Not.Null,
                $"Missing reference model at {ReferenceModelPath}.");
            Assert.That(sentisModel, Is.Not.Null,
                $"Missing imported Sentis model at {SentisModelPath}.");
        }

        [Test]
        public void SentisModel_HasFixedBatchThreeContract()
        {
            Model model = ModelLoader.Load(sentisModel);
            Assert.That(model.inputs, Has.Count.EqualTo(1));
            Assert.That(model.inputs[0].name, Is.EqualTo("input"));
            Assert.That(model.outputs, Has.Count.EqualTo(1));
            Assert.That(model.outputs[0].name, Is.EqualTo("batch_output"));
        }

        [Test]
        public void SentisCpuBatch3_MatchesThreeReferenceEvaluations()
        {
            CompareSentisWithReference(BackendType.CPU, 3e-4f);
        }

        [Test]
        [Category("GPU")]
        public void SentisGpuComputeBatch3_MatchesThreeReferenceEvaluations()
        {
            CompareSentisWithReference(BackendType.GPUCompute, 1.5e-3f);
        }

        private void CompareSentisWithReference(
            BackendType backendType,
            float absoluteTolerance)
        {
            int inputCount = BasketballModelAsset.InputFeatureCount;
            int outputCount = BasketballModelAsset.OutputFeatureCount;
            int expertCount = BasketballModelAsset.ExpertCount;
            int packedCount = outputCount + expertCount;

            var batchInput = new float[BasketballSentisBatchScheduler.BatchSize * inputCount];
            for (int batch = 0; batch < BasketballSentisBatchScheduler.BatchSize; batch++)
            {
                for (int index = 0; index < inputCount; index++)
                {
                    batchInput[batch * inputCount + index] =
                        0.31f * Mathf.Sin(index * 0.137f + batch * 0.47f) +
                        0.12f * Mathf.Cos(index * 0.053f - batch * 0.29f);
                }
            }

            var expectedOutput = new float[
                BasketballSentisBatchScheduler.BatchSize * outputCount];
            var expectedGating = new float[
                BasketballSentisBatchScheduler.BatchSize * expertCount];
            var rowInput = new float[inputCount];
            var rowOutput = new float[outputCount];
            var rowGating = new float[expertCount];
            using (var reference = new BasketballReferenceBackend())
            {
                reference.Initialize(referenceModel);
                for (int batch = 0; batch < BasketballSentisBatchScheduler.BatchSize; batch++)
                {
                    System.Array.Copy(
                        batchInput,
                        batch * inputCount,
                        rowInput,
                        0,
                        inputCount);
                    reference.Evaluate(rowInput, rowOutput);
                    reference.CopyGatingWeights(rowGating);
                    System.Array.Copy(
                        rowOutput,
                        0,
                        expectedOutput,
                        batch * outputCount,
                        outputCount);
                    System.Array.Copy(
                        rowGating,
                        0,
                        expectedGating,
                        batch * expertCount,
                        expertCount);
                }
            }

            Model model = ModelLoader.Load(sentisModel);
            using var worker = new Worker(model, backendType);
            using var inputTensor = new Tensor<float>(
                new TensorShape(BasketballSentisBatchScheduler.BatchSize, inputCount),
                batchInput);
            worker.Schedule(inputTensor);
            Tensor<float> workerOutput = worker.PeekOutput("batch_output") as Tensor<float>;
            Assert.That(workerOutput, Is.Not.Null);
            using Tensor<float> cpuOutput = workerOutput.ReadbackAndClone();
            Assert.That(
                cpuOutput.count,
                Is.EqualTo(BasketballSentisBatchScheduler.BatchSize * packedCount));

            var actual = cpuOutput.AsReadOnlySpan();
            for (int batch = 0; batch < BasketballSentisBatchScheduler.BatchSize; batch++)
            {
                int actualRow = batch * packedCount;
                int expectedOutputRow = batch * outputCount;
                int expectedGatingRow = batch * expertCount;
                for (int index = 0; index < outputCount; index++)
                {
                    float expected = expectedOutput[expectedOutputRow + index];
                    float tolerance = absoluteTolerance + 2e-4f * Mathf.Abs(expected);
                    Assert.That(
                        actual[actualRow + index],
                        Is.EqualTo(expected).Within(tolerance),
                        $"{backendType} output mismatch at batch {batch}, index {index}.");
                }
                for (int expert = 0; expert < expertCount; expert++)
                {
                    Assert.That(
                        actual[actualRow + outputCount + expert],
                        Is.EqualTo(expectedGating[expectedGatingRow + expert])
                            .Within(2e-4f),
                        $"{backendType} gating mismatch at batch {batch}, expert {expert}.");
                }
            }
        }
    }
}
