using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [Flags]
    public enum BasketballActionMask
    {
        None = 0,
        Move = 1 << 0,
        Sprint = 1 << 1,
        BallControl = 1 << 2,
        Hold = 1 << 3,
        Shoot = 1 << 4,
        Pass = 1 << 5,
        CommitPassRelease = 1 << 6,
        Catch = 1 << 7,
        Steal = 1 << 8
    }

    [Serializable]
    public readonly struct BasketballPlayerObservation
    {
        public BasketballPlayerObservation(
            int rosterIndex,
            int playerIndex,
            int teamId,
            bool active,
            Vector3 position,
            Vector3 velocity,
            Vector3 forward,
            bool hasBall,
            bool intendedReceiver,
            BasketballActionMask actionMask)
        {
            RosterIndex = rosterIndex;
            PlayerIndex = playerIndex;
            TeamId = teamId;
            Active = active;
            Position = position;
            Velocity = velocity;
            Forward = forward;
            HasBall = hasBall;
            IsIntendedReceiver = intendedReceiver;
            ActionMask = actionMask;
        }

        public int RosterIndex { get; }
        public int PlayerIndex { get; }
        public int TeamId { get; }
        public bool Active { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public Vector3 Forward { get; }
        public bool HasBall { get; }
        public bool IsIntendedReceiver { get; }
        public BasketballActionMask ActionMask { get; }

        public bool Can(BasketballActionMask action)
            => (ActionMask & action) == action;
    }

    public static class BasketballActionMaskUtility
    {
        public static BasketballActionMask Build(
            bool active,
            bool hasBall,
            bool intendedReceiver,
            int teamId,
            int ownerTeamId,
            int activeTeamSize,
            BasketballPossessionState ballState)
        {
            if (!active)
            {
                return BasketballActionMask.None;
            }

            BasketballActionMask mask = BasketballActionMask.Move |
                                          BasketballActionMask.Sprint;
            if (hasBall)
            {
                mask |= BasketballActionMask.BallControl |
                        BasketballActionMask.Hold;
                if (ballState == BasketballPossessionState.Possessed)
                {
                    mask |= BasketballActionMask.Shoot;
                    if (activeTeamSize > 1)
                    {
                        mask |= BasketballActionMask.Pass;
                    }
                }
                else if (ballState == BasketballPossessionState.PassPreparing)
                {
                    mask |= BasketballActionMask.CommitPassRelease;
                }
                return mask;
            }

            if (intendedReceiver)
            {
                mask |= BasketballActionMask.Catch |
                        BasketballActionMask.Hold;
            }

            bool physicsBall = ballState == BasketballPossessionState.PassFlight ||
                               ballState == BasketballPossessionState.ShotFlight ||
                               ballState == BasketballPossessionState.Loose ||
                               ballState == BasketballPossessionState.Contested;
            if (physicsBall)
            {
                mask |= BasketballActionMask.Catch;
            }
            if (ownerTeamId >= 0 && ownerTeamId != teamId)
            {
                mask |= BasketballActionMask.Steal;
            }
            else if (ownerTeamId < 0 &&
                     (ballState == BasketballPossessionState.PassFlight ||
                      ballState == BasketballPossessionState.Loose ||
                      ballState == BasketballPossessionState.Contested))
            {
                mask |= BasketballActionMask.Steal;
            }
            return mask;
        }
    }

    /// <summary>
    /// Fixed-capacity, allocation-free snapshot of high-level match state. The
    /// public API exposes values only and does not expose the mutable backing array.
    /// </summary>
    public sealed class BasketballWorldObservation
    {
        private static readonly BasketballPlayerObservation InvalidPlayer = new(
            -1,
            -1,
            -1,
            false,
            Vector3.zero,
            Vector3.zero,
            Vector3.forward,
            false,
            false,
            BasketballActionMask.None);
        private readonly BasketballPlayerObservation[] players;

        public BasketballWorldObservation(int playerCapacity)
        {
            players = new BasketballPlayerObservation[Mathf.Max(0, playerCapacity)];
            for (int index = 0; index < players.Length; index++)
            {
                players[index] = new BasketballPlayerObservation(
                    index,
                    -1,
                    -1,
                    false,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.forward,
                    false,
                    false,
                    BasketballActionMask.None);
            }
        }

        public int PlayerCapacity => players.Length;
        public int ActivePlayerCount { get; private set; }
        public int ActiveTeamZeroCount { get; private set; }
        public int ActiveTeamOneCount { get; private set; }
        public float TimeSeconds { get; private set; }
        public BasketballMatchMode MatchMode { get; private set; }
        public BasketballPossessionState BallState { get; private set; }
        public BasketballBallControlMode BallControlMode { get; private set; }
        public int PossessionVersion { get; private set; }
        public int OwnerPlayerIndex { get; private set; } = -1;
        public int PreviousOwnerPlayerIndex { get; private set; } = -1;
        public int IntendedReceiverPlayerIndex { get; private set; } = -1;
        public Vector3 BallPosition { get; private set; }
        public Vector3 BallVelocity { get; private set; }
        public Vector3 TeamZeroAttackTarget { get; private set; }
        public Vector3 TeamOneAttackTarget { get; private set; }
        public int TeamZeroScore { get; private set; }
        public int TeamOneScore { get; private set; }

        public BasketballPlayerObservation GetPlayer(int rosterIndex)
            => rosterIndex >= 0 && rosterIndex < players.Length
                ? players[rosterIndex]
                : InvalidPlayer;

        public bool TryGetPlayerById(
            int playerIndex,
            out BasketballPlayerObservation observation)
        {
            if (playerIndex < 0)
            {
                observation = default;
                return false;
            }
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index].PlayerIndex == playerIndex)
                {
                    observation = players[index];
                    return true;
                }
            }
            observation = default;
            return false;
        }

        internal void Capture(
            BasketballTeamMember[] roster,
            BasketballBallController ball,
            BasketballPossessionManager possession,
            BasketballCourt court,
            BasketballScoreTracker score,
            BasketballMatchMode matchMode)
        {
            TimeSeconds = Time.time;
            MatchMode = matchMode;
            BallState = possession != null
                ? possession.BallState
                : BasketballPossessionState.Loose;
            BallControlMode = possession != null
                ? possession.BallControlMode
                : BasketballBallControlMode.PhysicsFlight;
            PossessionVersion = possession != null ? possession.PossessionVersion : 0;
            OwnerPlayerIndex = possession?.Owner != null
                ? possession.Owner.PlayerIndex
                : -1;
            PreviousOwnerPlayerIndex = possession?.PreviousOwner != null
                ? possession.PreviousOwner.PlayerIndex
                : -1;
            IntendedReceiverPlayerIndex = possession?.IntendedReceiver != null
                ? possession.IntendedReceiver.PlayerIndex
                : -1;
            BallPosition = ball != null ? ball.transform.position : Vector3.zero;
            BallVelocity = ball != null ? ball.Velocity : Vector3.zero;
            TeamZeroAttackTarget = court?.GetAttackHoop(0) != null
                ? court.GetAttackHoop(0).AimPoint
                : Vector3.zero;
            TeamOneAttackTarget = court?.GetAttackHoop(1) != null
                ? court.GetAttackHoop(1).AimPoint
                : Vector3.zero;
            TeamZeroScore = score != null ? score.TeamZeroScore : 0;
            TeamOneScore = score != null ? score.TeamOneScore : 0;
            ActivePlayerCount = 0;
            ActiveTeamZeroCount = 0;
            ActiveTeamOneCount = 0;

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = roster != null && index < roster.Length
                    ? roster[index]
                    : null;
                if (member == null || !member.IsOnCourt ||
                    member.Controller?.State == null)
                {
                    continue;
                }
                ActivePlayerCount++;
                if (member.TeamId == 0)
                {
                    ActiveTeamZeroCount++;
                }
                else if (member.TeamId == 1)
                {
                    ActiveTeamOneCount++;
                }
            }

            int ownerTeamId = possession?.Owner != null
                ? possession.Owner.TeamId
                : -1;

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = roster != null && index < roster.Length
                    ? roster[index]
                    : null;
                BasketballAgentState state = member?.Controller?.State;
                bool active = member != null && member.IsOnCourt && state != null;
                Vector3 position = state != null
                    ? state.ActorRootPosition
                    : member != null ? member.transform.position : Vector3.zero;
                Vector3 velocity = state != null
                    ? state.RootVelocities[BasketballAgentState.Pivot]
                    : Vector3.zero;
                Vector3 forward = state != null
                    ? state.ActorRootRotation * Vector3.forward
                    : member != null ? member.transform.forward : Vector3.forward;
                bool hasBall = active && possession?.Owner == member;
                bool intendedReceiver = active &&
                                        possession?.IntendedReceiver == member;
                int activeTeamSize = member != null && member.TeamId == 0
                    ? ActiveTeamZeroCount
                    : member != null && member.TeamId == 1
                        ? ActiveTeamOneCount
                        : 0;
                BasketballActionMask actionMask = BasketballActionMaskUtility.Build(
                    active,
                    hasBall,
                    intendedReceiver,
                    member != null ? member.TeamId : -1,
                    ownerTeamId,
                    activeTeamSize,
                    BallState);
                players[index] = new BasketballPlayerObservation(
                    index,
                    member != null ? member.PlayerIndex : -1,
                    member != null ? member.TeamId : -1,
                    active,
                    position,
                    velocity,
                    forward,
                    hasBall,
                    intendedReceiver,
                    actionMask);
            }
        }
    }
}
