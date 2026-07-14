using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class BasketballPossessionManager : MonoBehaviour, IBasketballPossessionAuthority
    {
        [SerializeField] private BasketballTeamMember[] players;
        [SerializeField] private BasketballBallController ball;
        [SerializeField, Min(0.05f)] private float catchHandDistance = 0.36f;
        [SerializeField, Min(0.1f)] private float catchControlRadius = 1.25f;
        [SerializeField, Min(0.05f)] private float intendedReceiverCatchHandDistance = 0.52f;
        [SerializeField, Min(0.1f)] private float intendedReceiverControlRadius = 1.45f;
        [SerializeField, Min(0.05f)] private float intendedReceiverChestCatchRadius = 0.7f;
        [SerializeField, Min(0.1f)] private float maximumCatchRelativeSpeed = 14f;
        [SerializeField, Min(0.05f)] private float stealHandDistance = 0.48f;
        [SerializeField, Min(0.1f)] private float stealInteractionRadius = 1.7f;
        [SerializeField, Range(0f, 1f)] private float stealQualityThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float cleanStealQuality = 0.62f;
        [SerializeField, Min(0.05f)] private float cleanStealHandDistance = 0.3f;
        [SerializeField, Min(0.01f)] private float stealSecureDelay = 0.05f;
        [SerializeField, Min(0.05f)] private float stealSecureWindow = 0.45f;
        [SerializeField, Min(0.05f)] private float contestedTimeout = 0.8f;
        [SerializeField, Min(0f)] private float possessionCooldown = 0.25f;
        [SerializeField, Min(0.05f)] private float catchBlendSeconds = 0.18f;

        private BasketballBallObservation[] observations;
        private float[] observationTimes;
        private BasketballTeamMember owner;
        private BasketballTeamMember previousOwner;
        private BasketballTeamMember passer;
        private BasketballTeamMember intendedReceiver;
        private BasketballPassPlan passPlan;
        private float bestReleaseScore;
        private float bestReleaseDirectionError;
        private float bestReleaseSpeedScale;
        private float bestReleaseVerticalError;
        private float passStartedAt;
        private float flightStartedAt;
        private float catchBlendStartedAt;
        private float contestStartedAt = float.NegativeInfinity;
        private float lastPossessionChangeTime;
        private float lastStealTouchTime = float.NegativeInfinity;
        private BasketballTeamMember stealTouchCandidate;
        private float stealTouchCandidateQuality;
        private float stealTouchStartedAt = float.NegativeInfinity;
        private float stealTouchExpiresAt = float.NegativeInfinity;
        private bool initialized;

        public BasketballTeamMember Owner => owner;
        public BasketballTeamMember PreviousOwner => previousOwner;
        public BasketballTeamMember Passer => passer;
        public BasketballTeamMember IntendedReceiver => intendedReceiver;
        public BasketballPassPlan CurrentPass => passPlan;
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
            }
        }

        public void Initialize()
        {
            if (initialized || players == null || players.Length == 0 || ball == null)
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
            SetOwner(players[0], reacquire: false);
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

        public bool RequestPass(
            BasketballTeamMember requestPasser,
            BasketballTeamMember receiver,
            BasketballPassType passType = BasketballPassType.Lead)
        {
            if (!initialized || requestPasser == null || receiver == null ||
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
                passType);
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
            BasketballMLPassPhase phase;
            if (elapsed < 0.16f)
            {
                phase = BasketballMLPassPhase.Gather;
            }
            else if (elapsed < 0.3f)
            {
                phase = BasketballMLPassPhase.Align;
            }
            else if (elapsed < 0.56f)
            {
                phase = BasketballMLPassPhase.Push;
            }
            else
            {
                phase = BasketballMLPassPhase.ReleasePending;
            }

            BasketballPassPlanner.RefreshRelease(
                ref passPlan,
                member.Controller.State.BallPositions[BasketballAgentState.Pivot]);

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
                    float push = Mathf.InverseLerp(0.3f, 0.56f, elapsed);
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
            else if (BallState == BasketballPossessionState.Possessed &&
                     observation.ShootIntent &&
                     observation.Position.y > 1.5f &&
                     observation.HandContact < 0.1f &&
                     observation.Velocity.magnitude < observation.PreviousVelocity.magnitude)
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
            if (elapsed < 0.2f)
            {
                return;
            }

            BasketballPassPlanner.RefreshRelease(ref passPlan, observation.Position);
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
            bool releaseReady = elapsed >= 0.52f &&
                                (facingAlignment >= 0.35f || elapsed >= 0.78f);
            if (!releaseReady)
            {
                return;
            }

            // Pass direction comes from the pass plan, not Ball Target. Apply the
            // computed projectile velocity once at release; physics owns all flight.
            BasketballPassPlanner.RefreshRelease(ref passPlan, observation.Position);
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
            BallControlMode = BasketballBallControlMode.PhysicsFlight;
            flightStartedAt = Time.time;
            contestStartedAt = float.NegativeInfinity;
            LastPassFailureReason = null;
            ball.ReleaseFromPose(
                observation.Position,
                ball.transform.rotation,
                releaseVelocity,
                Vector3.zero);
        }

        private void ReleaseShot(in BasketballBallObservation observation)
        {
            BasketballTeamMember releasingPlayer = owner;
            SetOwner(null, reacquire: false);
            passer = releasingPlayer;
            intendedReceiver = null;
            passPlan = default;
            BallState = BasketballPossessionState.ShotFlight;
            BallControlMode = BasketballBallControlMode.PhysicsFlight;
            flightStartedAt = Time.time;
            contestStartedAt = float.NegativeInfinity;
            ball.ReleaseFromPose(
                observation.Position,
                ball.transform.rotation,
                observation.Velocity,
                Vector3.zero);
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
            Vector3 currentBallPosition = ball.transform.position;
            float sweepSeconds = Mathf.Min(
                Mathf.Max(Time.deltaTime, Time.fixedDeltaTime),
                1f / BasketballAgentState.Framerate);
            Vector3 previousBallPosition = currentBallPosition -
                                           ball.Velocity * sweepSeconds;
            for (int index = 0; index < players.Length; index++)
            {
                if (!HasFreshObservation(index))
                {
                    continue;
                }
                BasketballBallObservation observation = RefreshLiveContactObservation(
                    players[index],
                    observations[index],
                    ball.transform.position,
                    ball.Velocity);
                if (!observation.CatchIntent && !observation.StealIntent)
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
                float rootDistance = Vector3.Distance(
                    observation.RootPosition,
                    ball.transform.position);
                float relativeSpeed = (ball.Velocity -
                    observation.ClosestHandVelocity).magnitude;
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
                                          chestDistance <= intendedReceiverChestCatchRadius &&
                                          (Vector3.Distance(
                                               chestPosition,
                                               currentBallPosition) <= 0.24f ||
                                           incomingAlignment >= 0.05f);
                float allowedHandDistance = isIntendedReceiver
                    ? intendedReceiverCatchHandDistance
                    : catchHandDistance;
                float allowedControlRadius = isIntendedReceiver
                    ? intendedReceiverControlRadius
                    : catchControlRadius;
                if ((!intendedBodyCatch && handDistance > allowedHandDistance) ||
                    rootDistance > allowedControlRadius ||
                    relativeSpeed > maximumCatchRelativeSpeed)
                {
                    continue;
                }

                float quality =
                    0.55f * (1f - handDistance / allowedHandDistance) +
                    0.2f * (1f - relativeSpeed / maximumCatchRelativeSpeed) +
                    0.15f * observation.HandContact;
                if (isIntendedReceiver)
                {
                    quality += 0.12f;
                }
                if (intendedBodyCatch)
                {
                    float chestProximity = 1f -
                        chestDistance / intendedReceiverChestCatchRadius;
                    float approachQuality = Mathf.InverseLerp(
                        0.05f,
                        0.85f,
                        incomingAlignment);
                    quality = Mathf.Max(
                        quality,
                        0.38f + 0.35f * chestProximity + 0.12f * approachQuality);
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
                }
                else if (quality > secondQuality)
                {
                    secondQuality = quality;
                }
            }

            bool intendedPassCatch = best == intendedReceiver &&
                                     (BallState == BasketballPossessionState.PassFlight ||
                                      BallState == BasketballPossessionState.Contested);
            float requiredSecureQuality = best == stealTouchCandidate || intendedPassCatch
                ? 0.34f
                : 0.42f;
            if (best != null && bestQuality >= requiredSecureQuality)
            {
                if (stealTouchCandidate != null &&
                    Time.time - stealTouchStartedAt < stealSecureDelay)
                {
                    return;
                }
                if (secondQuality >= bestQuality - 0.06f)
                {
                    if (BallState != BasketballPossessionState.Contested)
                    {
                        contestStartedAt = Time.time;
                    }
                    BallState = BasketballPossessionState.Contested;
                    BallControlMode = BasketballBallControlMode.PhysicsContested;
                    return;
                }
                CompleteCatch(best);
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
            if (owner == null || Time.time - lastStealTouchTime < possessionCooldown)
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
                if (defender == owner || defender.TeamId == owner.TeamId ||
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
                float quality = Mathf.Clamp01(
                    0.25f * exposure + 0.6f * proximity + 0.15f * approach);
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
            Vector3 away = controlledBall.Position - defender.RootPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-8f ? away.normalized : defender.RootForward;
            bool cleanTouch = contactQuality >= cleanStealQuality &&
                              handDistance <= cleanStealHandDistance;
            Vector3 velocity;
            if (cleanTouch)
            {
                Vector3 towardDefender = defender.RootPosition - controlledBall.Position;
                towardDefender.y = 0f;
                towardDefender = towardDefender.sqrMagnitude > 1e-8f
                    ? towardDefender.normalized
                    : -away;
                velocity = Vector3.Lerp(
                               controlledBall.Velocity,
                               defender.ClosestHandVelocity,
                               0.45f) +
                           0.55f * towardDefender +
                           0.12f * Vector3.up;
                velocity = Vector3.ClampMagnitude(velocity, 5.5f);
            }
            else
            {
                velocity = controlledBall.Velocity +
                           0.3f * defender.ClosestHandVelocity +
                           1.35f * away +
                           0.25f * Vector3.up;
                velocity = Vector3.ClampMagnitude(velocity, 6.5f);
            }
            bool contested = cleanTouch || controlledBall.HandContact > 0.35f;

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
            BallControlMode = contested
                ? BasketballBallControlMode.PhysicsContested
                : BasketballBallControlMode.PhysicsFlight;
            lastStealTouchTime = Time.time;
            flightStartedAt = Time.time;
            contestStartedAt = contested ? Time.time : float.NegativeInfinity;
            ball.ReleaseFromPose(
                controlledBall.Position,
                ball.transform.rotation,
                velocity,
                Vector3.zero);
        }

        private void CompleteCatch(BasketballTeamMember receiver)
        {
            SetOwner(receiver, reacquire: true);
            passer = null;
            BallState = BasketballPossessionState.CatchBlend;
            BallControlMode = BasketballBallControlMode.CatchBlend;
            catchBlendStartedAt = Time.time;
            intendedReceiver = null;
            passPlan = default;
            contestStartedAt = float.NegativeInfinity;
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
        }

        private void ClearStealTouchCandidate()
        {
            stealTouchCandidate = null;
            stealTouchCandidateQuality = 0f;
            stealTouchStartedAt = float.NegativeInfinity;
            stealTouchExpiresAt = float.NegativeInfinity;
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
                BasketballTeamMember rival = null;
                if (owner != null && member == owner)
                {
                    float bestDistance = float.PositiveInfinity;
                    for (int candidateIndex = 0; candidateIndex < players.Length; candidateIndex++)
                    {
                        BasketballTeamMember candidate = players[candidateIndex];
                        if (candidate == null || candidate.TeamId == owner.TeamId ||
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
