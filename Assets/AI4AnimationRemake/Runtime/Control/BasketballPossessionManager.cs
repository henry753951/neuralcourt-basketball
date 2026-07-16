using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class BasketballPossessionManager : MonoBehaviour, IBasketballPossessionAuthority
    {
        [SerializeField] private BasketballTeamGroup[] teamGroups;
        [SerializeField] private BasketballTeamMember[] players;
        [SerializeField] private BasketballBallController ball;
        [SerializeField] private BasketballCourt court;
        [SerializeField] private BasketballWorldEventStream worldEventStream;
        [SerializeField, Min(0.05f)] private float catchHandDistance = 0.36f;
        [SerializeField, Min(0.1f)] private float catchControlRadius = 1.25f;
        [SerializeField, Min(0.05f)] private float intendedReceiverCatchHandDistance = 0.52f;
        [SerializeField, Min(0.1f)] private float intendedReceiverControlRadius = 1.45f;
        [SerializeField, Min(0.05f)] private float intendedReceiverChestCatchRadius = 0.7f;
        [SerializeField, Min(0.1f)] private float maximumCatchRelativeSpeed = 14f;
        [SerializeField, Min(0f)] private float passCatchHandAssist = 0.12f;
        [SerializeField, Min(0f)] private float passCatchControlAssist = 0.2f;
        [SerializeField, Min(0f)] private float passCatchChestAssist = 0.15f;
        [SerializeField, Min(0f)] private float passCatchSpeedAssist = 2f;
        [SerializeField, Min(0.1f)] private float looseBallPickupRootRadius = 1.05f;
        [SerializeField, Min(0.05f)] private float looseBallPickupMaximumHeight = 0.55f;
        [SerializeField, Min(0.1f)] private float looseBallPickupMaximumSpeed = 3.5f;
        [SerializeField, Min(0f)] private float minimumFlightSecureSeconds = 0.12f;
        [SerializeField, Min(0f)] private float releasingPlayerCatchLockout = 0.45f;
        [SerializeField, Min(0.05f)] private float stealHandDistance = 0.4f;
        [SerializeField, Min(0.1f)] private float stealInteractionRadius = 1.45f;
        [SerializeField, Range(0f, 1f)] private float minimumStealExposure = 0.28f;
        [SerializeField, Range(0f, 1f)] private float minimumStealApproach = 0.12f;
        [SerializeField, Range(0f, 1f)] private float stealQualityThreshold = 0.63f;
        [SerializeField, Range(0f, 1f)] private float cleanStealQuality = 0.78f;
        [SerializeField, Min(0.05f)] private float cleanStealHandDistance = 0.22f;
        [SerializeField, Min(0.01f)] private float stealSecureDelay = 0.16f;
        [SerializeField, Min(0.05f)] private float stealSecureWindow = 0.35f;
        [SerializeField, Range(0f, 1f)] private float minimumStealSecureQuality = 0.68f;
        [SerializeField, Min(0.05f)] private float stealSecureHandDistance = 0.28f;
        [SerializeField, Min(0.1f)] private float maximumStealSecureRelativeSpeed = 6.5f;
        [SerializeField, Min(0.05f)] private float contestedTimeout = 0.8f;
        [SerializeField, Min(0f)] private float possessionCooldown = 0.65f;
        [SerializeField, Min(0.05f)] private float catchBlendSeconds = 0.18f;
        [SerializeField, HideInInspector] private int interactionTuningVersion;

        private BasketballBallObservation[] observations;
        private float[] observationTimes;
        private BasketballTeamMember owner;
        private BasketballTeamMember previousOwner;
        private BasketballTeamMember passer;
        private BasketballTeamMember intendedReceiver;
        private BasketballPassPlan passPlan;
        private BasketballShotPlan lastShotPlan;
        private float bestReleaseScore;
        private float bestReleaseDirectionError;
        private float bestReleaseSpeedScale;
        private float bestReleaseVerticalError;
        private float passStartedAt;
        private float shotIntentStartedAt = float.NegativeInfinity;
        private float flightStartedAt;
        private float catchBlendStartedAt;
        private float contestStartedAt = float.NegativeInfinity;
        private float lastPossessionChangeTime;
        private float lastStealTouchTime = float.NegativeInfinity;
        private BasketballTeamMember stealTouchCandidate;
        private float stealTouchCandidateQuality;
        private float stealTouchStartedAt = float.NegativeInfinity;
        private float stealTouchExpiresAt = float.NegativeInfinity;
        private Vector3 previousPhysicsBallPosition;
        private bool hasPreviousPhysicsBallPosition;
        private bool currentShotScored;
        private BasketballPossessionState flightOriginState =
            BasketballPossessionState.Possessed;
        private bool initialized;

        private BasketballRuntimeSettings RuntimeSettings =>
            BasketballRuntimeSettings.LoadDefault();

        public BasketballTeamMember Owner => owner;
        public BasketballTeamMember PreviousOwner => previousOwner;
        public BasketballTeamMember Passer => passer;
        public BasketballTeamMember IntendedReceiver => intendedReceiver;
        public BasketballPassPlan CurrentPass => passPlan;
        public BasketballShotPlan LastShotPlan => lastShotPlan;
        public BasketballCourt Court => court;
        public BasketballPossessionState BallState { get; private set; }
        public BasketballBallControlMode BallControlMode { get; private set; }
        public int PossessionVersion { get; private set; }
        public float ExpectedArrivalTime => flightStartedAt + passPlan.ExpectedFlightTime;
        public float LastPossessionChangeTime => lastPossessionChangeTime;
        public float LastReleaseCandidateScore { get; private set; }
        public float LastReleaseDirectionError { get; private set; }
        public float LastReleaseSpeedScale { get; private set; }
        public float LastReleaseVerticalError { get; private set; }
        public string LastPassFailureReason { get; private set; }
        public float LastBallExposureScore { get; private set; }
        public float LastStealContactQuality { get; private set; }
        public bool IsShotReleasePending =>
            BallState == BasketballPossessionState.Possessed && owner != null &&
            !float.IsNegativeInfinity(shotIntentStartedAt);
        public BasketballWorldEventStream WorldEvents => worldEventStream;

        public void SetWorldEventStream(BasketballWorldEventStream eventStream)
        {
            worldEventStream = eventStream;
        }

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!initialized)
            {
                Initialize();
            }
            if (initialized)
            {
                UpdateInteractionTargets();
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            switch (BallState)
            {
                case BasketballPossessionState.Possessed:
                    ResolvePossessedSteal();
                    UpdatePossessedBallMode();
                    break;
                case BasketballPossessionState.PassPreparing:
                    ResolvePassPreparationTimeout();
                    break;
                case BasketballPossessionState.PassFlight:
                case BasketballPossessionState.ShotFlight:
                case BasketballPossessionState.Loose:
                case BasketballPossessionState.Contested:
                    ResolvePhysicsBall();
                    break;
                case BasketballPossessionState.CatchBlend:
                    ResolveCatchBlend();
                    break;
                case BasketballPossessionState.DeadBall:
                    break;
            }
        }

        public void ConfigurePlayers(System.Collections.Generic.IEnumerable<BasketballTeamMember> newPlayers)
        {
            if (newPlayers == null) return;
            players = new System.Collections.Generic.List<BasketballTeamMember>(newPlayers).ToArray();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && teamGroups != null && teamGroups.Length > 0)
            {
                var discoveredPlayers = new System.Collections.Generic.List<BasketballTeamMember>();
                foreach (var group in teamGroups)
                {
                    if (group != null)
                    {
                        discoveredPlayers.AddRange(group.CollectMembers());
                    }
                }
                if (discoveredPlayers.Count > 0)
                {
                    discoveredPlayers.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
                    players = discoveredPlayers.ToArray();
                }
            }
        }
