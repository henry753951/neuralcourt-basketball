using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballViolationType
    {
        None,
        OutOfBounds,
        ShotClock,
        BackcourtEightSeconds,
        BackcourtReturn,
        OffensiveThreeSeconds,
        Traveling,
        DoubleDribble
    }

    /// <summary>
    /// Authoritative, model-independent match rules. The neural model remains
    /// responsible for motion, while every turnover is resolved through the
    /// central possession manager so a violation cannot leave stale pass,
    /// catch, shot, or steal state behind.
    /// </summary>
    [DefaultExecutionOrder(-95)]
    [DisallowMultipleComponent]
    public sealed class BasketballRulesManager : MonoBehaviour
    {
        [Header("Rules / 籃球規則")]
        [SerializeField] private bool enableRules = true;
        [SerializeField] private bool enforceOutOfBounds = true;
        [SerializeField] private bool enforceShotClock = true;
        [SerializeField] private bool enforceBackcourt = true;
        [SerializeField] private bool enforceOffensiveThreeSeconds = true;
        [SerializeField] private bool enforceTraveling = true;
        [SerializeField] private bool enforceDoubleDribble = true;

        [Header("Timing / 時間限制")]
        [SerializeField, Min(1f)] private float shotClockSeconds = 24f;
        [SerializeField, Min(1f)] private float offensiveReboundShotClockSeconds = 14f;
        [SerializeField, Min(1f)] private float backcourtSeconds = 8f;
        [SerializeField, Min(1f)] private float offensiveLaneSeconds = 3f;
        [SerializeField, Min(0.1f)] private float violationRestartDelay = 0.85f;

        [Header("Court Geometry / 場地判定")]
        [SerializeField, Min(0f)] private float outOfBoundsTolerance = 0.08f;
        [SerializeField, Min(0f)] private float midcourtTolerance = 0.3f;
        [SerializeField, Min(0.5f)] private float paintWidth = 4.9f;
        [SerializeField, Min(0.5f)] private float paintDepth = 5.8f;
        [SerializeField, Range(0f, 1f)] private float paintExitWarningSeconds = 0.65f;
        [SerializeField, Min(0.1f)] private float inboundPlayerInset = 0.9f;

        [Header("Conservative Ball Handling / 保守持球違例")]
        [Tooltip("Only explicit Hold intent starts the traveling detector, avoiding false positives from the pretrained dribble motion.")]
        [SerializeField, Min(0.1f)] private float heldBallConfirmationSeconds = 0.35f;
        [SerializeField, Min(0.25f)] private float maximumHeldTravelDistance = 1.35f;
        [SerializeField, Range(0f, 1f)] private float dribbleGroundContactThreshold = 0.45f;

        private BasketballTeamMember[] players;
        private BasketballBallController ball;
        private BasketballPossessionManager possession;
        private BasketballCourt court;
        private BasketballWorldEventStream worldEvents;
        private float[] laneTimers;
        private int offenseTeamId = -1;
        private int lastTouchTeamId = -1;
        private int shotTeamId = -1;
        private int lastOwnerPlayerIndex = -1;
        private bool shotInProgress;
        private bool frontcourtEstablished;
        private float shotClockRemaining;
        private float backcourtRemaining;
        private float heldStartedAt = float.NegativeInfinity;
        private Vector3 heldStartPosition;
        private bool dribbleStopped;
        private bool previousGroundContact;
        private int lastProcessedEventSequence;
        private bool violationRestartPending;
        private float violationRestartAt = float.NegativeInfinity;
        private int pendingInboundTeamId = -1;
        private BasketballTeamMember pendingInbounder;
        private BasketballTeamMember pendingOffender;
        private Vector3 pendingViolationPosition;
        private Quaternion pendingBallRotation;

        public bool EnableRules => enableRules;
        public float ShotClockRemaining => Mathf.Max(0f, shotClockRemaining);
        public float BackcourtRemaining => Mathf.Max(0f, backcourtRemaining);
        public int OffenseTeamId => offenseTeamId;
        public bool FrontcourtEstablished => frontcourtEstablished;
        public BasketballViolationType LastViolation { get; private set; }
        public int LastViolationPlayerIndex { get; private set; } = -1;
        public float LastViolationTime { get; private set; } = float.NegativeInfinity;
        public bool IsRestartPending => violationRestartPending;
        public float RestartRemaining => violationRestartPending
            ? Mathf.Max(0f, violationRestartAt - Time.time)
            : 0f;

        public void ConfigureRuntime(
            BasketballTeamMember[] roster,
            BasketballBallController sharedBall,
            BasketballPossessionManager possessionManager,
            BasketballCourt basketballCourt,
            BasketballWorldEventStream eventStream)
        {
            players = roster;
            ball = sharedBall;
            possession = possessionManager;
            court = basketballCourt;
            worldEvents = eventStream;
            EnsureBuffers();
            ResetRuleState();
        }

        public bool EvaluateRules(bool gameplaySuspended)
        {
            if (violationRestartPending)
            {
                if (Time.time >= violationRestartAt)
                {
                    CompletePendingViolationRestart();
                }
                return true;
            }
            if (!enableRules || gameplaySuspended || players == null || ball == null ||
                possession == null || court == null)
            {
                return false;
            }

            EnsureBuffers();
            ConsumeWorldEvents();
            SynchronizeTeamControl();

            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            if (enforceOutOfBounds && IsBallOutOfBounds())
            {
                int offendingTeam = lastTouchTeamId >= 0
                    ? lastTouchTeamId
                    : offenseTeamId;
                return CallViolation(
                    BasketballViolationType.OutOfBounds,
                    possession.Owner,
                    offendingTeam,
                    BasketballWorldEventType.BallOutOfBounds);
            }

            if (offenseTeamId < 0)
            {
                ResetTravelState();
                return false;
            }

            bool shotFlight = possession.BallState == BasketballPossessionState.ShotFlight;
            if (enforceShotClock && !shotFlight)
            {
                shotClockRemaining -= deltaTime;
                if (shotClockRemaining <= 0f)
                {
                    return CallViolation(
                        BasketballViolationType.ShotClock,
                        possession.Owner,
                        offenseTeamId,
                        BasketballWorldEventType.ShotClockViolation);
                }
            }

            BasketballTeamMember owner = possession.Owner;
            bool liveTeamControl = possession.BallState != BasketballPossessionState.Loose &&
                                   possession.BallState != BasketballPossessionState.Contested &&
                                   possession.BallState != BasketballPossessionState.ShotFlight;
            if (enforceBackcourt && liveTeamControl && EvaluateBackcourt(owner, deltaTime))
            {
                return true;
            }
            if (owner != null && owner.TeamId == offenseTeamId &&
                possession.BallState != BasketballPossessionState.PassPreparing)
            {
                if (EvaluateConservativeBallHandling(owner))
                {
                    return true;
                }
            }
            else
            {
                ResetTravelState();
            }

            return enforceOffensiveThreeSeconds && EvaluateOffensiveLane(deltaTime);
        }

        public bool IsShotClockUrgent(float thresholdSeconds = 4f)
            => enableRules && enforceShotClock && offenseTeamId >= 0 &&
               shotClockRemaining <= Mathf.Max(0f, thresholdSeconds);

        public bool ShouldExitPaint(BasketballTeamMember member)
        {
            int rosterIndex = FindRosterIndex(member);
            return enableRules && enforceOffensiveThreeSeconds &&
                   rosterIndex >= 0 && member.TeamId == offenseTeamId &&
                   laneTimers[rosterIndex] >=
                   Mathf.Max(0f, offensiveLaneSeconds - paintExitWarningSeconds);
        }

        public Vector3 GetPaintExitTarget(BasketballTeamMember member)
        {
            if (member?.Controller?.State == null || court == null)
            {
                return member != null ? member.transform.position : Vector3.zero;
            }

            Vector3 local = court.WorldToCourtPoint(
                member.Controller.State.ActorRootPosition);
            float attackSign = GetAttackSign(member.TeamId);
            float baseline = attackSign * court.CourtLength * 0.5f;
            float laneEdgeZ = baseline - attackSign * (paintDepth + 0.45f);
            local.z = laneEdgeZ;
            if (Mathf.Abs(local.x) < paintWidth * 0.5f + 0.35f)
            {
                local.x = Mathf.Sign(local.x == 0f ? member.PlayerIndex % 2 - 0.5f : local.x) *
                          (paintWidth * 0.5f + 0.55f);
            }
            local.y = 0f;
            return court.ClampToPlayableArea(
                court.CourtToWorldPoint(local),
                inboundPlayerInset,
                preserveHeight: false);
        }

        public void ResetRuleState()
        {
            violationRestartPending = false;
            violationRestartAt = float.NegativeInfinity;
            pendingInboundTeamId = -1;
            pendingInbounder = null;
            pendingOffender = null;
            pendingViolationPosition = Vector3.zero;
            pendingBallRotation = Quaternion.identity;
            EnsureBuffers();
            for (int index = 0; index < laneTimers.Length; index++)
            {
                laneTimers[index] = 0f;
            }
            offenseTeamId = possession != null && possession.Owner != null
                ? possession.Owner.TeamId
                : -1;
            lastTouchTeamId = offenseTeamId;
            shotTeamId = -1;
            shotInProgress = false;
            frontcourtEstablished = false;
            shotClockRemaining = Mathf.Max(1f, shotClockSeconds);
            backcourtRemaining = Mathf.Max(1f, backcourtSeconds);
            lastOwnerPlayerIndex = possession != null && possession.Owner != null
                ? possession.Owner.PlayerIndex
                : -1;
            lastProcessedEventSequence = worldEvents != null
                ? worldEvents.LatestSequence
                : 0;
            ResetTravelState();
        }

        private void SynchronizeTeamControl()
        {
            BasketballTeamMember owner = possession.Owner;
            if (owner == null)
            {
                return;
            }

            if (owner.TeamId != offenseTeamId)
            {
                bool offensiveRebound = shotInProgress && owner.TeamId == shotTeamId;
                offenseTeamId = owner.TeamId;
                shotClockRemaining = offensiveRebound
                    ? Mathf.Min(shotClockSeconds, offensiveReboundShotClockSeconds)
                    : shotClockSeconds;
                backcourtRemaining = backcourtSeconds;
                frontcourtEstablished = false;
                ClearLaneTimers();
                shotInProgress = false;
                shotTeamId = -1;
            }
            else if (shotInProgress && owner.TeamId == shotTeamId)
            {
                // Same-team control after a missed shot is an offensive rebound.
                shotClockRemaining = Mathf.Min(
                    shotClockSeconds,
                    offensiveReboundShotClockSeconds);
                shotInProgress = false;
                shotTeamId = -1;
            }

            if (owner.PlayerIndex != lastOwnerPlayerIndex)
            {
                lastOwnerPlayerIndex = owner.PlayerIndex;
                lastTouchTeamId = owner.TeamId;
                ResetTravelState();
            }
        }

        private void ConsumeWorldEvents()
        {
            if (worldEvents == null)
            {
                return;
            }
            int next = Mathf.Max(
                lastProcessedEventSequence + 1,
                worldEvents.OldestSequence);
            for (; next <= worldEvents.LatestSequence; next++)
            {
                if (!worldEvents.TryGetBySequence(next, out BasketballWorldEvent evt))
                {
                    continue;
                }
                BasketballTeamMember actor = FindPlayerByIndex(evt.ActorPlayerIndex);
                switch (evt.Type)
                {
                    case BasketballWorldEventType.PassReleased:
                        if (actor != null)
                        {
                            lastTouchTeamId = actor.TeamId;
                        }
                        break;
                    case BasketballWorldEventType.ShotReleased:
                        if (actor != null)
                        {
                            lastTouchTeamId = actor.TeamId;
                            shotTeamId = actor.TeamId;
                            shotInProgress = true;
                        }
                        break;
                    case BasketballWorldEventType.StealTouch:
                        if (actor != null)
                        {
                            lastTouchTeamId = actor.TeamId;
                        }
                        break;
                    case BasketballWorldEventType.ShotMade:
                        shotInProgress = false;
                        break;
                    case BasketballWorldEventType.MatchRestarted:
                        shotInProgress = false;
                        shotTeamId = -1;
                        break;
                }
            }
            lastProcessedEventSequence = worldEvents.LatestSequence;
        }

        private bool EvaluateBackcourt(BasketballTeamMember owner, float deltaTime)
        {
            float attackSign = GetAttackSign(offenseTeamId);
            Vector3 localBall = court.WorldToCourtPoint(ball.transform.position);
            float attackProgress = attackSign * localBall.z;
            if (!frontcourtEstablished)
            {
                if (attackProgress >= midcourtTolerance)
                {
                    frontcourtEstablished = true;
                    return false;
                }
                backcourtRemaining -= deltaTime;
                if (backcourtRemaining <= 0f)
                {
                    return CallViolation(
                        BasketballViolationType.BackcourtEightSeconds,
                        owner != null ? owner : possession.Passer,
                        offenseTeamId,
                        BasketballWorldEventType.BackcourtViolation);
                }
                return false;
            }

            if (attackProgress < -midcourtTolerance)
            {
                return CallViolation(
                    BasketballViolationType.BackcourtReturn,
                    owner != null ? owner : possession.Passer,
                    offenseTeamId,
                    BasketballWorldEventType.BackcourtViolation);
            }
            return false;
        }

        private bool EvaluateOffensiveLane(float deltaTime)
        {
            if (possession.BallState == BasketballPossessionState.ShotFlight ||
                possession.BallState == BasketballPossessionState.Loose ||
                possession.BallState == BasketballPossessionState.Contested)
            {
                ClearLaneTimers();
                return false;
            }

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt ||
                    member.TeamId != offenseTeamId || member.Controller?.State == null)
                {
                    laneTimers[index] = 0f;
                    continue;
                }

                bool inPaint = IsInsideAttackingPaint(
                    member.TeamId,
                    member.Controller.State.ActorRootPosition);
                laneTimers[index] = inPaint ? laneTimers[index] + deltaTime : 0f;
                if (laneTimers[index] >= offensiveLaneSeconds)
                {
                    return CallViolation(
                        BasketballViolationType.OffensiveThreeSeconds,
                        member,
                        offenseTeamId,
                        BasketballWorldEventType.ThreeSecondViolation);
                }
            }
            return false;
        }

        private bool EvaluateConservativeBallHandling(BasketballTeamMember owner)
        {
            BasketballAgentState state = owner.Controller.State;
            if (state == null)
            {
                return false;
            }
            BasketballIntent intent = owner.Controller.CurrentIntent;
            int pivot = BasketballAgentState.Pivot;
            float ground = state.Contacts[BasketballAgentState.ContactIndex(pivot, 4)];
            bool groundContact = ground >= dribbleGroundContactThreshold;

            if (intent.Hold)
            {
                if (float.IsNegativeInfinity(heldStartedAt))
                {
                    heldStartedAt = Time.time;
                    heldStartPosition = state.ActorRootPosition;
                }
                if (Time.time - heldStartedAt >= heldBallConfirmationSeconds)
                {
                    dribbleStopped = true;
                    if (enforceTraveling && Vector3.Distance(
                            heldStartPosition,
                            state.ActorRootPosition) > maximumHeldTravelDistance)
                    {
                        return CallViolation(
                            BasketballViolationType.Traveling,
                            owner,
                            owner.TeamId,
                            BasketballWorldEventType.TravelViolation);
                    }
                }
            }
            else
            {
                heldStartedAt = float.NegativeInfinity;
                if (enforceDoubleDribble && dribbleStopped && groundContact &&
                    !previousGroundContact)
                {
                    return CallViolation(
                        BasketballViolationType.DoubleDribble,
                        owner,
                        owner.TeamId,
                        BasketballWorldEventType.DoubleDribbleViolation);
                }
            }
            previousGroundContact = groundContact;
            return false;
        }

        private bool CallViolation(
            BasketballViolationType violation,
            BasketballTeamMember offender,
            int offendingTeamId,
            BasketballWorldEventType eventType)
        {
            if (offendingTeamId < 0)
            {
                return false;
            }
            BasketballTeamMember inbounder = FindClosestPlayer(
                1 - offendingTeamId,
                ball.transform.position);
            if (inbounder?.Controller?.State == null)
            {
                return false;
            }

            Vector3 violationPosition = ball.transform.position;
            Quaternion ballRotation = ball.transform.rotation;
            if (!possession.BeginDeadBall(violationPosition, ballRotation))
            {
                return false;
            }

            LastViolation = violation;
            LastViolationPlayerIndex = offender != null ? offender.PlayerIndex : -1;
            LastViolationTime = Time.time;
            violationRestartPending = true;
            violationRestartAt = Time.time + violationRestartDelay;
            pendingInboundTeamId = 1 - offendingTeamId;
            pendingInbounder = inbounder;
            pendingOffender = offender;
            pendingViolationPosition = violationPosition;
            pendingBallRotation = ballRotation;
            worldEvents?.Publish(
                eventType,
                possession.PossessionVersion,
                offender,
                inbounder,
                violationPosition,
                value: shotClockRemaining,
                skillVariant: (int)violation);
            return true;
        }

        private bool CompletePendingViolationRestart()
        {
            BasketballTeamMember inbounder = pendingInbounder;
            if (inbounder == null || !inbounder.IsOnCourt ||
                inbounder.Controller?.State == null)
            {
                inbounder = FindClosestPlayer(pendingInboundTeamId, pendingViolationPosition);
            }
            if (inbounder?.Controller?.State == null)
            {
                return false;
            }

            Vector3 forward = inbounder.Controller.State.ActorRootRotation * Vector3.forward;
            Vector3 ballPosition = inbounder.Controller.State.BonePositions[14] +
                                   0.2f * forward - 0.05f * court.CourtUp;
            BasketballViolationType completedViolation = LastViolation;
            BasketballTeamMember offender = pendingOffender;
            if (!possession.ResetPossession(inbounder, ballPosition, pendingBallRotation))
            {
                return false;
            }

            worldEvents?.Publish(
                BasketballWorldEventType.MatchRestarted,
                possession.PossessionVersion,
                inbounder,
                offender,
                ballPosition,
                skillVariant: (int)completedViolation);
            ResetRuleState();
            return true;
        }

        private bool IsBallOutOfBounds()
        {
            Vector3 local = court.WorldToCourtPoint(ball.transform.position);
            float halfWidth = court.CourtWidth * 0.5f + outOfBoundsTolerance;
            float halfLength = court.CourtLength * 0.5f + outOfBoundsTolerance;
            return Mathf.Abs(local.x) > halfWidth || Mathf.Abs(local.z) > halfLength;
        }

        private bool IsInsideAttackingPaint(int teamId, Vector3 worldPosition)
        {
            Vector3 local = court.WorldToCourtPoint(worldPosition);
            float attackSign = GetAttackSign(teamId);
            float baseline = attackSign * court.CourtLength * 0.5f;
            float depthFromBaseline = attackSign * (baseline - local.z);
            return Mathf.Abs(local.x) <= paintWidth * 0.5f &&
                   depthFromBaseline >= 0f && depthFromBaseline <= paintDepth;
        }

        private float GetAttackSign(int teamId)
        {
            BasketballHoop hoop = court.GetAttackHoop(teamId);
            if (hoop == null)
            {
                return teamId == 0 ? 1f : -1f;
            }
            return court.WorldToCourtPoint(hoop.Center).z >= 0f ? 1f : -1f;
        }

        private BasketballTeamMember FindClosestPlayer(int teamId, Vector3 position)
        {
            BasketballTeamMember best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt || member.TeamId != teamId ||
                    member.Controller?.State == null)
                {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(
                    member.Controller.State.ActorRootPosition - position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = member;
                }
            }
            return best;
        }

        private BasketballTeamMember FindPlayerByIndex(int playerIndex)
        {
            if (players == null)
            {
                return null;
            }
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] != null && players[index].PlayerIndex == playerIndex)
                {
                    return players[index];
                }
            }
            return null;
        }

        private int FindRosterIndex(BasketballTeamMember member)
        {
            if (players == null || member == null)
            {
                return -1;
            }
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] == member)
                {
                    return index;
                }
            }
            return -1;
        }

        private void EnsureBuffers()
        {
            int count = players != null ? players.Length : 0;
            if (laneTimers == null || laneTimers.Length != count)
            {
                laneTimers = new float[count];
            }
        }

        private void ClearLaneTimers()
        {
            if (laneTimers == null)
            {
                return;
            }
            for (int index = 0; index < laneTimers.Length; index++)
            {
                laneTimers[index] = 0f;
            }
        }

        private void ResetTravelState()
        {
            heldStartedAt = float.NegativeInfinity;
            heldStartPosition = Vector3.zero;
            dribbleStopped = false;
            previousGroundContact = false;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballTeamMember[] roster,
            BasketballBallController sharedBall,
            BasketballPossessionManager possessionManager,
            BasketballCourt basketballCourt,
            BasketballWorldEventStream eventStream)
        {
            players = roster;
            ball = sharedBall;
            possession = possessionManager;
            court = basketballCourt;
            worldEvents = eventStream;
        }
#endif
    }
}
