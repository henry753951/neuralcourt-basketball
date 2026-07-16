using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball.Tests
{
    public sealed class BasketballBallTrajectoryPredictorTests
    {
        [Test]
        public void PlaneContact_PredictsBallCenterAtRadiusAboveCourt()
        {
            bool found = BasketballBallTrajectoryPredictor.TryPredictPlaneContact(
                new Vector3(0f, 2f, 0f),
                new Vector3(3f, 2f, 0f),
                new Vector3(0f, -10f, 0f),
                Vector3.zero,
                Vector3.up,
                0.125f,
                3f,
                out Vector3 contact,
                out float time);

            Assert.That(found, Is.True);
            Assert.That(time, Is.EqualTo(0.844f).Within(0.002f));
            Assert.That(contact.x, Is.EqualTo(2.533f).Within(0.01f));
            Assert.That(contact.y, Is.EqualTo(0.125f).Within(0.0001f));
        }

        [Test]
        public void PlaneContact_RejectsContactBeyondPredictionHorizon()
        {
            bool found = BasketballBallTrajectoryPredictor.TryPredictPlaneContact(
                new Vector3(0f, 10f, 0f),
                Vector3.zero,
                new Vector3(0f, -9.81f, 0f),
                Vector3.zero,
                Vector3.up,
                0.125f,
                0.2f,
                out _,
                out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void Intercept_ReturnsEarliestSamplePlayerCanReach()
        {
            bool found = BasketballBallTrajectoryPredictor
                .TryFindEarliestReachableIntercept(
                    new Vector3(2f, 0f, 0f),
                    4f,
                    0.05f,
                    0.5f,
                    new Vector3(0f, 1.2f, 0f),
                    new Vector3(4f, 0f, 0f),
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.up,
                    0.25f,
                    2.3f,
                    1f,
                    10,
                    0.05f,
                    out Vector3 target,
                    out float time,
                    out float margin);

            Assert.That(found, Is.True);
            Assert.That(time, Is.GreaterThan(0f));
            Assert.That(time, Is.LessThanOrEqualTo(1f));
            Assert.That(target.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(margin, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void Intercept_RejectsPlayerThatCannotReachFlightPathInTime()
        {
            bool found = BasketballBallTrajectoryPredictor
                .TryFindEarliestReachableIntercept(
                    new Vector3(20f, 0f, 0f),
                    3f,
                    0.15f,
                    0.5f,
                    new Vector3(0f, 1.2f, 0f),
                    new Vector3(4f, 0f, 0f),
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.up,
                    0.25f,
                    2.3f,
                    1f,
                    10,
                    0.05f,
                    out _,
                    out _,
                    out _);

            Assert.That(found, Is.False);
        }
    }
}
