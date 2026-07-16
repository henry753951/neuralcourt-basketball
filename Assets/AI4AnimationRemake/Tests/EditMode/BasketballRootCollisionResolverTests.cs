using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball.Tests
{
    public sealed class BasketballRootCollisionResolverTests
    {
        private GameObject wall;

        [TearDown]
        public void TearDown()
        {
            if (wall != null)
            {
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void ResolveAfterDecode_KeepsActorPoseAndControlledBallInsideWall()
        {
            wall = new GameObject("Boundary Wall");
            wall.layer = 0;
            wall.transform.position = new Vector3(1f, 1f, 0f);
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.2f, 4f, 4f);
            Physics.SyncTransforms();

            BasketballAgentState state = new();
            Vector3 previousPosition = new(0.5f, 1f, 0f);
            Vector3 decodedPosition = new(1.2f, 1f, 0f);
            for (int sample = 0;
                 sample < BasketballAgentState.SampleCount;
                 sample++)
            {
                state.RootPositions[sample] = sample < BasketballAgentState.Pivot
                    ? previousPosition
                    : decodedPosition;
                state.BallPositions[sample] = decodedPosition + Vector3.up;
            }

            state.ActorRootPosition = decodedPosition;
            state.Carrier = true;
            Vector3 boneOffset = new(0.15f, 0.8f, -0.1f);
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                state.BonePositions[bone] = decodedPosition + boneOffset;
            }

            Vector3 ballOffset =
                state.BallPositions[BasketballAgentState.Pivot] -
                decodedPosition;
            BasketballRootCollisionResolver resolver = new(
                0.25f,
                1 << 0);

            resolver.ResolveAfterDecode(state);

            Assert.That(state.ActorRootPosition.x, Is.LessThan(0.9f));
            Assert.That(
                Vector3.Distance(
                    state.BonePositions[0] - state.ActorRootPosition,
                    boneOffset),
                Is.LessThan(1e-5f));
            Assert.That(
                Vector3.Distance(
                    state.BallPositions[BasketballAgentState.Pivot] -
                    state.ActorRootPosition,
                    ballOffset),
                Is.LessThan(1e-5f));
        }
    }
}
