using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball.Tests
{
    public sealed class BasketballBallControllerTests
    {
        private GameObject ballObject;
        private BasketballBallController ball;

        [SetUp]
        public void SetUp()
        {
            ballObject = new GameObject("Ball Handoff Test");
            ball = ballObject.AddComponent<BasketballBallController>();
            ball.SetState(BasketballBallAuthorityState.Controlled);
            ballObject.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
        }

        [TearDown]
        public void TearDown()
        {
            if (ballObject != null)
            {
                Object.DestroyImmediate(ballObject);
            }
        }

        [Test]
        public void ControlledHandoff_DoesNotTeleportToRemoteNeuralPose()
        {
            Vector3 remoteTarget = new(5f, 1f, 0f);

            ball.BeginControlledHandoff(held: false);
            ball.SetControlledPose(
                remoteTarget,
                Quaternion.Euler(0f, 180f, 0f),
                Vector3.zero);

            Assert.That(
                Vector3.Distance(ballObject.transform.position, remoteTarget),
                Is.GreaterThan(0.1f));
        }

        [Test]
        public void ExplicitControlledReset_CancelsPendingHandoffBlend()
        {
            Vector3 resetPosition = new(2f, 0.5f, -1f);
            Quaternion resetRotation = Quaternion.Euler(0f, 45f, 0f);

            ball.BeginControlledHandoff(held: false);
            ball.SetState(BasketballBallAuthorityState.Controlled);
            ball.SetControlledPose(resetPosition, resetRotation, Vector3.zero);

            Assert.That(
                Vector3.Distance(ballObject.transform.position, resetPosition),
                Is.LessThan(1e-5f));
            Assert.That(
                Quaternion.Angle(ballObject.transform.rotation, resetRotation),
                Is.LessThan(1e-4f));
        }
    }
}
