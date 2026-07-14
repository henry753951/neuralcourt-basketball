using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballClosedLoopContractTests
    {
        [Test]
        public void BallAuthority_SeparatesNeuralAndPhysicsOwnership()
        {
            var gameObject = new GameObject("BallAuthorityTest");
            try
            {
                gameObject.AddComponent<SphereCollider>();
                Rigidbody body = gameObject.AddComponent<Rigidbody>();
                BasketballBallController ball = gameObject.AddComponent<BasketballBallController>();

                ball.SetState(BasketballBallAuthorityState.Controlled);
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.useGravity, Is.False);

                ball.SetState(BasketballBallAuthorityState.FreePhysics);
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.useGravity, Is.True);

                ball.BeginReacquire();
                Assert.That(ball.State, Is.EqualTo(BasketballBallAuthorityState.Reacquiring));
                Assert.That(body.isKinematic, Is.True);
                ball.CompleteReacquire(held: true);
                Assert.That(ball.State, Is.EqualTo(BasketballBallAuthorityState.Held));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FeatureBuilder_WritesExactOriginalLayout()
        {
            BasketballAgentState state = CreateIdentityState();
            var input = new float[BasketballModelAsset.InputFeatureCount];

            new BasketballFeatureBuilder().Build(state, input);

            Assert.That(input.Length, Is.EqualTo(864));
            for (int index = 0; index < input.Length; index++)
            {
                Assert.That(float.IsFinite(input[index]), Is.True, $"Input {index} is not finite.");
            }
            for (int index = 708; index <= 733; index++)
            {
                Assert.That(input[index], Is.EqualTo(BasketballAgentState.InteractionRadius));
            }
        }

        [Test]
        public void FeatureBuilder_ReconstructsLegacyRivalBlockWithoutChangingLayout()
        {
            BasketballAgentState state = CreateIdentityState();
            BasketballAgentState rival = CreateIdentityState();
            state.Rival = rival;
            for (int sample = 0; sample < BasketballAgentState.SampleCount; sample++)
            {
                rival.RootPositions[sample] = Vector3.right;
                rival.RootRotations[sample] = Quaternion.identity;
                rival.RootVelocities[sample] = 2f * Vector3.forward;
            }
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                rival.BonePositions[bone] = 0.5f * Vector3.right;
            }

            var input = new float[BasketballModelAsset.InputFeatureCount];
            new BasketballFeatureBuilder().Build(state, input);

            Assert.That(input[617], Is.EqualTo(1f));
            Assert.That(input[630], Is.EqualTo(4f).Within(1e-5f));
            Assert.That(input[631], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(input[632], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(input[633], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(input[634], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(input[635], Is.EqualTo(2f).Within(1e-5f));
            for (int index = 708; index <= 733; index++)
            {
                Assert.That(input[index], Is.EqualTo(0.5f).Within(1e-5f));
            }
        }

        [Test]
        public void OutputDecoder_ConsumesExactContractWithoutAllocatingStateArrays()
        {
            BasketballAgentState state = CreateIdentityState();
            Vector3[] bonePositions = state.BonePositions;
            Quaternion[] boneRotations = state.BoneRotations;
            var output = new float[BasketballModelAsset.OutputFeatureCount];

            new BasketballOutputDecoder().Decode(state, output);

            Assert.That(state.TickCount, Is.EqualTo(1));
            Assert.That(state.BonePositions, Is.SameAs(bonePositions));
            Assert.That(state.BoneRotations, Is.SameAs(boneRotations));
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                Assert.That(float.IsFinite(state.BonePositions[bone].x), Is.True);
                Assert.That(float.IsFinite(state.BoneRotations[bone].w), Is.True);
            }
        }

        private static BasketballAgentState CreateIdentityState()
        {
            var state = new BasketballAgentState();
            for (int sample = 0; sample < BasketballAgentState.SampleCount; sample++)
            {
                state.RootRotations[sample] = Quaternion.identity;
                state.BallRotations[sample] = Quaternion.identity;
                state.Pivots[sample] = Vector3.forward;
                state.Styles[BasketballAgentState.StyleIndex(sample, 0)] = 1f;
                state.Styles[BasketballAgentState.StyleIndex(sample, 2)] = 1f;
            }
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                state.BoneRotations[bone] = Quaternion.identity;
            }
            return state;
        }
    }
}
