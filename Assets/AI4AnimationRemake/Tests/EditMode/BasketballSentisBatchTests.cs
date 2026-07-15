using System;
using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballSentisBatchTests
    {
        private const string SentisModelPath =
            "Assets/AI4AnimationRemake/Resources/Models/BasketballMoEBatch10.onnx";

        private ModelAsset sentisModel;

        [SetUp]
        public void SetUp()
        {
            sentisModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(SentisModelPath);
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
        [Category("GPU")]
        public void SentisGpuComputeBatch10_ProducesFinitePackedOutput()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPUCompute is not supported on this device.");
            }

            int inputCount = BasketballModelContract.InputFeatureCount;
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

            Model model = ModelLoader.Load(sentisModel);
            using var worker = new Worker(model, BackendType.GPUCompute);
            using var inputTensor = new Tensor<float>(
                new TensorShape(BasketballSentisBatchScheduler.BatchSize, inputCount),
                batchInput);
            worker.Schedule(inputTensor);
            Tensor<float> workerOutput = worker.PeekOutput("batch_output") as Tensor<float>;
            Assert.That(workerOutput, Is.Not.Null);
            using Tensor<float> cpuOutput = workerOutput.ReadbackAndClone();
            Assert.That(
                cpuOutput.count,
                Is.EqualTo(
                    BasketballSentisBatchScheduler.BatchSize *
                    BasketballModelContract.PackedOutputFeatureCount));

            ReadOnlySpan<float> values = cpuOutput.AsReadOnlySpan();
            for (int index = 0; index < values.Length; index++)
            {
                Assert.That(float.IsFinite(values[index]), Is.True,
                    $"GPU output is non-finite at index {index}.");
            }
        }
    }
}