#endif

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (teamGroups != null && teamGroups.Length > 0)
            {
                var discoveredPlayers = new System.Collections.Generic.List<BasketballTeamMember>();
                foreach (var group in teamGroups)
                {
                    if (group != null)
                    {
                        discoveredPlayers.AddRange(group.CollectMembers());
                    }
                }
                if (discoveredPlayers.Count > 0)
                {
                    discoveredPlayers.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
                    players = discoveredPlayers.ToArray();
                }
            }

            if (players == null || players.Length == 0 || ball == null)
            {
                return;
            }

            observations = new BasketballBallObservation[players.Length];
            observationTimes = new float[players.Length];
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || member.Controller == null)
                {
                    return;
                }
                member.Controller.Initialize();
                member.Controller.SetPossessionAuthority(this);
            }

            initialized = true;
            BasketballTeamMember initialOwner = null;
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] != null && players[index].IsOnCourt)
                {
                    initialOwner = players[index];
                    break;
                }
            }
            if (initialOwner == null)
            {
                return;
            }
            SetOwner(initialOwner, reacquire: false);
            BallState = BasketballPossessionState.Possessed;
            BallControlMode = BasketballBallControlMode.NeuralPossession;
            ball.SetState(BasketballBallAuthorityState.Controlled);
            UpdateInteractionTargets();
        }

        public bool HasBall(BasketballNeuralController player)
            => owner != null && owner.Controller == player;

        public bool CanWriteBall(BasketballNeuralController player)
        {
            if (!HasBall(player))
            {
                return false;
            }
            return BallControlMode == BasketballBallControlMode.NeuralPossession ||
                   BallControlMode == BasketballBallControlMode.NeuralPassPreparation ||
                   BallControlMode == BasketballBallControlMode.CatchBlend;
        }

        public bool IsIntendedReceiver(BasketballTeamMember member)
            => member != null && intendedReceiver == member &&
               (BallState == BasketballPossessionState.PassPreparing ||
                BallState == BasketballPossessionState.PassFlight ||
                BallState == BasketballPossessionState.Contested);

        public bool ResetPossession(
            BasketballTeamMember newOwner,
            Vector3 controlledBallPosition,
            Quaternion controlledBallRotation)
        {
            if (!initialized || newOwner == null || !newOwner.IsOnCourt ||
                newOwner.Controller == null ||
                FindPlayerIndex(newOwner.Controller) < 0)
            {
                return false;
            }

            SetOwner(null, reacquire: false);
            passer = null;
            intendedReceiver = null;
            passPlan = default;
            lastShotPlan = default;
            currentShotScored = false;
            flightOriginState = BasketballPossessionState.Possessed;
            bestReleaseScore = 0f;
            contestStartedAt = float.NegativeInfinity;
            passStartedAt = float.NegativeInfinity;
            shotIntentStartedAt = float.NegativeInfinity;
            flightStartedAt = float.NegativeInfinity;
            catchBlendStartedAt = float.NegativeInfinity;
            ClearStealTouchCandidate();
            ball.SetState(BasketballBallAuthorityState.Controlled);
            ball.SetControlledPose(
                controlledBallPosition,
                controlledBallRotation,
                Vector3.zero);

            // A made-basket restart is an explicit simulation reset point. Seed
            // every recurrent ball history with the same authoritative pose so
            // no old shot velocity leaks into the next possession.
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                BasketballAgentState state = players[playerIndex]?.Controller?.State;
                if (state == null)
                {
                    continue;
                }
                for (int sample = 0; sample < BasketballAgentState.SampleCount; sample++)
                {
                    state.BallPositions[sample] = controlledBallPosition;
                    state.BallRotations[sample] = controlledBallRotation;
                    state.BallVelocities[sample] = Vector3.zero;
                }
            }

            SetOwner(newOwner, reacquire: false);
            BallState = BasketballPossessionState.Possessed;
            BallControlMode = BasketballBallControlMode.NeuralPossession;
            hasPreviousPhysicsBallPosition = false;
            UpdateInteractionTargets();
            return true;
        }

        public bool BeginDeadBall(Vector3 controlledBallPosition, Quaternion controlledBallRotation)
        {
            if (!initialized || ball == null)
            {
                return false;
            }

            SetOwner(null, reacquire: false);
            passer = null;
            intendedReceiver = null;
            passPlan = default;
            lastShotPlan = default;
            currentShotScored = false;
            bestReleaseScore = 0f;
            passStartedAt = float.NegativeInfinity;
            flightStartedAt = float.NegativeInfinity;
            catchBlendStartedAt = float.NegativeInfinity;
            contestStartedAt = float.NegativeInfinity;
            ClearStealTouchCandidate();
            BallState = BasketballPossessionState.DeadBall;
            BallControlMode = BasketballBallControlMode.DeadBall;
            hasPreviousPhysicsBallPosition = false;
            ball.SetState(BasketballBallAuthorityState.Controlled);
            ball.SetControlledPose(
                controlledBallPosition,
                controlledBallRotation,
                Vector3.zero);
            UpdateInteractionTargets();
            return true;
        }

        public bool ReconcileActiveRoster(BasketballTeamMember fallbackOwner)
        {
            if (!initialized || fallbackOwner == null || !fallbackOwner.IsOnCourt ||
                fallbackOwner.Controller?.State == null)
            {
                return false;
            }

            if (owner == null || !owner.IsOnCourt)
            {
                BasketballAgentState state = fallbackOwner.Controller.State;
                Vector3 forward = state.ActorRootRotation * Vector3.forward;
                Vector3 controlledPosition = fallbackOwner.AimPoint + 0.16f * forward;
                return ResetPossession(
                    fallbackOwner,
                    controlledPosition,
                    ball.transform.rotation);
            }

            if (intendedReceiver != null && !intendedReceiver.IsOnCourt)
            {
                if (BallState == BasketballPossessionState.PassPreparing)
                {
                    FailPassPreparation();
                }
                else
                {
                    intendedReceiver = null;
                    passPlan.Receiver = null;
                }
            }
            if (stealTouchCandidate != null && !stealTouchCandidate.IsOnCourt)
            {
                ClearStealTouchCandidate();
            }
            UpdateInteractionTargets();
            return true;
        }

        public bool RequestPass(
            BasketballTeamMember requestPasser,
            BasketballTeamMember receiver,
            BasketballPassType passType = BasketballPassType.Lead)
        {
            if (!initialized || requestPasser == null || receiver == null ||
                !requestPasser.IsOnCourt || !receiver.IsOnCourt ||
                owner != requestPasser || requestPasser == receiver ||
                requestPasser.TeamId != receiver.TeamId ||
                BallState != BasketballPossessionState.Possessed)
            {
                return false;
            }

            PossessionVersion++;
            passer = requestPasser;
            intendedReceiver = receiver;
            passPlan = BasketballPassPlanner.Create(
                requestPasser,
                receiver,
                ball.transform.position,
                PossessionVersion,
                passType,
                RuntimeSettings);
            passStartedAt = Time.time;
            bestReleaseScore = 0f;
            bestReleaseDirectionError = 180f;
            bestReleaseSpeedScale = float.PositiveInfinity;
            bestReleaseVerticalError = float.PositiveInfinity;
            LastReleaseCandidateScore = 0f;
            LastReleaseDirectionError = 180f;
            LastReleaseSpeedScale = float.PositiveInfinity;
            LastReleaseVerticalError = float.PositiveInfinity;
            LastPassFailureReason = null;
            BallState = BasketballPossessionState.PassPreparing;
            BallControlMode = BasketballBallControlMode.NeuralPassPreparation;
            worldEventStream?.Publish(
                BasketballWorldEventType.PassRequested,
                PossessionVersion,
                requestPasser,
                receiver,
                ball.transform.position,
                passPlan.DesiredReleaseVelocity,
                value: passPlan.ExpectedFlightTime,
                targetPosition: passPlan.PredictedCatchPoint,
                skillVariant: (int)passType);
            return true;
        }

        public bool TryGetPassControl(
            BasketballTeamMember member,
            out BasketballPassControlProfile profile)
        {
            profile = default;
            if (member == null || member != owner || member != passer ||
                BallState != BasketballPossessionState.PassPreparing ||
                passPlan.PossessionVersion != PossessionVersion)
            {
                return false;
            }

            float elapsed = Time.time - passStartedAt;
            BasketballRuntimeSettings settings = RuntimeSettings;
            float gatherEnd = settings != null ? settings.PassGatherSeconds : 0.08f;
            float alignEnd = settings != null ? settings.PassAlignSeconds : 0.16f;
            float pushEnd = settings != null ? settings.PassPushSeconds : 0.34f;
            BasketballMLPassPhase phase;
            if (elapsed < gatherEnd)
            {
                phase = BasketballMLPassPhase.Gather;
            }
            else if (elapsed < alignEnd)
            {
                phase = BasketballMLPassPhase.Align;
            }
            else if (elapsed < pushEnd)
            {
                phase = BasketballMLPassPhase.Push;
            }
            else
            {
                phase = BasketballMLPassPhase.ReleasePending;
            }

            BasketballPassPlanner.RefreshRelease(
                ref passPlan,
                member.Controller.State.BallPositions[BasketballAgentState.Pivot],
                RuntimeSettings);

            float hold;
            float shoot;
            switch (phase)
            {
                case BasketballMLPassPhase.Gather:
                    hold = 1f;
                    shoot = 0f;
                    break;
                case BasketballMLPassPhase.Align:
                    hold = 0.9f;
                    shoot = 0f;
                    break;
                case BasketballMLPassPhase.Push:
                    float push = Mathf.InverseLerp(alignEnd, pushEnd, elapsed);
                    hold = Mathf.Lerp(0.8f, 0.3f, push);
                    shoot = 0f;
                    break;
                default:
                    hold = 0.15f;
                    shoot = 0f;
                    break;
            }

            profile = new BasketballPassControlProfile(
                phase,
                hold,
                shoot,
                passPlan.DesiredReleaseDirection);
            return true;
        }

        public void CancelPassPreparation(BasketballTeamMember requestPasser)
        {
            if (BallState != BasketballPossessionState.PassPreparing ||
                requestPasser == null || requestPasser != owner)
            {
                return;
            }
            FailPassPreparation();
        }

        public void NotifyShotScored()
        {
            if (BallState == BasketballPossessionState.ShotFlight &&
                lastShotPlan.Shooter != null)
            {
                currentShotScored = true;
            }
        }

        public void ReportNeuralTick(
            BasketballNeuralController player,
            in BasketballBallObservation observation)
        {
            int index = FindPlayerIndex(player);
            if (index < 0)
            {
                return;
            }
            observations[index] = observation;
            observationTimes[index] = Time.time;

            if (!HasBall(player))
            {
                return;
            }

            if (BallState == BasketballPossessionState.PassPreparing)
            {
                EvaluatePassRelease(observation);
            }
            else if (BallState == BasketballPossessionState.Possessed)
            {
                EvaluateShotRelease(observation);
            }
        }

        private void EvaluateShotRelease(in BasketballBallObservation observation)
        {
            if (!observation.ShootIntent &&
                float.IsNegativeInfinity(shotIntentStartedAt))
            {
                return;
            }
            if (float.IsNegativeInfinity(shotIntentStartedAt))
            {
                shotIntentStartedAt = Time.time;
            }

            BasketballRuntimeSettings settings = RuntimeSettings;
            float elapsed = Time.time - shotIntentStartedAt;
            float minimumSeconds = settings != null
                ? settings.MinimumShotReleaseSeconds
                : 0.2f;
            float forcedSeconds = settings != null
                ? settings.ForcedShotReleaseSeconds
                : 0.58f;
            float minimumHeight = settings != null
                ? settings.MinimumShotReleaseHeight
                : 1.25f;
            float contactThreshold = settings != null
                ? settings.NaturalShotReleaseHandContact
                : 0.18f;
            float heightAboveCourt = court != null
                ? Vector3.Dot(
                    observation.Position - court.CourtCenter,
                    court.CourtUp)
                : observation.Position.y;
            bool naturalRelease = elapsed >= minimumSeconds &&
                                  heightAboveCourt >= minimumHeight &&
                                  observation.HandContact <= contactThreshold;
            if (naturalRelease || elapsed >= forcedSeconds)
            {
                ReleaseShot(observation);
            }
        }

        private void EvaluatePassRelease(in BasketballBallObservation observation)
        {
            if (passPlan.PossessionVersion != PossessionVersion)
            {
                return;
            }

            float elapsed = Time.time - passStartedAt;
            BasketballRuntimeSettings settings = RuntimeSettings;
            float minimumReleaseSeconds = settings != null
                ? settings.MinimumPassReleaseSeconds
                : 0.3f;
            if (elapsed < minimumReleaseSeconds)
            {
                return;
            }

            BasketballPassPlanner.RefreshRelease(
                ref passPlan,
                observation.Position,
                RuntimeSettings);
            float score = BasketballPassReleaseDetector.Evaluate(passPlan, observation);
            BasketballPassReleaseDetector.MeasureErrors(
                passPlan,
                observation,
                out float directionError,
                out float speedScale,
                out float verticalError);
            LastReleaseCandidateScore = score;
            LastReleaseDirectionError = directionError;
            LastReleaseSpeedScale = speedScale;
            LastReleaseVerticalError = verticalError;
            if (score > bestReleaseScore)
            {
                bestReleaseScore = score;
                bestReleaseDirectionError = directionError;
                bestReleaseSpeedScale = speedScale;
                bestReleaseVerticalError = verticalError;
            }

            Vector3 desiredFacing = Vector3.ProjectOnPlane(
                passPlan.DesiredReleaseDirection,
                Vector3.up);
            Vector3 currentFacing = Vector3.ProjectOnPlane(
                observation.RootForward,
                Vector3.up);
            float facingAlignment = desiredFacing.sqrMagnitude > 1e-8f &&
                                    currentFacing.sqrMagnitude > 1e-8f
                ? Vector3.Dot(desiredFacing.normalized, currentFacing.normalized)
                : 1f;
            BasketballAgentState passerState = passer != null
                ? passer.Controller.State
                : null;
            float chestY = passerState != null
                ? passerState.BonePositions[14].y
                : observation.RootPosition.y + 1.2f;
            float belowChestTolerance = settings != null
                ? settings.PassReleaseBelowChestTolerance
                : 0.48f;
            float forcedReleaseSeconds = settings != null
                ? settings.ForcedPassReleaseSeconds
                : 0.84f;
            bool releaseHeightReady = observation.Position.y >=
                                      chestY - belowChestTolerance;
            bool releaseReady = elapsed >= minimumReleaseSeconds &&
                                ((facingAlignment >= 0.35f && releaseHeightReady) ||
                                 elapsed >= forcedReleaseSeconds);
            if (!releaseReady)
            {
                return;
            }

            // Pass direction comes from the pass plan, not Ball Target. Apply the
            // computed projectile velocity once at release; physics owns all flight.
            BasketballPassPlanner.RefreshRelease(
                ref passPlan,
                observation.Position,
                RuntimeSettings);
            ReleasePass(observation, passPlan.DesiredReleaseVelocity);
        }

        private void ReleasePass(
            in BasketballBallObservation observation,
            Vector3 releaseVelocity)
        {
            BasketballTeamMember releasingPlayer = owner;
            SetOwner(null, reacquire: false);
            passer = releasingPlayer;
            BallState = BasketballPossessionState.PassFlight;
            flightOriginState = BasketballPossessionState.PassFlight;
            BallControlMode = BasketballBallControlMode.PhysicsFlight;
            flightStartedAt = Time.time;
            contestStartedAt = float.NegativeInfinity;
            LastPassFailureReason = null;
            BeginPhysicsTracking(observation.Position);
            ball.ReleaseFromPose(
                observation.Position,
                ball.transform.rotation,
                releaseVelocity,
                Vector3.zero);
            worldEventStream?.Publish(
                BasketballWorldEventType.PassReleased,
                PossessionVersion,
                releasingPlayer,
                intendedReceiver,
                observation.Position,
                releaseVelocity,
                value: passPlan.ExpectedFlightTime,
                targetPosition: passPlan.PredictedCatchPoint,
                skillVariant: (int)passPlan.PassType);
        }

        private void ReleaseShot(in BasketballBallObservation observation)
        {
            BasketballTeamMember releasingPlayer = owner;
            Vector3 releaseVelocity = observation.Velocity;
            if (BasketballShotPlanner.TryCreate(
                    releasingPlayer,
                    court,
                    observation.Position,
                    observation.Velocity,
                    RuntimeSettings,
                    out BasketballShotPlan plannedShot))
            {
                lastShotPlan = plannedShot;
                releaseVelocity = plannedShot.DesiredVelocity;
            }
            else
            {
                lastShotPlan = default;
            }
            currentShotScored = false;
            SetOwner(null, reacquire: false);
            passer = releasingPlayer;
            intendedReceiver = null;
            passPlan = default;
            BallState = BasketballPossessionState.ShotFlight;
            flightOriginState = BasketballPossessionState.ShotFlight;
            BallControlMode = BasketballBallControlMode.PhysicsFlight;
            flightStartedAt = Time.time;
            contestStartedAt = float.NegativeInfinity;
            BeginPhysicsTracking(observation.Position);
            ball.ReleaseFromPose(
                observation.Position,
                ball.transform.rotation,
                releaseVelocity,
                Vector3.zero);
            worldEventStream?.Publish(
                BasketballWorldEventType.ShotReleased,
                PossessionVersion,
                releasingPlayer,
                position: observation.Position,
                velocity: releaseVelocity,
                value: lastShotPlan.Distance,
                targetPosition: lastShotPlan.TargetPoint);
        }

        private void ResolvePassPreparationTimeout()
        {
            if (Time.time - passStartedAt >= 1.2f)
            {
                FailPassPreparation(reportModelFailure: true);
            }
        }

        private void FailPassPreparation(bool reportModelFailure = false)
        {
            BasketballTeamMember failedPasser = passer;
            BasketballTeamMember failedReceiver = intendedReceiver;
            BasketballPassPlan failedPlan = passPlan;
            float failedScore = bestReleaseScore;
            if (reportModelFailure)
            {
                LastReleaseCandidateScore = bestReleaseScore;
                LastReleaseDirectionError = bestReleaseDirectionError;
                LastReleaseSpeedScale = bestReleaseSpeedScale;
                LastReleaseVerticalError = bestReleaseVerticalError;
                LastPassFailureReason =
                    $"Pass preparation timed out: score {bestReleaseScore:F2}, " +
                    $"direction {LastReleaseDirectionError:F1} deg, " +
                    $"speed scale {LastReleaseSpeedScale:F2}, " +
                    $"vertical error {LastReleaseVerticalError:F2} m/s";
                Debug.LogWarning(LastPassFailureReason, this);
            }
            PossessionVersion++;
            passer = null;
            intendedReceiver = null;
            passPlan = default;
            bestReleaseScore = 0f;
            BallState = BasketballPossessionState.Possessed;
            BallControlMode = BasketballBallControlMode.NeuralPossession;
            worldEventStream?.Publish(
                BasketballWorldEventType.PassPreparationFailed,
                PossessionVersion,
                failedPasser,
                failedReceiver,
                ball.transform.position,
                value: failedScore,
                targetPosition: failedPlan.PredictedCatchPoint,
                skillVariant: (int)failedPlan.PassType);
        }

        private void ResolvePhysicsBall()
        {
            if (stealTouchCandidate != null && Time.time > stealTouchExpiresAt)
            {
                ClearStealTouchCandidate();
            }

            BasketballTeamMember best = null;
            float bestQuality = 0f;
            float secondQuality = 0f;
            float bestHandDistance = float.PositiveInfinity;
            float bestControlDistance = float.PositiveInfinity;
            float bestRelativeSpeed = float.PositiveInfinity;
            Vector3 currentBallPosition = ball.transform.position;
            Vector3 previousBallPosition = hasPreviousPhysicsBallPosition
                ? previousPhysicsBallPosition
                : currentBallPosition;
            previousPhysicsBallPosition = currentBallPosition;
            hasPreviousPhysicsBallPosition = true;
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] == null || !players[index].IsOnCourt)
                {
                    continue;
                }
                if (!HasFreshObservation(index))
                {
                    continue;
                }
                BasketballBallObservation observation = RefreshLiveContactObservation(
                    players[index],
                    observations[index],
                    ball.transform.position,
                    ball.Velocity);
                float rootDistance = Vector3.Distance(
                    observation.RootPosition,
                    ball.transform.position);
                float sweptRootDistance = DistanceToSegment(
                    observation.RootPosition,
                    previousBallPosition,
                    currentBallPosition);
                float relativeSpeed = (ball.Velocity -
                    observation.ClosestHandVelocity).magnitude;
                float heightAboveCourt = court != null
                    ? Vector3.Dot(
                        currentBallPosition - court.CourtCenter,
                        court.CourtUp)
                    : currentBallPosition.y;
                bool loosePickupWindow = BallState == BasketballPossessionState.Loose &&
                                         rootDistance <= looseBallPickupRootRadius &&
                                         heightAboveCourt <=
                                             ball.Radius + looseBallPickupMaximumHeight &&
                                         ball.Velocity.magnitude <=
                                             looseBallPickupMaximumSpeed;
                if (!observation.CatchIntent &&
                    !observation.StealIntent)
                {
                    continue;
                }

                float flightAge = Time.time - flightStartedAt;
                bool activeFlight = BallState == BasketballPossessionState.PassFlight ||
                                    BallState == BasketballPossessionState.ShotFlight;
                if (activeFlight && flightAge < minimumFlightSecureSeconds)
                {
                    continue;
                }
                if (activeFlight && players[index] == previousOwner &&
                    flightAge < releasingPlayerCatchLockout)
                {
                    continue;
                }

                float handDistance = MinimumSweptHandDistance(
                    observation,
                    currentBallPosition);
                handDistance = Mathf.Min(
                    handDistance,
                    Mathf.Min(
                        DistanceToSegment(
                            observation.LeftHandPosition,
                            previousBallPosition,
                            currentBallPosition),
                        DistanceToSegment(
                            observation.RightHandPosition,
                            previousBallPosition,
                            currentBallPosition)));
                bool loosePickup = loosePickupWindow &&
                                   observation.CatchIntent &&
                                   handDistance <= Mathf.Min(
                                       catchHandDistance,
                                       stealSecureHandDistance);
                bool isIntendedReceiver = players[index] == intendedReceiver &&
                                          (BallState == BasketballPossessionState.PassFlight ||
                                           BallState == BasketballPossessionState.Contested);
                Vector3 chestPosition = players[index].Controller.State.BonePositions[14];
                Vector3 previousBallToChest = chestPosition - previousBallPosition;
                float chestDistance = DistanceToSegment(
                    chestPosition,
                    previousBallPosition,
                    currentBallPosition);
                float incomingAlignment = previousBallToChest.sqrMagnitude > 1e-8f &&
                                          ball.Velocity.sqrMagnitude > 1e-8f
                    ? Vector3.Dot(ball.Velocity.normalized, previousBallToChest.normalized)
                    : 1f;
                bool intendedBodyCatch = isIntendedReceiver &&
                                          observation.CatchIntent &&
                                          Time.time - flightStartedAt >= 0.08f &&
                                          chestDistance <=
                                              intendedReceiverChestCatchRadius +
                                              passCatchChestAssist &&
                                          (Vector3.Distance(
                                               chestPosition,
                                               currentBallPosition) <= 0.24f ||
                                           incomingAlignment >= 0.05f);
                float allowedHandDistance = isIntendedReceiver
                    ? intendedReceiverCatchHandDistance + passCatchHandAssist
                    : catchHandDistance;
                float allowedControlRadius = isIntendedReceiver
                    ? intendedReceiverControlRadius + passCatchControlAssist
                    : catchControlRadius;
                float allowedRelativeSpeed = maximumCatchRelativeSpeed +
                    (isIntendedReceiver ? passCatchSpeedAssist : 0f);
                float controlDistance = isIntendedReceiver
                    ? Mathf.Min(rootDistance, sweptRootDistance)
                    : rootDistance;
                if ((!intendedBodyCatch &&
                     !loosePickup &&
                     handDistance > allowedHandDistance) ||
                    controlDistance > allowedControlRadius ||
                    relativeSpeed > allowedRelativeSpeed)
                {
                    continue;
                }

                float quality =
                    0.55f * (1f - handDistance / allowedHandDistance) +
                    0.2f * (1f - relativeSpeed / allowedRelativeSpeed) +
                    0.15f * observation.HandContact;
                if (isIntendedReceiver)
                {
                    quality += 0.12f;
                }
                if (intendedBodyCatch)
                {
                    float assistedChestRadius = intendedReceiverChestCatchRadius +
                                                passCatchChestAssist;
                    float chestProximity = 1f -
                        chestDistance / assistedChestRadius;
                    float approachQuality = Mathf.InverseLerp(
                        0.05f,
                        0.85f,
                        incomingAlignment);
                    quality = Mathf.Max(
                        quality,
                        0.38f + 0.35f * chestProximity + 0.12f * approachQuality);
                }
                if (loosePickup)
                {
                    float rootProximity = 1f -
                        rootDistance / looseBallPickupRootRadius;
                    quality = Mathf.Max(
                        quality,
                        0.44f + 0.18f * Mathf.Clamp01(rootProximity));
                }
                if (players[index] == stealTouchCandidate)
                {
                    quality += Mathf.Lerp(0.08f, 0.2f, stealTouchCandidateQuality);
                }
                quality = Mathf.Clamp01(quality);
                if (quality > bestQuality)
                {
                    secondQuality = bestQuality;
                    bestQuality = quality;
                    best = players[index];
                    bestHandDistance = handDistance;
                    bestControlDistance = controlDistance;
                    bestRelativeSpeed = relativeSpeed;
                }
                else if (quality > secondQuality)
                {
                    secondQuality = quality;
                }
            }

            bool intendedPassCatch = best == intendedReceiver &&
                                     (BallState == BasketballPossessionState.PassFlight ||
                                      BallState == BasketballPossessionState.Contested);
            float requiredSecureQuality = best == stealTouchCandidate
                ? 0.52f
                : intendedPassCatch ? 0.34f : 0.42f;
            if (best != null && bestQuality >= requiredSecureQuality)
            {
                bool securingStealTouch = best == stealTouchCandidate;
                if (securingStealTouch &&
                    Time.time - stealTouchStartedAt < stealSecureDelay)
                {
                    return;
                }
                if (securingStealTouch &&
                    (bestQuality < minimumStealSecureQuality ||
                     bestHandDistance > stealSecureHandDistance ||
                     bestControlDistance > Mathf.Min(catchControlRadius, 0.9f) ||
                     bestRelativeSpeed > maximumStealSecureRelativeSpeed))
                {
                    return;
                }
                if (secondQuality >= bestQuality - 0.06f)
                {
                    if (BallState != BasketballPossessionState.Contested)
                    {
                        contestStartedAt = Time.time;
                        worldEventStream?.Publish(
                            BasketballWorldEventType.BallContested,
                            PossessionVersion,
                            best,
                            intendedReceiver,
                            ball.transform.position,
                            ball.Velocity,
                            bestQuality);
                    }
                    BallState = BasketballPossessionState.Contested;
                    BallControlMode = BasketballBallControlMode.PhysicsContested;
                    return;
                }
                if (best == stealTouchCandidate)
                {
                    CompleteContactSteal(
                        best,
                        previousOwner,
                        ball.transform.position,
                        ball.transform.rotation,
                        ball.Velocity,
                        bestQuality);
                }
                else
                {
                    CompleteCatch(best);
                }
                return;
            }

            if (BallState == BasketballPossessionState.PassFlight &&
                Time.time - flightStartedAt > passPlan.ExpectedFlightTime + 0.65f)
            {
                MarkLoose();
            }
            else if ((BallState == BasketballPossessionState.PassFlight ||
                      BallState == BasketballPossessionState.ShotFlight) &&
                     ball.transform.position.y <= ball.Radius + 0.04f &&
                     Time.time - flightStartedAt > 0.15f)
            {
                MarkLoose();
            }
            else if (BallState == BasketballPossessionState.Contested &&
                     Time.time - contestStartedAt >= contestedTimeout)
            {
                MarkLoose();
            }
        }

        private void ResolvePossessedSteal()
        {
            LastBallExposureScore = 0f;
            LastStealContactQuality = 0f;
            if (owner == null || IsShotReleasePending ||
                Time.time - lastStealTouchTime < possessionCooldown)
            {
                return;
            }

            int ownerIndex = FindPlayerIndex(owner.Controller);
            if (ownerIndex < 0 || !HasFreshObservation(ownerIndex))
            {
                return;
            }
            BasketballBallObservation ownerObservation = RefreshLiveContactObservation(
                owner,
                observations[ownerIndex],
                observations[ownerIndex].Position,
                observations[ownerIndex].Velocity);

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember defender = players[index];
                if (defender == null || !defender.IsOnCourt || defender == owner ||
                    defender.TeamId == owner.TeamId ||
                    !HasFreshObservation(index))
                {
                    continue;
                }
                BasketballBallObservation defenderObservation = RefreshLiveContactObservation(
                    defender,
                    observations[index],
                    ownerObservation.Position,
                    ownerObservation.Velocity);
                if (!defenderObservation.StealIntent ||
                    Vector3.Distance(
                        defenderObservation.RootPosition,
                        ownerObservation.RootPosition) > stealInteractionRadius)
                {
                    continue;
                }

                float handDistance = MinimumSweptHandDistance(
                    defenderObservation,
                    ownerObservation.Position);
                if (handDistance > stealHandDistance)
                {
                    continue;
                }

                Vector3 closestHand = Vector3.SqrMagnitude(
                    defenderObservation.LeftHandPosition - ownerObservation.Position) <=
                    Vector3.SqrMagnitude(
                        defenderObservation.RightHandPosition - ownerObservation.Position)
                    ? defenderObservation.LeftHandPosition
                    : defenderObservation.RightHandPosition;
                if (owner.BodyContact != null && owner.BodyContact.OccludesHandPath(
                        owner.Controller.State,
                        closestHand,
                        ownerObservation.Position))
                {
                    // A hand that reaches the ball only by crossing the carrier's
                    // torso is not a legal steal touch.
                    continue;
                }

                float exposure = BasketballBallExposureEvaluator.Evaluate(
                    owner.Controller.State,
                    ownerObservation,
                    defenderObservation);
                Vector3 toBall = ownerObservation.Position -
                                 (Vector3.SqrMagnitude(
                                      defenderObservation.LeftHandPosition - ownerObservation.Position) <=
                                  Vector3.SqrMagnitude(
                                      defenderObservation.RightHandPosition - ownerObservation.Position)
                                     ? defenderObservation.LeftHandPosition
                                     : defenderObservation.RightHandPosition);
                float approach = toBall.sqrMagnitude > 1e-8f
                    ? Mathf.Clamp01(Vector3.Dot(
                        defenderObservation.ClosestHandVelocity - ownerObservation.Velocity,
                        toBall.normalized) / 4f)
                    : 1f;
                float proximity = 1f - handDistance / stealHandDistance;
                // Exposure opens the window, but a real hand-ball touch must be
                // able to knock the ball loose even when the carrier is holding it.
                // Holding changes Loose to Contested below; it does not erase touch.
                if (exposure < minimumStealExposure || approach < minimumStealApproach)
                {
                    continue;
                }
                float quality = Mathf.Clamp01(
                    0.35f * exposure + 0.35f * proximity + 0.3f * approach);
                LastBallExposureScore = Mathf.Max(LastBallExposureScore, exposure);
                LastStealContactQuality = Mathf.Max(LastStealContactQuality, quality);
                if (quality >= stealQualityThreshold)
                {
                    KnockBallLoose(
                        defender,
                        defenderObservation,
                        ownerObservation,
                        quality,
                        handDistance);
                    return;
                }
            }
        }

        private void KnockBallLoose(
            BasketballTeamMember defenderMember,
            in BasketballBallObservation defender,
            in BasketballBallObservation controlledBall,
            float contactQuality,
            float handDistance)
        {
            BasketballTeamMember dispossessedOwner = owner;
            Vector3 realPosition = ball.transform.position;
            Quaternion realRotation = ball.transform.rotation;
            Vector3 realVelocity = ball.Velocity;
            Vector3 away = realPosition - defender.RootPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-8f ? away.normalized : defender.RootForward;
            bool cleanTouch = contactQuality >= cleanStealQuality &&
                              handDistance <= cleanStealHandDistance;

            // A clean hand-ball contact changes authority at the ball's current
            // world pose. The defender has already been running the original
            // Hold response against this same ball, so no CatchBlend suction is
            // necessary and no Transform is moved toward the defender.
            if (cleanTouch)
            {
                CompleteContactSteal(
                    defenderMember,
                    dispossessedOwner,
                    realPosition,
                    realRotation,
                    realVelocity,
                    contactQuality);
                return;
            }

            Vector3 velocity = realVelocity +
                               0.3f * defender.ClosestHandVelocity +
                               1.35f * away +
                               0.25f * Vector3.up;
            velocity = Vector3.ClampMagnitude(velocity, 6.5f);
            bool contested = controlledBall.HandContact > 0.35f;

            SetOwner(null, reacquire: false);
            stealTouchCandidate = defenderMember;
            stealTouchCandidateQuality = contactQuality;
            stealTouchStartedAt = Time.time;
            stealTouchExpiresAt = Time.time + stealSecureWindow;
            intendedReceiver = null;
            passPlan = default;
            BallState = contested
                ? BasketballPossessionState.Contested
                : BasketballPossessionState.Loose;
            flightOriginState = BasketballPossessionState.Loose;
            BallControlMode = contested
                ? BasketballBallControlMode.PhysicsContested
                : BasketballBallControlMode.PhysicsFlight;
            lastStealTouchTime = Time.time;
            flightStartedAt = Time.time;
            contestStartedAt = contested ? Time.time : float.NegativeInfinity;
            BeginPhysicsTracking(realPosition);
            ball.ReleaseFromPose(
                realPosition,
                realRotation,
                velocity,
                Vector3.zero);
            worldEventStream?.Publish(
                BasketballWorldEventType.StealTouch,
                PossessionVersion,
                defenderMember,
                dispossessedOwner,
                realPosition,
                velocity,
                contactQuality);
            worldEventStream?.Publish(
                contested
                    ? BasketballWorldEventType.BallContested
                    : BasketballWorldEventType.BallLoose,
                PossessionVersion,
                defenderMember,
                dispossessedOwner,
                realPosition,
                velocity,
                contactQuality);
        }

        private void CompleteContactSteal(
            BasketballTeamMember defender,
            BasketballTeamMember dispossessedOwner,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            float contactQuality)
        {
            SetOwner(defender, reacquire: false);
            passer = null;
            intendedReceiver = null;
            passPlan = default;
            BallState = BasketballPossessionState.Possessed;
            BallControlMode = BasketballBallControlMode.NeuralPossession;
            flightOriginState = BasketballPossessionState.Possessed;
            contestStartedAt = float.NegativeInfinity;
            hasPreviousPhysicsBallPosition = false;
            lastStealTouchTime = Time.time;
            ball.SetState(BasketballBallAuthorityState.Controlled);
            ball.SetControlledPose(position, rotation, velocity);
            ball.BeginControlledHandoff(defender.Controller.CurrentIntent.Hold);
            worldEventStream?.Publish(
                BasketballWorldEventType.StealTouch,
                PossessionVersion,
                defender,
                dispossessedOwner,
                position,
                velocity,
                contactQuality);
            worldEventStream?.Publish(
                BasketballWorldEventType.BallSecured,
                PossessionVersion,
                defender,
                dispossessedOwner,
                position,
                velocity,
                contactQuality);
        }

        private void CompleteCatch(BasketballTeamMember receiver)
        {
            BasketballTeamMember completedPasser = passer;
            BasketballTeamMember expectedReceiver = intendedReceiver;
            BasketballTeamMember oldOwner = previousOwner;
            BasketballPassPlan completedPassPlan = passPlan;
            bool securedStealTouch = receiver == stealTouchCandidate;
            bool useCatchBlend = BallState == BasketballPossessionState.PassFlight ||
                                 BallState == BasketballPossessionState.ShotFlight ||
                                 BallState == BasketballPossessionState.Contested &&
                                 flightOriginState != BasketballPossessionState.Loose;
            Vector3 securedPosition = ball.transform.position;
            Quaternion securedRotation = ball.transform.rotation;
            Vector3 securedVelocity = ball.Velocity;
            SetOwner(receiver, reacquire: useCatchBlend);
            passer = null;
            if (useCatchBlend)
            {
                BallState = BasketballPossessionState.CatchBlend;
                BallControlMode = BasketballBallControlMode.CatchBlend;
                catchBlendStartedAt = Time.time;
            }
            else
            {
                BallState = BasketballPossessionState.Possessed;
                BallControlMode = BasketballBallControlMode.NeuralPossession;
                ball.SetState(BasketballBallAuthorityState.Controlled);
                ball.SetControlledPose(
                    securedPosition,
                    securedRotation,
                    securedVelocity);
                ball.BeginControlledHandoff(receiver.Controller.CurrentIntent.Hold);
            }
            intendedReceiver = null;
            passPlan = default;
            contestStartedAt = float.NegativeInfinity;
            hasPreviousPhysicsBallPosition = false;
            BasketballWorldEventType resultType;
            BasketballTeamMember resultTarget;
            if (flightOriginState == BasketballPossessionState.PassFlight &&
                completedPasser != null)
            {
                bool intendedCatch = receiver == expectedReceiver;
                resultType = intendedCatch
                    ? BasketballWorldEventType.PassCaught
                    : BasketballWorldEventType.PassIntercepted;
                resultTarget = intendedCatch ? completedPasser : expectedReceiver;
            }
            else
            {
                resultType = BasketballWorldEventType.BallSecured;
                resultTarget = securedStealTouch ? oldOwner : null;
            }
            worldEventStream?.Publish(
                resultType,
                PossessionVersion,
                receiver,
                resultTarget,
                ball.transform.position,
                ball.Velocity,
                value: Time.time - flightStartedAt,
                targetPosition: completedPassPlan.PredictedCatchPoint,
                skillVariant: (int)completedPassPlan.PassType);
            flightOriginState = BasketballPossessionState.Possessed;
        }

        private void ResolveCatchBlend()
        {
            if (owner == null)
            {
                MarkLoose();
                return;
            }
            if (Time.time - catchBlendStartedAt < catchBlendSeconds)
            {
                return;
            }

            BallState = BasketballPossessionState.Possessed;
            BallControlMode = BasketballBallControlMode.NeuralPossession;
            ball.CompleteReacquire(owner.Controller.CurrentIntent.Hold);
        }

        private void MarkLoose()
        {
            BasketballPossessionState originState = flightOriginState;
            BasketballTeamMember failedPasser = passer;
            BasketballTeamMember failedReceiver = intendedReceiver;
            BasketballPassPlan failedPassPlan = passPlan;
            BasketballTeamMember shooter = lastShotPlan.Shooter;
            if (owner != null)
            {
                SetOwner(null, reacquire: false);
            }
            intendedReceiver = null;
            passer = null;
            passPlan = default;
            contestStartedAt = float.NegativeInfinity;
            ClearStealTouchCandidate();
            BallState = BasketballPossessionState.Loose;
            BallControlMode = BasketballBallControlMode.PhysicsFlight;
            flightOriginState = BasketballPossessionState.Loose;
            if (originState == BasketballPossessionState.PassFlight)
            {
                worldEventStream?.Publish(
                    BasketballWorldEventType.PassFailed,
                    PossessionVersion,
                    failedPasser,
                    failedReceiver,
                    ball.transform.position,
                    ball.Velocity,
                    value: Time.time - flightStartedAt,
                    targetPosition: failedPassPlan.PredictedCatchPoint,
                    skillVariant: (int)failedPassPlan.PassType);
            }
            else if (originState == BasketballPossessionState.ShotFlight &&
                     !currentShotScored)
            {
                worldEventStream?.Publish(
                    BasketballWorldEventType.ShotMissed,
                    PossessionVersion,
                    shooter,
                    position: ball.transform.position,
                    velocity: ball.Velocity,
                    value: Time.time - flightStartedAt,
                    targetPosition: lastShotPlan.TargetPoint);
            }
            worldEventStream?.Publish(
                BasketballWorldEventType.BallLoose,
                PossessionVersion,
                failedPasser,
                failedReceiver,
                ball.transform.position,
                ball.Velocity);
        }

        private void UpdatePossessedBallMode()
        {
            if (owner == null)
            {
                return;
            }
            BasketballBallAuthorityState desired = owner.Controller.CurrentIntent.Hold
                ? BasketballBallAuthorityState.Held
                : BasketballBallAuthorityState.Controlled;
            if (ball.State != desired)
            {
                ball.SetState(desired);
            }
        }

        private void SetOwner(BasketballTeamMember value, bool reacquire)
        {
            if (owner == value && initialized)
            {
                return;
            }
            previousOwner = owner;
            owner = value;
            shotIntentStartedAt = float.NegativeInfinity;
            ClearStealTouchCandidate();
            PossessionVersion++;
            lastPossessionChangeTime = Time.time;
            for (int index = 0; index < players.Length; index++)
            {
                players[index].Controller.SetCarrier(players[index] == owner, reacquire: false);
            }
            if (owner != null && reacquire)
            {
                ball.BeginReacquire();
            }
            worldEventStream?.Publish(
                BasketballWorldEventType.PossessionChanged,
                PossessionVersion,
                owner,
                previousOwner,
                ball != null ? ball.transform.position : Vector3.zero,
                ball != null ? ball.Velocity : Vector3.zero);
        }

        private void ClearStealTouchCandidate()
        {
            stealTouchCandidate = null;
            stealTouchCandidateQuality = 0f;
            stealTouchStartedAt = float.NegativeInfinity;
            stealTouchExpiresAt = float.NegativeInfinity;
        }

        private void BeginPhysicsTracking(Vector3 position)
        {
            previousPhysicsBallPosition = position;
            hasPreviousPhysicsBallPosition = true;
        }

        private static float MinimumSweptHandDistance(
            in BasketballBallObservation observation,
            Vector3 ballPosition)
        {
            float tickSeconds = 1f / BasketballAgentState.Framerate;
            Vector3 leftSweep = Vector3.ClampMagnitude(
                observation.LeftHandVelocity * tickSeconds,
                0.45f);
            Vector3 rightSweep = Vector3.ClampMagnitude(
                observation.RightHandVelocity * tickSeconds,
                0.45f);
            float left = DistanceToSegment(
                ballPosition,
                observation.LeftHandPosition - leftSweep,
                observation.LeftHandPosition);
            float right = DistanceToSegment(
                ballPosition,
                observation.RightHandPosition - rightSweep,
                observation.RightHandPosition);
            return Mathf.Min(left, right);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 1e-8f)
            {
                return Vector3.Distance(point, end);
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return Vector3.Distance(point, start + t * segment);
        }

        private void UpdateInteractionTargets()
        {
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt)
                {
                    member?.Controller?.SetRival(null);
                    continue;
                }
                BasketballTeamMember rival = null;
                if (owner != null && member == owner)
                {
                    float bestDistance = float.PositiveInfinity;
                    for (int candidateIndex = 0; candidateIndex < players.Length; candidateIndex++)
                    {
                        BasketballTeamMember candidate = players[candidateIndex];
                        if (candidate == null || !candidate.IsOnCourt ||
                            candidate.TeamId == owner.TeamId ||
                            !candidate.Controller.CurrentIntent.Steal)
                        {
                            continue;
                        }
                        float distance = Vector3.SqrMagnitude(
                            candidate.transform.position - owner.transform.position);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            rival = candidate;
                        }
                    }
                }
                else if (owner != null && member.TeamId != owner.TeamId &&
                         member.Controller.CurrentIntent.Steal)
                {
                    rival = owner;
                }
                member.Controller.SetRival(rival != null ? rival.Controller : null);
            }
        }

        private bool HasFreshObservation(int index)
            => index >= 0 && index < observationTimes.Length &&
               Time.time - observationTimes[index] <= 0.15f;

        private static BasketballBallObservation RefreshLiveContactObservation(
            BasketballTeamMember member,
            in BasketballBallObservation fallback,
            Vector3 observedBallPosition,
            Vector3 observedBallVelocity)
        {
            BasketballAgentState state = member.Controller.State;
            if (state == null)
            {
                return fallback;
            }

            int pivot = BasketballAgentState.Pivot;
            return new BasketballBallObservation(
                fallback.Tick,
                observedBallPosition,
                observedBallVelocity,
                fallback.PreviousVelocity,
                state.ActorRootPosition,
                state.ActorRootRotation * Vector3.forward,
                state.BonePositions[18],
                state.BonePositions[25],
                state.BoneVelocities[18],
                state.BoneVelocities[25],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 2)],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 3)],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 4)],
                fallback.CatchIntent,
                fallback.ShootIntent,
                fallback.StealIntent);
        }

        private int FindPlayerIndex(BasketballNeuralController controller)
        {
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index].Controller == controller)
                {
                    return index;
                }
            }
            return -1;
        }

        private void OnDrawGizmos()
        {
            if (!initialized || passPlan.Receiver == null)
            {
                return;
            }
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(passPlan.PredictedCatchPoint, 0.18f);
            Gizmos.DrawLine(
                passPlan.DesiredReleasePosition,
                passPlan.DesiredReleasePosition + 0.2f * passPlan.DesiredReleaseVelocity);
        }

#if UNITY_EDITOR
        public void UpgradeStealTuningDefaults()
        {
            if (interactionTuningVersion >= 1)
            {
                return;
            }
            stealHandDistance = 0.4f;
            stealInteractionRadius = 1.45f;
            minimumStealExposure = 0.28f;
            minimumStealApproach = 0.12f;
            stealQualityThreshold = 0.63f;
            cleanStealQuality = 0.78f;
            cleanStealHandDistance = 0.22f;
            stealSecureDelay = 0.16f;
            stealSecureWindow = 0.35f;
            possessionCooldown = 0.65f;
            interactionTuningVersion = 1;
        }

        public void Configure(
            BasketballTeamMember[] teamMembers,
            BasketballBallController sharedBall)
        {
            players = teamMembers;
            ball = sharedBall;
        }
#endif
    }
}
