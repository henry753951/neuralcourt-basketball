using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballCourtShotTests
    {
        private GameObject root;
        private BasketballCourt court;
        private BasketballHoop positiveHoop;
        private BasketballHoop negativeHoop;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Court Test Root");
            court = root.AddComponent<BasketballCourt>();
            positiveHoop = CreateHoop("Positive Z Hoop", 0, new Vector3(0f, 3.05f, 12.463f));
            negativeHoop = CreateHoop("Negative Z Hoop", 1, new Vector3(0f, 3.05f, -12.463f));
            court.Configure(positiveHoop, negativeHoop);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Court_SelectsTheCorrectAttackHoopForEachTeam()
        {
            Assert.That(court.GetAttackHoop(0), Is.SameAs(positiveHoop));
            Assert.That(court.GetAttackHoop(1), Is.SameAs(negativeHoop));
        }

        [Test]
        public void ThreePointClassification_UsesReleasePositionAndFibaGeometry()
        {
            Assert.That(
                court.IsThreePoint(new Vector3(0f, 2f, 6.5f), positiveHoop),
                Is.False,
                "A release inside the top arc should be worth two points.");
            Assert.That(
                court.IsThreePoint(new Vector3(0f, 2f, 5.5f), positiveHoop),
                Is.True,
                "A release outside the 6.75 m arc should be worth three points.");
            Assert.That(
                court.IsThreePoint(new Vector3(6.7f, 2f, 11.2f), positiveHoop),
                Is.True,
                "A release beyond the 6.6 m corner line should be worth three points.");
            Assert.That(
                court.IsThreePoint(new Vector3(6.5f, 2f, 11.2f), positiveHoop),
                Is.False,
                "A release inside the corner line should be worth two points.");
        }

        [Test]
        public void CanonicalFrame_UsesHoopAxisWhenLegacyCourtRootIsRotated()
        {
            root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            positiveHoop.RimCenterTransform.position =
                new Vector3(0f, 3.05f, 12.463f);
            negativeHoop.RimCenterTransform.position =
                new Vector3(0f, 3.05f, -12.463f);

            Assert.That(Vector3.Dot(court.CourtForward, Vector3.forward),
                Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(court.CourtRight, Vector3.right),
                Is.GreaterThan(0.999f));

            Vector3 worldPoint = new(2.25f, 1.4f, 5.5f);
            Vector3 courtPoint = court.WorldToCourtPoint(worldPoint);
            Assert.That(courtPoint.x, Is.EqualTo(2.25f).Within(0.001f));
            Assert.That(courtPoint.y, Is.EqualTo(1.4f).Within(0.001f));
            Assert.That(courtPoint.z, Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(Vector3.Distance(
                    court.CourtToWorldPoint(courtPoint),
                    worldPoint),
                Is.LessThan(0.001f));
            Assert.That(court.IsThreePoint(worldPoint, positiveHoop), Is.True);
        }

        [TestCase(0, 5f)]
        [TestCase(1, -5f)]
        public void ShotPlanner_SolvesAOneTimeBallisticVelocityToTheTeamHoop(
            int teamId,
            float releaseZ)
        {
            var shooterObject = new GameObject("Shooter");
            shooterObject.transform.SetParent(root.transform);
            BasketballTeamMember shooter = shooterObject.AddComponent<BasketballTeamMember>();
            shooter.Configure(teamId, teamId, teamId + 1, null, null, null, null);
            Vector3 release = new Vector3(0.35f, 1.85f, releaseZ);

            bool created = BasketballShotPlanner.TryCreate(
                shooter,
                court,
                release,
                Vector3.zero,
                null,
                out BasketballShotPlan plan);

            Assert.That(created, Is.True);
            Assert.That(plan.Hoop, Is.SameAs(teamId == 0 ? positiveHoop : negativeHoop));
            Assert.That(plan.IsSupported, Is.True);
            Vector3 simulatedTarget = release +
                                      plan.DesiredVelocity * plan.FlightTime +
                                      0.5f * Physics.gravity *
                                      plan.FlightTime * plan.FlightTime;
            Assert.That(Vector3.Distance(simulatedTarget, plan.TargetPoint), Is.LessThan(0.002f));
        }

        private BasketballHoop CreateHoop(
            string name,
            int teamId,
            Vector3 center)
        {
            var hoopObject = new GameObject(name);
            hoopObject.transform.SetParent(root.transform);
            var rimCenter = new GameObject("Rim Center").transform;
            rimCenter.SetParent(hoopObject.transform);
            rimCenter.position = center;
            var backboard = new GameObject("Backboard").transform;
            backboard.SetParent(hoopObject.transform);
            backboard.position = center + Vector3.up * 0.3f;
            BasketballHoop hoop = hoopObject.AddComponent<BasketballHoop>();
            hoop.Configure(teamId, rimCenter, backboard, 0.225f);
            return hoop;
        }
    }
}
