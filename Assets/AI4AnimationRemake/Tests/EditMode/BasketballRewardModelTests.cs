using NUnit.Framework;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball.Tests
{
    public sealed class BasketballRewardModelTests
    {
        private static readonly BasketballRewardWeights Weights = new(
            scorePerPointTeamReward: 1f,
            scorePerPointOpponentPenalty: -1f,
            scorePerPointActorReward: 0.1f,
            completedPassTeamReward: 0.03f,
            completedPassReceiverReward: 0.01f,
            completedPassPasserReward: 0.02f,
            failedPassTeamPenalty: -0.03f,
            failedPassActorPenalty: -0.01f,
            missedShotActorPenalty: -0.01f,
            interceptionTeamReward: 0.2f,
            interceptionOpponentPenalty: -0.2f,
            interceptionActorReward: 0.05f,
            stealTouchActorReward: 0.01f,
            stealSecureTeamReward: 0.2f,
            stealSecureOpponentPenalty: -0.2f,
            stealSecureActorReward: 0.05f,
            looseBallPickupTeamReward: 0.03f,
            looseBallPickupActorReward: 0.01f,
            rejectedCommandActorPenalty: -0.01f);

        [Test]
        public void ShotMade_UsesPointsAndCorrectTeamDirection()
        {
            BasketballWorldEvent source = Event(
                BasketballWorldEventType.ShotMade,
                teamId: 1,
                actor: 7,
                points: 3);

            bool evaluated = BasketballRewardModel.TryEvaluate(
                source,
                Weights,
                rewardSequence: 4,
                episodeId: 2,
                out BasketballRewardSignal signal);

            Assert.That(evaluated, Is.True);
            Assert.That(signal.TeamZeroDelta, Is.EqualTo(-3f));
            Assert.That(signal.TeamOneDelta, Is.EqualTo(3f));
            Assert.That(signal.ActorDelta, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(signal.EndsPossession, Is.True);
            Assert.That(signal.EpisodeId, Is.EqualTo(2));
        }

        [Test]
        public void PassCaught_RewardsReceiverAndPasserWithoutOpponentPenalty()
        {
            BasketballWorldEvent source = Event(
                BasketballWorldEventType.PassCaught,
                teamId: 0,
                actor: 2,
                target: 0);

            bool evaluated = BasketballRewardModel.TryEvaluate(
                source,
                Weights,
                rewardSequence: 1,
                episodeId: 1,
                out BasketballRewardSignal signal);

            Assert.That(evaluated, Is.True);
            Assert.That(signal.TeamZeroDelta, Is.EqualTo(0.03f));
            Assert.That(signal.TeamOneDelta, Is.Zero);
            Assert.That(signal.ActorPlayerIndex, Is.EqualTo(2));
            Assert.That(signal.ActorDelta, Is.EqualTo(0.01f));
            Assert.That(signal.TargetPlayerIndex, Is.EqualTo(0));
            Assert.That(signal.TargetDelta, Is.EqualTo(0.02f));
        }

        [Test]
        public void PassIntercepted_IsZeroSumAtTeamLevel()
        {
            BasketballWorldEvent source = Event(
                BasketballWorldEventType.PassIntercepted,
                teamId: 1,
                actor: 8,
                target: 3);

            bool evaluated = BasketballRewardModel.TryEvaluate(
                source,
                Weights,
                rewardSequence: 3,
                episodeId: 1,
                out BasketballRewardSignal signal);

            Assert.That(evaluated, Is.True);
            Assert.That(signal.TeamZeroDelta, Is.EqualTo(-0.2f));
            Assert.That(signal.TeamOneDelta, Is.EqualTo(0.2f));
            Assert.That(signal.ActorDelta, Is.EqualTo(0.05f));
        }

        [Test]
        public void NonOutcomeEvent_DoesNotCreateRewardSignal()
        {
            BasketballWorldEvent source = Event(
                BasketballWorldEventType.MatchRestarted,
                teamId: 0,
                actor: 0);

            bool evaluated = BasketballRewardModel.TryEvaluate(
                source,
                Weights,
                rewardSequence: 1,
                episodeId: 1,
                out _);

            Assert.That(evaluated, Is.False);
        }

        private static BasketballWorldEvent Event(
            BasketballWorldEventType type,
            int teamId,
            int actor,
            int target = -1,
            int points = 0)
        {
            return new BasketballWorldEvent(
                sequence: 12,
                timeSeconds: 1.25f,
                type: type,
                possessionVersion: 4,
                actorPlayerIndex: actor,
                targetPlayerIndex: target,
                teamId: teamId,
                position: Vector3.zero,
                velocity: Vector3.zero,
                value: 0f,
                points: points,
                targetPosition: Vector3.zero,
                skillVariant: 0);
        }
    }
}
