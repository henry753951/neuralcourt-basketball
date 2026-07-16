using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballFormationLayoutTests
    {
        private static readonly Vector3[] ExpectedFiveOnFiveHome =
        {
            new(-5.2f, 0f, -3.8f),
            new(-2.6f, 0f, -1.5f),
            new(0f, 0f, -4.8f),
            new(2.6f, 0f, -1.5f),
            new(5.2f, 0f, -3.8f)
        };

        [Test]
        public void FiveOnFive_PreservesOriginalSceneBuilderPositions()
        {
            for (int slot = 0; slot < ExpectedFiveOnFiveHome.Length; slot++)
            {
                Assert.That(
                    BasketballFormationLayout.GetLocalOnCourtPosition(0, slot, 5),
                    Is.EqualTo(ExpectedFiveOnFiveHome[slot]));
            }
        }

        [Test]
        public void AwayFormation_MirrorsHomeAcrossCourtCenter()
        {
            for (int count = 1; count <= 5; count++)
            {
                for (int slot = 0; slot < count; slot++)
                {
                    Vector3 home = BasketballFormationLayout.GetLocalOnCourtPosition(
                        0,
                        slot,
                        count);
                    Vector3 away = BasketballFormationLayout.GetLocalOnCourtPosition(
                        1,
                        slot,
                        count);
                    Assert.That(away.x, Is.EqualTo(home.x));
                    Assert.That(away.y, Is.EqualTo(home.y));
                    Assert.That(away.z, Is.EqualTo(-home.z));
                }
            }
        }

        [Test]
        public void TeamsFaceEachOtherAtReset()
        {
            Assert.That(
                BasketballFormationLayout.GetLocalFacingDirection(0),
                Is.EqualTo(Vector3.forward));
            Assert.That(
                BasketballFormationLayout.GetLocalFacingDirection(1),
                Is.EqualTo(Vector3.back));
        }
    }
}
