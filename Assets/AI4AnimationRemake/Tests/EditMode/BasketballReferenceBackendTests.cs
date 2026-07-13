using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEditor;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballReferenceBackendTests
    {
        private const string ModelPath = "Assets/AI4AnimationRemake/Models/BasketballModel.asset";
        private const string SemanticHash = "6bbd068181a792373eb73705365aca8a5422548b401706a7610639ba2109c7b0";

        private BasketballModelAsset model;

        [SetUp]
        public void SetUp()
        {
            model = AssetDatabase.LoadAssetAtPath<BasketballModelAsset>(ModelPath);
            Assert.That(model, Is.Not.Null, $"Missing model wrapper at {ModelPath}.");
        }

        [Test]
        public void ModelAsset_HasExactTopologyAndBufferCounts()
        {
            Assert.That(BasketballModelAsset.InputFeatureCount, Is.EqualTo(864));
            Assert.That(BasketballModelAsset.MainFeatureCount, Is.EqualTo(734));
            Assert.That(BasketballModelAsset.GatingFeatureCount, Is.EqualTo(130));
            Assert.That(BasketballModelAsset.OutputFeatureCount, Is.EqualTo(588));
            Assert.That(BasketballModelAsset.ExpertCount, Is.EqualTo(8));
            Assert.That(model.Validate(out string reason), Is.True, reason);
            Assert.That(model.Source.Buffers, Has.Length.EqualTo(BasketballModelAsset.BufferCount));
        }

        [Test]
        public void ModelAsset_FloatBuffersMatchOriginalSemanticHash()
        {
            string actual = ComputeSemanticHash(model.Source);
            Assert.That(actual, Is.EqualTo(SemanticHash));
        }

        [Test]
        public void ReferenceBackend_ZeroInputMatchesIndependentReferenceSamples()
        {
            var backend = new BasketballReferenceBackend();
            backend.Initialize(model);

            var input = new float[backend.InputSize];
            var output = new float[backend.OutputSize];
            backend.Evaluate(input, output);

            int[] indices =
            {
                0, 1, 2, 3, 4, 5, 10, 15, 16, 100,
                200, 300, 400, 430, 443, 447, 448, 500, 587
            };
            float[] expected =
            {
                -0.00448077219f, 6.29185152f, -0.0511422306f,
                -0.130657688f, -1.58323503f, -0.134743586f,
                -0.288830936f, -0.127709672f, -0.0051272721f,
                0.0657345206f, 0.521595836f, 0.0561242402f,
                -0.235161066f, 0.227986217f, 0.714810312f,
                0.0931893289f, 1.85985768f, 0.125214756f,
                -0.425468177f
            };

            for (int i = 0; i < indices.Length; i++)
            {
                Assert.That(
                    output[indices[i]],
                    Is.EqualTo(expected[i]).Within(2e-5f),
                    $"Output mismatch at index {indices[i]}.");
            }
        }

        [Test]
        public void ReferenceBackend_RepeatedEvaluationIsFiniteAndDeterministic()
        {
            var backend = new BasketballReferenceBackend();
            backend.Initialize(model);

            var input = new float[backend.InputSize];
            var first = new float[backend.OutputSize];
            var second = new float[backend.OutputSize];

            backend.Evaluate(input, first);
            backend.Evaluate(input, second);

            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(float.IsNaN(first[i]), Is.False, $"NaN at output {i}.");
                Assert.That(float.IsInfinity(first[i]), Is.False, $"Infinity at output {i}.");
                Assert.That(second[i], Is.EqualTo(first[i]), $"Non-deterministic output at {i}.");
            }
        }

        [Test]
        public void ReferenceBackend_RejectsWrongBufferSizes()
        {
            var backend = new BasketballReferenceBackend();
            backend.Initialize(model);

            Assert.Throws<ArgumentException>(() =>
                backend.Evaluate(new float[backend.InputSize - 1], new float[backend.OutputSize]));
            Assert.Throws<ArgumentException>(() =>
                backend.Evaluate(new float[backend.InputSize], new float[backend.OutputSize - 1]));
        }

        private static string ComputeSemanticHash(global::Parameters parameters)
        {
            using SHA256 sha = SHA256.Create();
            using var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
            using var writer = new BinaryWriter(crypto, new UTF8Encoding(false), leaveOpen: true);

            for (int i = 0; i < parameters.Buffers.Length; i++)
            {
                global::Parameters.Buffer buffer = parameters.Buffers[i];
                byte[] id = Encoding.UTF8.GetBytes(buffer.ID);
                writer.Write(id.Length);
                writer.Write(id);
                writer.Write(buffer.Values.Length);
                for (int valueIndex = 0; valueIndex < buffer.Values.Length; valueIndex++)
                {
                    writer.Write(buffer.Values[valueIndex]);
                }
            }

            writer.Flush();
            crypto.FlushFinalBlock();
            return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
