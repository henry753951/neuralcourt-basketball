using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public readonly struct BasketballRewardWeights
    {
        public BasketballRewardWeights(
            float scorePerPointTeamReward,
            float scorePerPointOpponentPenalty,
            float scorePerPointActorReward,
            float completedPassTeamReward,
            float completedPassReceiverReward,
            float completedPassPasserReward,
            float failedPassTeamPenalty,
            float failedPassActorPenalty,
            float missedShotActorPenalty,
            float interceptionTeamReward,
            float interceptionOpponentPenalty,
            float interceptionActorReward,
            float stealTouchActorReward,
            float stealSecureTeamReward,
            float stealSecureOpponentPenalty,
            float stealSecureActorReward,
            float looseBallPickupTeamReward,
            float looseBallPickupActorReward,
            float rejectedCommandActorPenalty)
        {
            ScorePerPointTeamReward = scorePerPointTeamReward;
            ScorePerPointOpponentPenalty = scorePerPointOpponentPenalty;
            ScorePerPointActorReward = scorePerPointActorReward;
            CompletedPassTeamReward = completedPassTeamReward;
            CompletedPassReceiverReward = completedPassReceiverReward;
            CompletedPassPasserReward = completedPassPasserReward;
            FailedPassTeamPenalty = failedPassTeamPenalty;
            FailedPassActorPenalty = failedPassActorPenalty;
            MissedShotActorPenalty = missedShotActorPenalty;
            InterceptionTeamReward = interceptionTeamReward;
            InterceptionOpponentPenalty = interceptionOpponentPenalty;
            InterceptionActorReward = interceptionActorReward;
            StealTouchActorReward = stealTouchActorReward;
            StealSecureTeamReward = stealSecureTeamReward;
            StealSecureOpponentPenalty = stealSecureOpponentPenalty;
            StealSecureActorReward = stealSecureActorReward;
            LooseBallPickupTeamReward = looseBallPickupTeamReward;
            LooseBallPickupActorReward = looseBallPickupActorReward;
            RejectedCommandActorPenalty = rejectedCommandActorPenalty;
        }

        public float ScorePerPointTeamReward { get; }
        public float ScorePerPointOpponentPenalty { get; }
        public float ScorePerPointActorReward { get; }
        public float CompletedPassTeamReward { get; }
        public float CompletedPassReceiverReward { get; }
        public float CompletedPassPasserReward { get; }
        public float FailedPassTeamPenalty { get; }
        public float FailedPassActorPenalty { get; }
        public float MissedShotActorPenalty { get; }
        public float InterceptionTeamReward { get; }
        public float InterceptionOpponentPenalty { get; }
        public float InterceptionActorReward { get; }
        public float StealTouchActorReward { get; }
        public float StealSecureTeamReward { get; }
        public float StealSecureOpponentPenalty { get; }
        public float StealSecureActorReward { get; }
        public float LooseBallPickupTeamReward { get; }
        public float LooseBallPickupActorReward { get; }
        public float RejectedCommandActorPenalty { get; }
    }

    [Serializable]
    public readonly struct BasketballRewardSignal
    {
        public BasketballRewardSignal(
            int sequence,
            int episodeId,
            int worldEventSequence,
            BasketballWorldEventType sourceType,
            int actorPlayerIndex,
            int targetPlayerIndex,
            int teamId,
            float teamZeroDelta,
            float teamOneDelta,
            float actorDelta,
            float targetDelta,
            bool endsPossession)
        {
            Sequence = sequence;
            EpisodeId = episodeId;
            WorldEventSequence = worldEventSequence;
            SourceType = sourceType;
            ActorPlayerIndex = actorPlayerIndex;
            TargetPlayerIndex = targetPlayerIndex;
            TeamId = teamId;
            TeamZeroDelta = teamZeroDelta;
            TeamOneDelta = teamOneDelta;
            ActorDelta = actorDelta;
            TargetDelta = targetDelta;
            EndsPossession = endsPossession;
        }

        public int Sequence { get; }
        public int EpisodeId { get; }
        public int WorldEventSequence { get; }
        public BasketballWorldEventType SourceType { get; }
        public int ActorPlayerIndex { get; }
        public int TargetPlayerIndex { get; }
        public int TeamId { get; }
        public float TeamZeroDelta { get; }
        public float TeamOneDelta { get; }
        public float ActorDelta { get; }
        public float TargetDelta { get; }
        public bool EndsPossession { get; }
    }

    public static class BasketballRewardModel
    {
        public static bool TryEvaluate(
            in BasketballWorldEvent worldEvent,
            in BasketballRewardWeights weights,
            int rewardSequence,
            int episodeId,
            out BasketballRewardSignal signal)
        {
            float teamReward = 0f;
            float opponentReward = 0f;
            float actorReward = 0f;
            float targetReward = 0f;
            bool endsPossession = false;

            switch (worldEvent.Type)
            {
                case BasketballWorldEventType.ShotMade:
                {
                    int points = Mathf.Max(1, worldEvent.Points);
                    teamReward = points * weights.ScorePerPointTeamReward;
                    opponentReward = points * weights.ScorePerPointOpponentPenalty;
                    actorReward = points * weights.ScorePerPointActorReward;
                    endsPossession = true;
                    break;
                }
                case BasketballWorldEventType.ShotMissed:
                    actorReward = weights.MissedShotActorPenalty;
                    endsPossession = true;
                    break;
                case BasketballWorldEventType.PassCaught:
                    teamReward = weights.CompletedPassTeamReward;
                    actorReward = weights.CompletedPassReceiverReward;
                    targetReward = weights.CompletedPassPasserReward;
                    endsPossession = true;
                    break;
                case BasketballWorldEventType.PassFailed:
                case BasketballWorldEventType.PassPreparationFailed:
                    teamReward = weights.FailedPassTeamPenalty;
                    actorReward = weights.FailedPassActorPenalty;
                    endsPossession = worldEvent.Type == BasketballWorldEventType.PassFailed;
                    break;
                case BasketballWorldEventType.PassIntercepted:
                    teamReward = weights.InterceptionTeamReward;
                    opponentReward = weights.InterceptionOpponentPenalty;
                    actorReward = weights.InterceptionActorReward;
                    endsPossession = true;
                    break;
                case BasketballWorldEventType.StealTouch:
                    actorReward = weights.StealTouchActorReward;
                    break;
                case BasketballWorldEventType.BallSecured:
                    if (worldEvent.TargetPlayerIndex >= 0)
                    {
                        teamReward = weights.StealSecureTeamReward;
                        opponentReward = weights.StealSecureOpponentPenalty;
                        actorReward = weights.StealSecureActorReward;
                    }
                    else
                    {
                        teamReward = weights.LooseBallPickupTeamReward;
                        actorReward = weights.LooseBallPickupActorReward;
                    }
                    endsPossession = true;
                    break;
                case BasketballWorldEventType.CommandRejected:
                    actorReward = weights.RejectedCommandActorPenalty;
                    break;
            }

            float teamZeroDelta = 0f;
            float teamOneDelta = 0f;
            if (worldEvent.TeamId == 0)
            {
                teamZeroDelta = teamReward;
                teamOneDelta = opponentReward;
            }
            else if (worldEvent.TeamId == 1)
            {
                teamOneDelta = teamReward;
                teamZeroDelta = opponentReward;
            }

            if (Mathf.Approximately(teamZeroDelta, 0f) &&
                Mathf.Approximately(teamOneDelta, 0f) &&
                Mathf.Approximately(actorReward, 0f) &&
                Mathf.Approximately(targetReward, 0f))
            {
                signal = default;
                return false;
            }

            signal = new BasketballRewardSignal(
                rewardSequence,
                episodeId,
                worldEvent.Sequence,
                worldEvent.Type,
                worldEvent.ActorPlayerIndex,
                worldEvent.TargetPlayerIndex,
                worldEvent.TeamId,
                teamZeroDelta,
                teamOneDelta,
                actorReward,
                targetReward,
                endsPossession);
            return true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BasketballRewardTracker : MonoBehaviour
    {
        [SerializeField] private BasketballWorldEventStream eventStream;
        [SerializeField, Range(32, 2048)] private int capacity = 256;
        [SerializeField] private float scorePerPointTeamReward = 1f;
        [SerializeField] private float scorePerPointOpponentPenalty = -1f;
        [SerializeField] private float scorePerPointActorReward = 0.1f;
        [SerializeField] private float completedPassTeamReward = 0.03f;
        [SerializeField] private float completedPassReceiverReward = 0.01f;
        [SerializeField] private float completedPassPasserReward = 0.01f;
        [SerializeField] private float failedPassTeamPenalty = -0.03f;
        [SerializeField] private float failedPassActorPenalty = -0.01f;
        [SerializeField] private float missedShotActorPenalty = -0.01f;
        [SerializeField] private float interceptionTeamReward = 0.2f;
        [SerializeField] private float interceptionOpponentPenalty = -0.2f;
        [SerializeField] private float interceptionActorReward = 0.05f;
        [SerializeField] private float stealTouchActorReward = 0.01f;
        [SerializeField] private float stealSecureTeamReward = 0.2f;
        [SerializeField] private float stealSecureOpponentPenalty = -0.2f;
        [SerializeField] private float stealSecureActorReward = 0.05f;
        [SerializeField] private float looseBallPickupTeamReward = 0.03f;
        [SerializeField] private float looseBallPickupActorReward = 0.01f;
        [SerializeField] private float rejectedCommandActorPenalty = -0.01f;

        private BasketballRewardSignal[] signals;
        private BasketballTeamMember[] roster;
        private float[] playerReturns;
        private int nextWriteIndex;
        private int count;
        private int sequence;

        public int EpisodeId { get; private set; }
        public int Count => count;
        public int Capacity => signals != null ? signals.Length : Mathf.Max(32, capacity);
        public int LatestSequence => sequence;
        public int OldestSequence => count > 0 ? sequence - count + 1 : sequence + 1;
        public float TeamZeroReturn { get; private set; }
        public float TeamOneReturn { get; private set; }
        public event Action<BasketballRewardSignal> Published;

        private BasketballRewardWeights Weights => new(
            scorePerPointTeamReward,
            scorePerPointOpponentPenalty,
            scorePerPointActorReward,
            completedPassTeamReward,
            completedPassReceiverReward,
            completedPassPasserReward,
            failedPassTeamPenalty,
            failedPassActorPenalty,
            missedShotActorPenalty,
            interceptionTeamReward,
            interceptionOpponentPenalty,
            interceptionActorReward,
            stealTouchActorReward,
            stealSecureTeamReward,
            stealSecureOpponentPenalty,
            stealSecureActorReward,
            looseBallPickupTeamReward,
            looseBallPickupActorReward,
            rejectedCommandActorPenalty);

        private void Awake()
        {
            EnsureBuffers();
            BasketballWorldEventStream resolved = eventStream != null
                ? eventStream
                : GetComponent<BasketballWorldEventStream>();
            eventStream = null;
            SetEventStream(resolved);
        }

        private void OnDestroy()
        {
            SetEventStream(null);
        }

        public void ConfigureRuntime(
            BasketballWorldEventStream stream,
            BasketballTeamMember[] players)
        {
            roster = players;
            EnsureBuffers();
            SetEventStream(stream);
        }

        public void SetEventStream(BasketballWorldEventStream value)
        {
            if (eventStream == value)
            {
                return;
            }
            if (eventStream != null)
            {
                eventStream.Published -= HandleWorldEvent;
            }
            eventStream = value;
            if (eventStream != null)
            {
                eventStream.Published += HandleWorldEvent;
            }
        }

        public void ResetEpisode()
        {
            EnsureBuffers();
            EpisodeId++;
            TeamZeroReturn = 0f;
            TeamOneReturn = 0f;
            Array.Clear(playerReturns, 0, playerReturns.Length);
            Array.Clear(signals, 0, signals.Length);
            nextWriteIndex = 0;
            count = 0;
        }

        public float GetPlayerReturn(int playerIndex)
        {
            int rosterIndex = FindRosterIndex(playerIndex);
            return rosterIndex >= 0 && rosterIndex < playerReturns.Length
                ? playerReturns[rosterIndex]
                : 0f;
        }

        public BasketballRewardSignal GetNewest(int newestOffset = 0)
        {
            if (signals == null || newestOffset < 0 || newestOffset >= count)
            {
                return default;
            }
            int index = nextWriteIndex - 1 - newestOffset;
            while (index < 0)
            {
                index += signals.Length;
            }
            return signals[index];
        }

        public bool TryGetBySequence(
            int requestedSequence,
            out BasketballRewardSignal signal)
        {
            if (signals == null || count == 0 ||
                requestedSequence < OldestSequence ||
                requestedSequence > LatestSequence)
            {
                signal = default;
                return false;
            }
            signal = GetNewest(LatestSequence - requestedSequence);
            return signal.Sequence == requestedSequence;
        }

        private void HandleWorldEvent(BasketballWorldEvent worldEvent)
        {
            BasketballRewardWeights weights = Weights;
            if (!BasketballRewardModel.TryEvaluate(
                    worldEvent,
                    weights,
                    sequence + 1,
                    EpisodeId,
                    out BasketballRewardSignal signal))
            {
                return;
            }

            sequence++;
            TeamZeroReturn += signal.TeamZeroDelta;
            TeamOneReturn += signal.TeamOneDelta;
            AddPlayerReturn(signal.ActorPlayerIndex, signal.ActorDelta);
            AddPlayerReturn(signal.TargetPlayerIndex, signal.TargetDelta);
            signals[nextWriteIndex] = signal;
            nextWriteIndex = (nextWriteIndex + 1) % signals.Length;
            count = Mathf.Min(count + 1, signals.Length);
            Published?.Invoke(signal);
        }

        private void AddPlayerReturn(int playerIndex, float value)
        {
            if (Mathf.Approximately(value, 0f))
            {
                return;
            }
            int rosterIndex = FindRosterIndex(playerIndex);
            if (rosterIndex >= 0 && rosterIndex < playerReturns.Length)
            {
                playerReturns[rosterIndex] += value;
            }
        }

        private int FindRosterIndex(int playerIndex)
        {
            if (playerIndex < 0 || roster == null)
            {
                return -1;
            }
            for (int index = 0; index < roster.Length; index++)
            {
                if (roster[index] != null && roster[index].PlayerIndex == playerIndex)
                {
                    return index;
                }
            }
            return -1;
        }

        private void EnsureBuffers()
        {
            int signalCapacity = Mathf.Max(32, capacity);
            if (signals == null || signals.Length != signalCapacity)
            {
                signals = new BasketballRewardSignal[signalCapacity];
                nextWriteIndex = 0;
                count = 0;
            }
            int playerCapacity = roster != null ? roster.Length : 0;
            if (playerReturns == null || playerReturns.Length != playerCapacity)
            {
                playerReturns = new float[playerCapacity];
            }
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballWorldEventStream stream,
            BasketballTeamMember[] players,
            int rewardCapacity = 256)
        {
            eventStream = stream;
            roster = players;
            capacity = Mathf.Clamp(rewardCapacity, 32, 2048);
        }
#endif
    }
}
