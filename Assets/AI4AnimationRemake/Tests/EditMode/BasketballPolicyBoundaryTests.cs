using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballPolicyBoundaryTests
    {
        [Test]
        public void PlayerCommand_PreservesPassTransactionData()
        {
            BasketballPlayerCommand command = BasketballPlayerCommand.RequestPass(
                commandId: 9,
                actorPlayerIndex: 2,
                targetPlayerIndex: 4,
                possessionVersion: 17,
                passType: BasketballPassType.Chest,
                expiresAt: 4.5f);

            Assert.That(command.CommandId, Is.EqualTo(9));
            Assert.That(command.Skill, Is.EqualTo(BasketballSkillCommandType.RequestPass));
            Assert.That(command.ActorPlayerIndex, Is.EqualTo(2));
            Assert.That(command.TargetPlayerIndex, Is.EqualTo(4));
            Assert.That(command.PossessionVersion, Is.EqualTo(17));
            Assert.That(command.PassType, Is.EqualTo(BasketballPassType.Chest));
            Assert.That(command.ExpiresAt, Is.EqualTo(4.5f));
            Assert.That(command.HasSkillRequest, Is.True);
        }

        [Test]
        public void WorldObservation_UsesFixedCapacityAndSafeInvalidReads()
        {
            var observation = new BasketballWorldObservation(10);

            Assert.That(observation.PlayerCapacity, Is.EqualTo(10));
            Assert.That(observation.GetPlayer(-1).PlayerIndex, Is.EqualTo(-1));
            Assert.That(observation.GetPlayer(10).PlayerIndex, Is.EqualTo(-1));
            Assert.That(observation.GetPlayer(0).PlayerIndex, Is.EqualTo(-1));
            Assert.That(
                observation.GetPlayer(0).ActionMask,
                Is.EqualTo(BasketballActionMask.None));
        }

        [Test]
        public void ActionMask_RespectsRosterSizeAndPossessionRole()
        {
            BasketballActionMask oneOnOneOwner = BasketballActionMaskUtility.Build(
                active: true,
                hasBall: true,
                intendedReceiver: false,
                teamId: 0,
                ownerTeamId: 0,
                activeTeamSize: 1,
                ballState: BasketballPossessionState.Possessed);
            Assert.That(
                (oneOnOneOwner & BasketballActionMask.Shoot) != 0,
                Is.True);
            Assert.That(
                (oneOnOneOwner & BasketballActionMask.Pass) != 0,
                Is.False);

            BasketballActionMask threeOnThreeOwner =
                BasketballActionMaskUtility.Build(
                    active: true,
                    hasBall: true,
                    intendedReceiver: false,
                    teamId: 0,
                    ownerTeamId: 0,
                    activeTeamSize: 3,
                    ballState: BasketballPossessionState.Possessed);
            Assert.That(
                (threeOnThreeOwner & BasketballActionMask.Pass) != 0,
                Is.True);

            BasketballActionMask defender = BasketballActionMaskUtility.Build(
                active: true,
                hasBall: false,
                intendedReceiver: false,
                teamId: 1,
                ownerTeamId: 0,
                activeTeamSize: 3,
                ballState: BasketballPossessionState.Possessed);
            Assert.That(
                (defender & BasketballActionMask.Steal) != 0,
                Is.True);
        }

        [Test]
        public void WorldEventStream_OverwritesOldestWithoutGrowing()
        {
            var root = new GameObject("World Event Stream Test");
            try
            {
                BasketballWorldEventStream stream =
                    root.AddComponent<BasketballWorldEventStream>();
                int capacity = stream.Capacity;
                for (int index = 0; index < capacity + 5; index++)
                {
                    stream.Publish(
                        BasketballWorldEventType.BallLoose,
                        possessionVersion: index,
                        value: index);
                }

                Assert.That(stream.Count, Is.EqualTo(capacity));
                Assert.That(stream.LatestSequence, Is.EqualTo(capacity + 5));
                Assert.That(stream.OldestSequence, Is.EqualTo(6));
                Assert.That(stream.GetNewest().Value, Is.EqualTo(capacity + 4));
                Assert.That(
                    stream.GetNewest(capacity - 1).Value,
                    Is.EqualTo(5));

                Assert.That(stream.TryGetBySequence(5, out _), Is.False);
                Assert.That(
                    stream.TryGetBySequence(6, out BasketballWorldEvent oldest),
                    Is.True);
                Assert.That(oldest.Sequence, Is.EqualTo(6));
                Assert.That(oldest.Value, Is.EqualTo(5));
                Assert.That(
                    stream.TryGetBySequence(
                        capacity + 5,
                        out BasketballWorldEvent newest),
                    Is.True);
                Assert.That(newest.Value, Is.EqualTo(capacity + 4));
                Assert.That(
                    stream.TryGetBySequence(capacity + 6, out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldEventStream_PreservesSkillTargetAndVariant()
        {
            var root = new GameObject("World Event Skill Context Test");
            try
            {
                BasketballWorldEventStream stream =
                    root.AddComponent<BasketballWorldEventStream>();
                Vector3 target = new(2.5f, 1.4f, -3f);
                BasketballWorldEvent published = stream.Publish(
                    BasketballWorldEventType.PassRequested,
                    possessionVersion: 12,
                    position: Vector3.one,
                    velocity: Vector3.forward * 6f,
                    value: 0.8f,
                    targetPosition: target,
                    skillVariant: (int)BasketballPassType.Lob);

                Assert.That(published.TargetPosition, Is.EqualTo(target));
                Assert.That(
                    published.SkillVariant,
                    Is.EqualTo((int)BasketballPassType.Lob));
                Assert.That(
                    stream.TryGetBySequence(
                        published.Sequence,
                        out BasketballWorldEvent retained),
                    Is.True);
                Assert.That(retained.TargetPosition, Is.EqualTo(target));
                Assert.That(retained.SkillVariant, Is.EqualTo(published.SkillVariant));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
