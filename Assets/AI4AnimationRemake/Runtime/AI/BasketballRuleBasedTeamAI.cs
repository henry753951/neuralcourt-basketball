using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballRuleBasedTeamAI : MonoBehaviour,
        IBasketballDecisionPolicy,
        IBasketballWorldEventObserver
    {
        [SerializeField, Min(0.05f)] private float decisionInterval = 0.1f;
        [SerializeField, Min(1f)] private float preferredShotDistance = 5.25f;
        [SerializeField, Range(0.5f, 0.999f)] private float shotFacingDot = 0.88f;
        [SerializeField, Min(0.05f)] private float maximumShotAlignTime = 0.22f;
        [SerializeField, Min(0.5f)] private float driveStopDistance = 4.2f;
        [SerializeField, Min(0.2f)] private float defenseSpacing = 0.9f;
        [SerializeField, Range(0f, 1f)] private float defensiveAggression = 0.72f;
        [SerializeField, Min(0.2f)] private float stealAttemptDistance = 1.15f;
        [SerializeField, Range(-1f, 1f)] private float minimumStealFacingDot = 0.35f;
        [SerializeField, Min(0.05f)] private float stealAttemptDuration = 0.18f;
        [SerializeField, Min(0.1f)] private float stealAttemptCooldown = 1.15f;
        [SerializeField, Min(0.1f)] private float protectBallPressureDistance = 1.8f;
        [SerializeField, Range(0.1f, 1f)] private float protectBallControlStrength = 0.72f;
        [SerializeField, Min(0.2f)] private float protectedHandSwitchInterval = 0.85f;
        [SerializeField, Min(0.2f)] private float passPressureDistance = 1.45f;
        [SerializeField, Min(0.1f)] private float actionCooldown = 1.1f;
        [SerializeField, Min(0f)] private float minimumPossessionSecondsBeforePass = 1.05f;
        [SerializeField, Min(0f)] private float returnPassLockoutSeconds = 2.25f;
        [SerializeField, Min(0f)] private float minimumOpenPassGain = 2.25f;
        [SerializeField, Min(0f)] private float minimumPressuredPassGain = 0.85f;
        [SerializeField, Min(0f)] private float callForPassScoreBonus = 2.35f;
        [SerializeField, Min(0.2f)] private float defenseSprintDistance = 1.6f;
        [SerializeField, Min(0.2f)] private float offBallSprintDistance = 2.4f;
        [SerializeField, Min(0.2f)] private float playerAvoidanceRadius = 0.9f;
        [SerializeField, Range(0f, 1.5f)] private float playerAvoidanceStrength = 0.7f;
        [SerializeField, Min(0.1f)] private float courtBoundaryMargin = 0.65f;
        [SerializeField, Min(0.5f)] private float helpDefenseDepth = 2.1f;
        [SerializeField, Min(0.5f)] private float cutLaneOffset = 1.7f;
        [SerializeField, Min(0.25f)] private float recoveryPredictionHorizon = 3f;
        [SerializeField, Min(0.05f)] private float rollingBallLeadTime = 0.35f;
        [SerializeField, Min(0.5f)] private float recoveryRunSpeed = 4.5f;
        [SerializeField, Range(4, 32)] private int interceptTrajectorySamples = 12;
        [SerializeField, Min(0f)] private float interceptReactionTime = 0.12f;
        [SerializeField, Min(0f)] private float interceptArrivalSlack = 0.12f;
        [SerializeField, Min(0.1f)] private float interceptReachRadius = 0.7f;
        [SerializeField, Min(0.5f)] private float maximumInterceptHeight = 2.3f;
        [SerializeField] private bool drawDecisionGizmos = true;
        [SerializeField, HideInInspector] private int tuningVersion;

        private BasketballTeamMember[] players;
        private BasketballPossessionManager possession;
        private BasketballBallController ball;
        private BasketballCourt court;
        private float[] nextDecisionAt;
        private float[] nextActionAt;
        private float[] shotAlignStartedAt;
        private float[] shootUntil;
        private BasketballTeamMember[] cachedPassTargets;
        private BasketballTeamRole[] roles;
        private int[] assignedOpponentRosterIndices;
        private Vector3[] roleTargets;
        private float[] stealUntil;
        private float[] nextStealAt;
        private float[] nextProtectedHandSwitchAt;
        private float[] protectedBallSide;
        private BasketballWorldObservation worldObservation;
        private BasketballRulesManager rules;
        private int commandSequence;

        public void ConfigureRuntime(
            BasketballTeamMember[] roster,
            BasketballPossessionManager possessionManager,
            BasketballBallController sharedBall,
            BasketballCourt basketballCourt)
        {
            players = roster;
            possession = possessionManager;
            ball = sharedBall;
            court = basketballCourt;
            rules = GetComponent<BasketballRulesManager>();
            int count = players != null ? players.Length : 0;
            if (nextDecisionAt == null || nextDecisionAt.Length != count || roles == null)
            {
                nextDecisionAt = new float[count];
                nextActionAt = new float[count];
                shotAlignStartedAt = new float[count];
                shootUntil = new float[count];
                cachedPassTargets = new BasketballTeamMember[count];
                roles = new BasketballTeamRole[count];
                assignedOpponentRosterIndices = new int[count];
                roleTargets = new Vector3[count];
                stealUntil = new float[count];
                nextStealAt = new float[count];
                nextProtectedHandSwitchAt = new float[count];
                protectedBallSide = new float[count];
                for (int index = 0; index < count; index++)
                {
                    shotAlignStartedAt[index] = float.NegativeInfinity;
                    assignedOpponentRosterIndices[index] = -1;
                    protectedBallSide[index] = index % 2 == 0 ? -1f : 1f;
                }
            }
        }

        public void BeginDecisionFrame(BasketballWorldObservation observation)
        {
            worldObservation = observation;
            BuildTeamPlan();
        }

        public BasketballPlayerCommand Decide(in BasketballPlayerObservation player)
        {
            BasketballTeamMember member = players != null && player.RosterIndex >= 0 &&
                                          player.RosterIndex < players.Length
                ? players[player.RosterIndex]
                : null;
            if (member == null || member.PlayerIndex != player.PlayerIndex)
            {
                return BasketballPlayerCommand.IntentOnly(
                    player.PlayerIndex,
                    worldObservation != null ? worldObservation.PossessionVersion : -1,
                    default);
            }
            return BuildCommand(member);
        }

        public void ReportCommandResult(
            in BasketballPlayerCommand command,
            BasketballSkillCommandResult result)
        {
            if (command.Skill != BasketballSkillCommandType.RequestPass || players == null)
            {
                return;
            }
            int rosterIndex = FindRosterIndex(command.ActorPlayerIndex);
            if (rosterIndex < 0)
            {
                return;
            }
            cachedPassTargets[rosterIndex] = null;
            stealUntil[rosterIndex] = 0f;
            nextDecisionAt[rosterIndex] = Time.time + decisionInterval;
            nextActionAt[rosterIndex] = Time.time +
                                        (result == BasketballSkillCommandResult.Accepted
                                            ? actionCooldown
                                            : decisionInterval);
        }

        public void ObserveEvent(in BasketballWorldEvent worldEvent)
        {
            switch (worldEvent.Type)
            {
                case BasketballWorldEventType.MatchConfigurationChanged:
                case BasketballWorldEventType.MatchRestarted:
                case BasketballWorldEventType.PossessionChanged:
                case BasketballWorldEventType.BallContested:
                case BasketballWorldEventType.BallLoose:
                case BasketballWorldEventType.BallSecured:
                    InvalidateAllTacticalCaches();
                    break;

                case BasketballWorldEventType.PassPreparationFailed:
                case BasketballWorldEventType.PassFailed:
                case BasketballWorldEventType.PassCaught:
                case BasketballWorldEventType.PassIntercepted:
                    InvalidatePlayerTacticalCache(worldEvent.ActorPlayerIndex);
                    InvalidatePlayerTacticalCache(worldEvent.TargetPlayerIndex);
                    break;

                case BasketballWorldEventType.ShotMade:
                case BasketballWorldEventType.ShotMissed:
                    InvalidatePlayerTacticalCache(worldEvent.ActorPlayerIndex);
                    break;
            }
        }

        private void InvalidateAllTacticalCaches()
        {
            if (players == null || nextDecisionAt == null)
            {
                return;
            }
            for (int index = 0; index < players.Length; index++)
            {
                InvalidateRosterIndex(index);
            }
        }

        private void InvalidatePlayerTacticalCache(int playerIndex)
        {
            int rosterIndex = FindRosterIndex(playerIndex);
            if (rosterIndex >= 0)
            {
                InvalidateRosterIndex(rosterIndex);
            }
        }

        private void InvalidateRosterIndex(int rosterIndex)
        {
            if (rosterIndex < 0 || nextDecisionAt == null ||
                rosterIndex >= nextDecisionAt.Length)
            {
                return;
            }
            nextDecisionAt[rosterIndex] = 0f;
            nextActionAt[rosterIndex] = 0f;
            shotAlignStartedAt[rosterIndex] = float.NegativeInfinity;
            shootUntil[rosterIndex] = 0f;
            cachedPassTargets[rosterIndex] = null;
        }

        private BasketballPlayerCommand BuildCommand(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            if (member == null || !member.IsOnCourt || member.Controller?.State == null ||
                players == null || possession == null || ball == null || court == null)
            {
                return BasketballPlayerCommand.IntentOnly(
                    member != null ? member.PlayerIndex : -1,
                    possession != null ? possession.PossessionVersion : -1,
                    intent);
            }

            if (possession.IsIntendedReceiver(member))
            {
                intent = BuildReceiverIntent(member);
            }
            else
            {
                BasketballTeamMember owner = possession.Owner;
                if (owner == member)
                {
                    return BuildBallHandlerCommand(member);
                }
                if (owner == null)
                {
                    int rosterIndex = FindRosterIndex(member.PlayerIndex);
                    BasketballTeamRole role = rosterIndex >= 0
                        ? roles[rosterIndex]
                        : BasketballTeamRole.Transition;
                    intent = role == BasketballTeamRole.PassInterceptor
                        ? BuildInterceptorIntent(member, rosterIndex)
                        : role == BasketballTeamRole.LooseBallChaser ||
                             role == BasketballTeamRole.Rebounder
                        ? BuildFreeBallIntent(member, rosterIndex)
                        : role == BasketballTeamRole.Spacer ||
                             role == BasketballTeamRole.Cutter ||
                             role == BasketballTeamRole.TransitionSafety
                            ? BuildOffBallIntent(member)
                        : role == BasketballTeamRole.BoxOutDefender
                            ? BuildBoxOutIntent(member, rosterIndex)
                            : BuildTransitionIntent(member);
                }
                else
                {
                    intent = owner.TeamId == member.TeamId
                        ? BuildOffBallIntent(member)
                        : BuildDefenseIntent(member, owner);
                }
            }
            return BasketballPlayerCommand.IntentOnly(
                member.PlayerIndex,
                possession.PossessionVersion,
                intent);
        }

        private BasketballPlayerCommand BuildBallHandlerCommand(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            int index = FindRosterIndex(member.PlayerIndex);
            if (index < 0)
            {
                return BasketballPlayerCommand.IntentOnly(
                    member.PlayerIndex,
                    possession.PossessionVersion,
                    intent);
            }
            if (possession.BallState == BasketballPossessionState.PassPreparing &&
                possession.TryGetPassControl(member, out BasketballPassControlProfile profile))
            {
                intent.PassTargeting = true;
                intent.CommitBallRelease = true;
                intent.PassControl = true;
                intent.PassHoldStyle = profile.HoldStyle;
                intent.PassShootStyle = profile.ShootStyle;
                intent.UseWorldFacing = true;
                intent.WorldFacing = profile.Facing;
                return BasketballPlayerCommand.IntentOnly(
                    member.PlayerIndex,
                    possession.PossessionVersion,
                    intent);
            }

            BasketballHoop hoop = court.GetAttackHoop(member.TeamId);
            if (hoop == null || possession.BallState != BasketballPossessionState.Possessed)
            {
                return BasketballPlayerCommand.IntentOnly(
                    member.PlayerIndex,
                    possession.PossessionVersion,
                    intent);
            }

            BasketballAgentState state = member.Controller.State;
            Vector3 root = state.ActorRootPosition;
            Vector3 toHoop = Vector3.ProjectOnPlane(hoop.AimPoint - root, court.CourtUp);
            float hoopDistance = toHoop.magnitude;
            Vector3 facing = state.ActorRootRotation * Vector3.forward;
            float facingDot = toHoop.sqrMagnitude > 1e-8f
                ? Vector3.Dot(
                    Vector3.ProjectOnPlane(facing, court.CourtUp).normalized,
                    toHoop.normalized)
                : 1f;
            float pressure = NearestOpponentDistance(member, root);
            ApplyProtectiveDribble(member, index, pressure, ref intent);

            if (Time.time < shootUntil[index])
            {
                intent.UseWorldFacing = true;
                intent.WorldFacing = toHoop;
                intent.Shoot = true;
                return BasketballPlayerCommand.IntentOnly(
                    member.PlayerIndex,
                    possession.PossessionVersion,
                    intent);
            }

            bool shotClockUrgent = rules != null && rules.IsShotClockUrgent();
            bool canPassByCadence = shotClockUrgent ||
                                    Time.time - possession.LastPossessionChangeTime >=
                                    minimumPossessionSecondsBeforePass;
            if (Time.time >= nextDecisionAt[index])
            {
                nextDecisionAt[index] = Time.time + decisionInterval;
                BasketballTeamMember receiver = FindBestPassTarget(member, hoop, out float gain);
                bool underPressure = pressure <= passPressureDistance;
                bool shouldPass = canPassByCadence && receiver != null &&
                                  hoopDistance > Mathf.Min(
                                      preferredShotDistance,
                                      member.MaximumEffectiveShotDistance) &&
                                  gain >= (underPressure
                                      ? minimumPressuredPassGain
                                      : minimumOpenPassGain);
                if (shotClockUrgent && hoopDistance > member.MaximumEffectiveShotDistance)
                {
                    shouldPass = receiver != null && gain >= 0f;
                }
                if (shouldPass)
                {
                    BasketballPassPlan candidatePlan = BasketballPassPlanner.Create(
                        member,
                        receiver,
                        state.BallPositions[BasketballAgentState.Pivot],
                        possession.PossessionVersion,
                        SelectPassType(member, receiver),
                        BasketballRuntimeSettings.LoadDefault());
                    shouldPass = candidatePlan.IsSupported;
                }
                cachedPassTargets[index] = shouldPass ? receiver : null;
            }

            BasketballTeamMember target = cachedPassTargets[index];
            if (target != null && Time.time >= nextActionAt[index])
            {
                return BasketballPlayerCommand.RequestPass(
                    ++commandSequence,
                    member.PlayerIndex,
                    target.PlayerIndex,
                    possession.PossessionVersion,
                    SelectPassType(member, target),
                    Time.time + Mathf.Max(0.1f, decisionInterval));
            }

            float effectiveShotDistance = shotClockUrgent
                ? member.MaximumEffectiveShotDistance
                : Mathf.Min(preferredShotDistance, member.MaximumEffectiveShotDistance);
            if (hoopDistance <= effectiveShotDistance)
            {
                if (float.IsNegativeInfinity(shotAlignStartedAt[index]))
                {
                    shotAlignStartedAt[index] = Time.time;
                }
                intent.UseWorldFacing = true;
                intent.WorldFacing = toHoop;
                intent.Move = Vector2.zero;
                bool aligned = facingDot >= shotFacingDot ||
                               Time.time - shotAlignStartedAt[index] >=
                               maximumShotAlignTime;
                if (aligned && Time.time >= nextActionAt[index])
                {
                    BasketballRuntimeSettings settings =
                        BasketballRuntimeSettings.LoadDefault();
                    float releaseWindow = settings != null
                        ? settings.ForcedShotReleaseSeconds + 0.2f
                        : 0.78f;
                    shootUntil[index] = Time.time + Mathf.Max(0.75f, releaseWindow);
                    nextActionAt[index] = Time.time + actionCooldown;
                    intent.Shoot = true;
                }
                return BasketballPlayerCommand.IntentOnly(
                    member.PlayerIndex,
                    possession.PossessionVersion,
                    intent);
            }

            shotAlignStartedAt[index] = float.NegativeInfinity;
            Vector3 driveTarget = hoop.AimPoint -
                                  toHoop.normalized * driveStopDistance;
            ApplyMove(
                ref intent,
                member,
                driveTarget,
                hoop.AimPoint,
                sprint: hoopDistance > 8f,
                avoidPlayers: true);
            return BasketballPlayerCommand.IntentOnly(
                member.PlayerIndex,
                possession.PossessionVersion,
                intent);
        }

        private BasketballIntent BuildReceiverIntent(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            BasketballPossessionState state = possession.BallState;
            bool contested = state == BasketballPossessionState.Contested;
            bool inFlight = state == BasketballPossessionState.PassFlight;
            Vector3 catchPoint = contested
                ? ball.transform.position
                : possession.CurrentPass.PredictedCatchPoint;
            BasketballAgentState agent = member.Controller.State;
            Vector3 planar = Vector3.ProjectOnPlane(
                catchPoint - agent.ActorRootPosition,
                court.CourtUp);
            float timeToArrival = inFlight
                ? Mathf.Max(0f, possession.ExpectedArrivalTime - Time.time)
                : contested ? 0f : possession.CurrentPass.ExpectedFlightTime;
            float ballDistance = Vector3.Distance(
                ball.transform.position,
                agent.ActorRootPosition);
            intent.CatchReady = true;
            intent.Hold = (inFlight || contested) &&
                          (contested || ballDistance <= 3f || timeToArrival <= 0.65f);
            intent.UseWorldMove = planar.magnitude > 0.22f;
            intent.WorldMove = Vector3.ClampMagnitude(planar, 1f);
            intent.Sprint = planar.magnitude > 1.65f ||
                            (inFlight && timeToArrival < 0.75f && planar.magnitude > 0.8f);
            intent.UseWorldFacing = true;
            intent.WorldFacing = (inFlight || contested)
                ? ball.transform.position - agent.ActorRootPosition
                : possession.CurrentPass.DesiredReleasePosition - agent.ActorRootPosition;
            return intent;
        }

        private BasketballIntent BuildFreeBallIntent(
            BasketballTeamMember member,
            int rosterIndex)
        {
            BasketballIntent intent = default;
            BasketballAgentState state = member.Controller.State;
            Vector3 target = rosterIndex >= 0 && rosterIndex < roleTargets.Length
                ? roleTargets[rosterIndex]
                : ClampTargetToCourt(ball.transform.position);
            float targetDistance = Vector3.Distance(state.ActorRootPosition, target);
            float ballDistance = Vector3.Distance(
                state.ActorRootPosition,
                ball.transform.position);
            float handDistance = Mathf.Min(
                Vector3.Distance(state.BonePositions[18], ball.transform.position),
                Vector3.Distance(state.BonePositions[25], ball.transform.position));
            ApplyMove(
                ref intent,
                member,
                target,
                ball.transform.position,
                sprint: targetDistance > 1.4f,
                avoidPlayers: false);
            intent.CatchReady = true;
            intent.Hold = ballDistance <= 1.15f && handDistance <= 0.75f;
            intent.Steal = possession.BallState == BasketballPossessionState.PassFlight ||
                           possession.BallState == BasketballPossessionState.Contested;
            return intent;
        }

        private BasketballIntent BuildInterceptorIntent(
            BasketballTeamMember member,
            int rosterIndex)
        {
            BasketballIntent intent = default;
            BasketballAgentState state = member.Controller.State;
            Vector3 target = rosterIndex >= 0 && rosterIndex < roleTargets.Length
                ? roleTargets[rosterIndex]
                : ball.transform.position;
            float distance = Vector3.Distance(state.ActorRootPosition, target);
            ApplyMove(
                ref intent,
                member,
                target,
                ball.transform.position,
                sprint: distance > 2.2f,
                avoidPlayers: false);
            intent.CatchReady = true;
            intent.Steal = true;
            return intent;
        }

        private BasketballIntent BuildOffBallIntent(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            int rosterIndex = FindRosterIndex(member.PlayerIndex);
            BasketballTeamRole role = rosterIndex >= 0
                ? roles[rosterIndex]
                : BasketballTeamRole.Spacer;
            Vector3 target = rosterIndex >= 0 &&
                             roleTargets[rosterIndex] != Vector3.zero
                ? roleTargets[rosterIndex]
                : GetSpacingTarget(member);
            if (rules != null && rules.ShouldExitPaint(member))
            {
                target = rules.GetPaintExitTarget(member);
            }
            BasketballHoop hoop = court.GetAttackHoop(member.TeamId);
            bool cut = role == BasketballTeamRole.Cutter;
            ApplyMove(
                ref intent,
                member,
                target,
                cut || role == BasketballTeamRole.TransitionSafety
                    ? ball.transform.position
                    : hoop != null ? hoop.AimPoint : target,
                sprint: cut || Vector3.Distance(
                    member.Controller.State.ActorRootPosition,
                    target) > offBallSprintDistance,
                avoidPlayers: true);
            intent.CatchReady = true;
            return intent;
        }

        private BasketballIntent BuildDefenseIntent(
            BasketballTeamMember member,
            BasketballTeamMember owner)
        {
            BasketballIntent intent = default;
            int rosterIndex = FindRosterIndex(member.PlayerIndex);
            int assignmentIndex = rosterIndex >= 0
                ? assignedOpponentRosterIndices[rosterIndex]
                : -1;
            BasketballTeamMember assignment = assignmentIndex >= 0 &&
                                              assignmentIndex < players.Length
                ? players[assignmentIndex]
                : null;
            if (assignment == null)
            {
                assignment = owner;
            }
            BasketballAgentState defender = member.Controller.State;
            Vector3 opponentPosition = assignment.Controller.State.ActorRootPosition;
            BasketballHoop defendedHoop = court.GetAttackHoop(owner.TeamId);
            Vector3 towardHoop = defendedHoop != null
                ? Vector3.ProjectOnPlane(
                    defendedHoop.Center - opponentPosition,
                    court.CourtUp)
                : Vector3.zero;
            float effectiveDefenseSpacing = Mathf.Lerp(
                defenseSpacing + 0.22f,
                Mathf.Max(0.45f, defenseSpacing * 0.72f),
                defensiveAggression);
            Vector3 target = opponentPosition +
                             (towardHoop.sqrMagnitude > 1e-8f
                                 ? towardHoop.normalized * effectiveDefenseSpacing
                                 : Vector3.zero);
            BasketballTeamRole role = rosterIndex >= 0
                ? roles[rosterIndex]
                : BasketballTeamRole.OffBallDefender;
            if (role == BasketballTeamRole.HelpDefender && rosterIndex >= 0 &&
                roleTargets[rosterIndex] != Vector3.zero)
            {
                target = roleTargets[rosterIndex];
                opponentPosition = owner.Controller.State.ActorRootPosition;
            }
            ApplyMove(
                ref intent,
                member,
                target,
                opponentPosition,
                sprint: Vector3.Distance(
                    defender.ActorRootPosition,
                    target) > Mathf.Lerp(
                        defenseSprintDistance * 1.15f,
                        defenseSprintDistance * 0.65f,
                        defensiveAggression),
                avoidPlayers: true);
            if (role == BasketballTeamRole.OnBallDefender && ShouldAttemptSteal(
                    member,
                    owner,
                    rosterIndex))
            {
                intent.Steal = true;
            }
            return intent;
        }

        private void ApplyProtectiveDribble(
            BasketballTeamMember owner,
            int rosterIndex,
            float pressure,
            ref BasketballIntent intent)
        {
            if (rosterIndex < 0 || pressure > protectBallPressureDistance ||
                owner?.Controller?.State == null)
            {
                return;
            }

            BasketballTeamMember defender = FindNearestOpponent(
                owner,
                owner.Controller.State.ActorRootPosition);
            if (defender?.Controller?.State == null)
            {
                return;
            }

            BasketballAgentState state = owner.Controller.State;
            Vector3 toDefender = Vector3.ProjectOnPlane(
                defender.Controller.State.ActorRootPosition - state.ActorRootPosition,
                court.CourtUp);
            Vector3 localDefender = Quaternion.Inverse(state.ActorRootRotation) * toDefender;
            float awaySide = Mathf.Abs(localDefender.x) > 0.18f
                ? -Mathf.Sign(localDefender.x)
                : protectedBallSide[rosterIndex];
            bool defenderReachedProtectedSide = Mathf.Sign(localDefender.x) ==
                                                protectedBallSide[rosterIndex];
            if (Time.time >= nextProtectedHandSwitchAt[rosterIndex] ||
                defenderReachedProtectedSide)
            {
                protectedBallSide[rosterIndex] = awaySide == 0f
                    ? -protectedBallSide[rosterIndex]
                    : awaySide;
                nextProtectedHandSwitchAt[rosterIndex] = Time.time +
                    protectedHandSwitchInterval;
            }

            float pressureWeight = 1f - Mathf.Clamp01(
                pressure / Mathf.Max(0.1f, protectBallPressureDistance));
            float strength = Mathf.Lerp(
                protectBallControlStrength * 0.65f,
                protectBallControlStrength,
                pressureWeight);
            intent.BallControl = new Vector2(
                protectedBallSide[rosterIndex] * strength,
                -0.12f * pressureWeight);
        }

        private bool ShouldAttemptSteal(
            BasketballTeamMember defender,
            BasketballTeamMember owner,
            int rosterIndex)
        {
            if (rosterIndex < 0 || stealUntil == null || nextStealAt == null ||
                defender?.Controller?.State == null || owner?.Controller?.State == null)
            {
                return false;
            }
            if (Time.time < stealUntil[rosterIndex])
            {
                return true;
            }
            if (Time.time < nextStealAt[rosterIndex])
            {
                return false;
            }

            BasketballAgentState defenderState = defender.Controller.State;
            BasketballAgentState ownerState = owner.Controller.State;
            Vector3 ballPosition = ball.transform.position;
            Vector3 toBall = Vector3.ProjectOnPlane(
                ballPosition - defenderState.ActorRootPosition,
                court.CourtUp);
            float effectiveAttemptDistance = stealAttemptDistance *
                                             Mathf.Lerp(
                                                 0.92f,
                                                 1.18f,
                                                 defensiveAggression);
            if (toBall.magnitude > effectiveAttemptDistance)
            {
                return false;
            }
            Vector3 forward = Vector3.ProjectOnPlane(
                defenderState.ActorRootRotation * Vector3.forward,
                court.CourtUp);
            float facingDot = toBall.sqrMagnitude > 1e-8f && forward.sqrMagnitude > 1e-8f
                ? Vector3.Dot(forward.normalized, toBall.normalized)
                : 1f;
            float effectiveFacingDot = Mathf.Clamp(
                minimumStealFacingDot + Mathf.Lerp(
                    0.16f,
                    -0.16f,
                    defensiveAggression),
                -1f,
                1f);
            if (facingDot < effectiveFacingDot)
            {
                return false;
            }

            int pivot = BasketballAgentState.Pivot;
            Vector3 localBall = Quaternion.Inverse(ownerState.ActorRootRotation) *
                                (ballPosition - ownerState.ActorRootPosition);
            float handContact = Mathf.Max(
                ownerState.Contacts[BasketballAgentState.ContactIndex(pivot, 2)],
                ownerState.Contacts[BasketballAgentState.ContactIndex(pivot, 3)]);
            bool exposed = Mathf.Abs(localBall.x) >= Mathf.Lerp(
                               0.4f,
                               0.26f,
                               defensiveAggression) ||
                           localBall.y <= Mathf.Lerp(
                               0.78f,
                               1.02f,
                               defensiveAggression) ||
                           handContact <= Mathf.Lerp(
                               0.32f,
                               0.5f,
                               defensiveAggression);
            if (!exposed)
            {
                return false;
            }

            stealUntil[rosterIndex] = Time.time + stealAttemptDuration *
                Mathf.Lerp(0.85f, 1.45f, defensiveAggression);
            nextStealAt[rosterIndex] = Time.time + stealAttemptCooldown *
                Mathf.Lerp(1.25f, 0.58f, defensiveAggression);
            return true;
        }

        private BasketballTeamMember FindNearestOpponent(
            BasketballTeamMember member,
            Vector3 position)
        {
            BasketballTeamMember best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId == member.TeamId || candidate.Controller?.State == null)
                {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(
                    candidate.Controller.State.ActorRootPosition - position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private BasketballIntent BuildBoxOutIntent(
            BasketballTeamMember member,
            int rosterIndex)
        {
            BasketballIntent intent = default;
            Vector3 target = rosterIndex >= 0 && rosterIndex < roleTargets.Length &&
                             roleTargets[rosterIndex] != Vector3.zero
                ? roleTargets[rosterIndex]
                : member.Controller.State.ActorRootPosition;
            ApplyMove(
                ref intent,
                member,
                target,
                ball.transform.position,
                sprint: false,
                avoidPlayers: true);
            intent.CatchReady = true;
            return intent;
        }

        public BasketballTeamRole GetRole(int playerIndex)
        {
            int rosterIndex = FindRosterIndex(playerIndex);
            return rosterIndex >= 0 && roles != null
                ? roles[rosterIndex]
                : BasketballTeamRole.Inactive;
        }

        private void BuildTeamPlan()
        {
            if (players == null || roles == null || possession == null || ball == null)
            {
                return;
            }
            for (int index = 0; index < players.Length; index++)
            {
                roles[index] = players[index] != null && players[index].IsOnCourt
                    ? BasketballTeamRole.Transition
                    : BasketballTeamRole.Inactive;
                assignedOpponentRosterIndices[index] = -1;
                roleTargets[index] = Vector3.zero;
            }

            BasketballTeamMember owner = possession.Owner;
            if (owner != null && owner.IsOnCourt)
            {
                int ownerIndex = FindRosterIndex(owner.PlayerIndex);
                if (ownerIndex >= 0)
                {
                    roles[ownerIndex] = BasketballTeamRole.BallHandler;
                }
                int primaryOnBallDefender = FindPrimaryOnBallDefender(owner);
                for (int index = 0; index < players.Length; index++)
                {
                    BasketballTeamMember member = players[index];
                    if (member == null || !member.IsOnCourt || member == owner)
                    {
                        continue;
                    }
                    if (member.TeamId == owner.TeamId)
                    {
                        roles[index] = member == possession.IntendedReceiver
                            ? BasketballTeamRole.IntendedReceiver
                            : BasketballTeamRole.Spacer;
                        continue;
                    }
                    BasketballTeamMember assignment = index == primaryOnBallDefender
                        ? owner
                        : FindDefensiveAssignment(member);
                    int assignmentIndex = assignment != null
                        ? FindRosterIndex(assignment.PlayerIndex)
                        : ownerIndex;
                    assignedOpponentRosterIndices[index] = assignmentIndex;
                    roles[index] = index == primaryOnBallDefender
                        ? BasketballTeamRole.OnBallDefender
                        : BasketballTeamRole.OffBallDefender;
                }
                AssignOffenseSupportRoles(owner);
                AssignHelpDefender(owner);
                return;
            }

            BasketballTeamMember intended = possession.IntendedReceiver;
            if ((possession.BallState == BasketballPossessionState.PassFlight ||
                 possession.BallState == BasketballPossessionState.Contested) &&
                intended != null && intended.IsOnCourt)
            {
                for (int index = 0; index < players.Length; index++)
                {
                    BasketballTeamMember member = players[index];
                    if (member == null || !member.IsOnCourt)
                    {
                        continue;
                    }
                    if (member == intended)
                    {
                        roles[index] = BasketballTeamRole.IntendedReceiver;
                    }
                    else if (member.TeamId == intended.TeamId)
                    {
                        roles[index] = BasketballTeamRole.Spacer;
                    }
                    else
                    {
                        BasketballTeamMember assignment =
                            FindDefensiveAssignment(member);
                        assignedOpponentRosterIndices[index] = assignment != null
                            ? FindRosterIndex(assignment.PlayerIndex)
                            : FindRosterIndex(intended.PlayerIndex);
                        roles[index] = BasketballTeamRole.OffBallDefender;
                    }
                }
                AssignTransitionSafety(intended.TeamId, intended);
                int interceptor = FindBestPassInterceptor(
                    1 - intended.TeamId,
                    out Vector3 interceptPoint);
                if (interceptor >= 0)
                {
                    roles[interceptor] = BasketballTeamRole.PassInterceptor;
                    roleTargets[interceptor] = interceptPoint;
                }
                return;
            }

            if (possession.BallState == BasketballPossessionState.ShotFlight)
            {
                BuildShotFlightPlan();
                return;
            }

            Vector3 looseBallTarget = PredictRecoveryTarget(out float looseBallTime);
            for (int teamId = 0; teamId <= 1; teamId++)
            {
                int chaser = FindBestRecoveryPlayer(
                    looseBallTarget,
                    looseBallTime,
                    teamId);
                if (chaser >= 0)
                {
                    roles[chaser] = BasketballTeamRole.LooseBallChaser;
                    roleTargets[chaser] = looseBallTarget;
                }
            }
        }

        private void AssignOffenseSupportRoles(BasketballTeamMember owner)
        {
            if (owner == null || TeamCount(owner.TeamId) < 2)
            {
                return;
            }

            if (TeamCount(owner.TeamId) >= 3)
            {
                AssignTransitionSafety(owner.TeamId, possession.IntendedReceiver ?? owner);
            }
            Vector3 cutTarget = GetCutTarget(owner.TeamId, owner);
            int cutter = FindClosestRoleCandidate(
                cutTarget,
                owner.TeamId,
                BasketballTeamRole.Spacer);
            if (cutter >= 0)
            {
                roles[cutter] = BasketballTeamRole.Cutter;
                roleTargets[cutter] = cutTarget;
            }
        }

        private void AssignTransitionSafety(
            int teamId,
            BasketballTeamMember excluded)
        {
            if (TeamCount(teamId) < 3)
            {
                return;
            }

            Vector3 safetyTarget = GetSafetyTarget(teamId);
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || member == excluded || !member.IsOnCourt ||
                    member.TeamId != teamId || roles[index] != BasketballTeamRole.Spacer)
                {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(
                    member.Controller.State.ActorRootPosition - safetyTarget);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }
            if (best >= 0)
            {
                roles[best] = BasketballTeamRole.TransitionSafety;
                roleTargets[best] = safetyTarget;
            }
        }

        private void AssignHelpDefender(BasketballTeamMember owner)
        {
            int defensiveTeam = 1 - owner.TeamId;
            if (TeamCount(defensiveTeam) < 2)
            {
                return;
            }

            BasketballHoop defendedHoop = court.GetAttackHoop(owner.TeamId);
            if (defendedHoop == null)
            {
                return;
            }
            Vector3 ownerPosition = owner.Controller.State.ActorRootPosition;
            Vector3 towardHoop = Vector3.ProjectOnPlane(
                defendedHoop.Center - ownerPosition,
                court.CourtUp);
            if (towardHoop.sqrMagnitude <= 1e-8f)
            {
                return;
            }
            Vector3 lateral = Vector3.Cross(court.CourtUp, towardHoop.normalized);
            Vector3 localOwner = court.WorldToCourtPoint(ownerPosition);
            float lateralSign = localOwner.x >= 0f ? -1f : 1f;
            Vector3 helpTarget = ownerPosition +
                                 towardHoop.normalized * helpDefenseDepth +
                                 lateral * (0.75f * lateralSign);
            helpTarget = ClampTargetToCourt(helpTarget);
            int helper = FindClosestRoleCandidate(
                helpTarget,
                defensiveTeam,
                BasketballTeamRole.OffBallDefender);
            if (helper >= 0)
            {
                roles[helper] = BasketballTeamRole.HelpDefender;
                roleTargets[helper] = helpTarget;
            }
        }

        private void BuildShotFlightPlan()
        {
            Vector3 recoveryTarget = PredictRecoveryTarget(out float recoveryTime);
            int homeRebounder = FindBestRecoveryPlayer(recoveryTarget, recoveryTime, 0);
            int awayRebounder = FindBestRecoveryPlayer(recoveryTarget, recoveryTime, 1);
            if (homeRebounder >= 0)
            {
                roles[homeRebounder] = BasketballTeamRole.Rebounder;
                roleTargets[homeRebounder] = recoveryTarget;
            }
            if (awayRebounder >= 0)
            {
                roles[awayRebounder] = BasketballTeamRole.Rebounder;
                roleTargets[awayRebounder] = recoveryTarget;
            }

            BasketballTeamMember shooter = possession.LastShotPlan.Shooter;
            int attackingTeam = shooter != null
                ? shooter.TeamId
                : possession.PreviousOwner != null
                    ? possession.PreviousOwner.TeamId
                    : -1;
            if (attackingTeam < 0 || attackingTeam > 1)
            {
                return;
            }

            int attackingRebounder = attackingTeam == 0
                ? homeRebounder
                : awayRebounder;
            BasketballTeamMember excluded = attackingRebounder >= 0
                ? players[attackingRebounder]
                : shooter;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member != null && member.IsOnCourt &&
                    member.TeamId == attackingTeam &&
                    roles[index] == BasketballTeamRole.Transition)
                {
                    roles[index] = BasketballTeamRole.Spacer;
                }
            }
            AssignTransitionSafety(attackingTeam, excluded);

            if (attackingRebounder < 0)
            {
                return;
            }
            int defensiveTeam = 1 - attackingTeam;
            Vector3 opponentPosition =
                players[attackingRebounder].Controller.State.ActorRootPosition;
            int boxOut = FindClosestRoleCandidate(
                opponentPosition,
                defensiveTeam,
                BasketballTeamRole.Transition);
            BasketballHoop defendedHoop = court.GetAttackHoop(attackingTeam);
            if (boxOut >= 0 && defendedHoop != null)
            {
                Vector3 toHoop = Vector3.ProjectOnPlane(
                    defendedHoop.Center - opponentPosition,
                    court.CourtUp);
                roles[boxOut] = BasketballTeamRole.BoxOutDefender;
                assignedOpponentRosterIndices[boxOut] = attackingRebounder;
                roleTargets[boxOut] = opponentPosition +
                                      (toHoop.sqrMagnitude > 1e-8f
                                          ? toHoop.normalized * 0.7f
                                          : Vector3.zero);
            }
        }

        private int FindClosestRoleCandidate(
            Vector3 target,
            int teamId,
            BasketballTeamRole requiredRole)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt ||
                    member.TeamId != teamId || roles[index] != requiredRole ||
                    member.Controller?.State == null)
                {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(
                    member.Controller.State.ActorRootPosition - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }
            return best;
        }

        private BasketballIntent BuildTransitionIntent(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            BasketballHoop defended = court.GetAttackHoop(1 - member.TeamId);
            Vector3 target = defended != null
                ? Vector3.Lerp(court.CourtCenter, defended.Center, 0.36f)
                : court.CourtCenter;
            ApplyMove(
                ref intent,
                member,
                target,
                ball.transform.position,
                sprint: false,
                avoidPlayers: true);
            return intent;
        }

        private BasketballTeamMember FindBestPassTarget(
            BasketballTeamMember passer,
            BasketballHoop hoop,
            out float gain)
        {
            BasketballTeamMember best = null;
            float bestScore = float.NegativeInfinity;
            Vector3 passerPosition = passer.Controller.State.ActorRootPosition;
            float passerHoopDistance = Vector3.Distance(passerPosition, hoop.Center);
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt || candidate == passer ||
                    candidate.TeamId != passer.TeamId || candidate.Controller?.State == null)
                {
                    continue;
                }
                Vector3 candidatePosition = candidate.Controller.State.ActorRootPosition;
                float passDistance = Vector3.Distance(passerPosition, candidatePosition);
                bool immediateReturnTarget = candidate == possession.PreviousOwner &&
                                             Time.time - possession.LastPossessionChangeTime <
                                             returnPassLockoutSeconds;
                if (immediateReturnTarget || passDistance < 1.75f ||
                    passDistance > passer.MaximumEffectivePassDistance)
                {
                    continue;
                }
                float progress = passerHoopDistance -
                                 Vector3.Distance(candidatePosition, hoop.Center);
                float space = NearestOpponentDistance(candidate, candidatePosition);
                float laneClearance = NearestOpponentLaneDistance(
                    passer.TeamId,
                    passerPosition,
                    candidatePosition);
                if (laneClearance < 0.45f)
                {
                    continue;
                }
                float score = progress + 0.45f * space +
                              0.55f * Mathf.Min(2.5f, laneClearance) -
                              0.08f * passDistance;
                if (candidate.Controller.CurrentIntent.CallForPass)
                {
                    // A human request is an AI preference, not a command. Range,
                    // return-pass lockout, lane clearance, gain threshold and the
                    // physical pass-plan capability checks still have authority.
                    score += callForPassScoreBonus;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            gain = best != null ? bestScore : float.NegativeInfinity;
            return best;
        }

        private BasketballPassType SelectPassType(
            BasketballTeamMember passer,
            BasketballTeamMember receiver)
        {
            if (passer?.Controller?.State == null || receiver?.Controller?.State == null)
            {
                return BasketballPassType.Chest;
            }

            Vector3 passerPosition = passer.Controller.State.ActorRootPosition;
            Vector3 receiverPosition = receiver.Controller.State.ActorRootPosition;
            float distance = Vector3.Distance(passerPosition, receiverPosition);
            Vector3 receiverVelocity = receiver.Controller.State.RootVelocities != null &&
                                       receiver.Controller.State.RootVelocities.Length >
                                       BasketballAgentState.Pivot
                ? receiver.Controller.State.RootVelocities[BasketballAgentState.Pivot]
                : Vector3.zero;
            receiverVelocity = Vector3.ProjectOnPlane(
                receiverVelocity,
                court != null ? court.CourtUp : Vector3.up);

            // Chest is the normal, flatter pass. Lead is reserved for a receiver
            // that is actually moving or for a medium-distance pass. Lob is never
            // selected implicitly by the rule AI.
            return receiverVelocity.magnitude > 1.15f || distance > 5.5f
                ? BasketballPassType.Lead
                : BasketballPassType.Chest;
        }

        private int FindBestPassInterceptor(
            int defendingTeamId,
            out Vector3 interceptPoint)
        {
            int best = -1;
            float bestScore = float.PositiveInfinity;
            interceptPoint = ball.transform.position;
            float maximumTime = Mathf.Clamp(
                possession.CurrentPass.ExpectedFlightTime,
                0.15f,
                recoveryPredictionHorizon);
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId != defendingTeamId ||
                    candidate.Controller?.State == null)
                {
                    continue;
                }
                Vector3 position = candidate.Controller.State.ActorRootPosition;
                bool reachable = BasketballBallTrajectoryPredictor
                    .TryFindEarliestReachableIntercept(
                        position,
                        recoveryRunSpeed,
                        interceptReactionTime,
                        interceptReachRadius,
                        ball.transform.position,
                        ball.Velocity,
                        Physics.gravity,
                        court.CourtCenter,
                        court.CourtUp,
                        0.25f,
                        maximumInterceptHeight,
                        maximumTime,
                        interceptTrajectorySamples,
                        interceptArrivalSlack,
                        out Vector3 point,
                        out float time,
                        out float margin);
                if (!reachable)
                {
                    continue;
                }
                float score = time - 0.1f * margin;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = index;
                    interceptPoint = ClampTargetToCourt(point);
                }
            }
            return best;
        }

        private int FindPrimaryOnBallDefender(BasketballTeamMember owner)
        {
            if (owner?.Controller?.State == null)
            {
                return -1;
            }
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            Vector3 ownerPosition = owner.Controller.State.ActorRootPosition;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId == owner.TeamId ||
                    candidate.Controller?.State == null)
                {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(
                    candidate.Controller.State.ActorRootPosition - ownerPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }
            return best;
        }

        private BasketballTeamMember FindDefensiveAssignment(BasketballTeamMember defender)
        {
            int defenderRank = TeamRank(defender);
            BasketballTeamMember fallback = null;
            float fallbackDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId == defender.TeamId || candidate.Controller?.State == null)
                {
                    continue;
                }
                if (TeamRank(candidate) == defenderRank)
                {
                    return candidate;
                }
                float distance = Vector3.SqrMagnitude(
                    candidate.Controller.State.ActorRootPosition -
                    defender.Controller.State.ActorRootPosition);
                if (distance < fallbackDistance)
                {
                    fallbackDistance = distance;
                    fallback = candidate;
                }
            }
            return fallback;
        }

        private int FindBestRecoveryPlayer(
            Vector3 position,
            float ballArrivalTime,
            int teamId)
        {
            int best = -1;
            float bestScore = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId != teamId || candidate.Controller?.State == null)
                {
                    continue;
                }
                BasketballAgentState state = candidate.Controller.State;
                Vector3 velocity = state.RootVelocities != null &&
                                   state.RootVelocities.Length > BasketballAgentState.Pivot
                    ? Vector3.ProjectOnPlane(
                        state.RootVelocities[BasketballAgentState.Pivot],
                        court.CourtUp)
                    : Vector3.zero;
                Vector3 anticipatedPosition = state.ActorRootPosition +
                                              velocity * Mathf.Min(0.3f, ballArrivalTime);
                float distance = Vector3.Distance(anticipatedPosition, position);
                float arrivalTime = distance / Mathf.Max(0.5f, recoveryRunSpeed);
                float latePenalty = Mathf.Max(0f, arrivalTime - ballArrivalTime);
                float score = arrivalTime + 2f * latePenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }
            return best;
        }

        private Vector3 PredictRecoveryTarget(out float recoveryTime)
        {
            Vector3 position = ball.transform.position;
            Vector3 velocity = ball.Velocity;
            bool reachesCourt = BasketballBallTrajectoryPredictor.TryPredictPlaneContact(
                position,
                velocity,
                Physics.gravity,
                court.CourtCenter,
                court.CourtUp,
                ball.Radius,
                recoveryPredictionHorizon,
                out Vector3 contactCenter,
                out recoveryTime);
            if (!reachesCourt)
            {
                recoveryTime = Mathf.Min(0.5f, recoveryPredictionHorizon);
                contactCenter = BasketballBallTrajectoryPredictor.EvaluatePosition(
                    position,
                    velocity,
                    Physics.gravity,
                    recoveryTime);
            }
            else if (recoveryTime <= 0.05f)
            {
                recoveryTime = Mathf.Min(rollingBallLeadTime, recoveryPredictionHorizon);
                Vector3 planarVelocity = Vector3.ProjectOnPlane(
                    velocity,
                    court.CourtUp);
                contactCenter = position + planarVelocity * recoveryTime;
            }
            return ClampTargetToCourt(contactCenter);
        }

        private float NearestOpponentDistance(
            BasketballTeamMember member,
            Vector3 position)
        {
            float best = 20f;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId == member.TeamId || candidate.Controller?.State == null)
                {
                    continue;
                }
                best = Mathf.Min(
                    best,
                    Vector3.Distance(candidate.Controller.State.ActorRootPosition, position));
            }
            return best;
        }

        private float NearestOpponentLaneDistance(
            int attackingTeamId,
            Vector3 segmentStart,
            Vector3 segmentEnd)
        {
            float best = 20f;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId == attackingTeamId ||
                    candidate.Controller?.State == null)
                {
                    continue;
                }
                Vector3 position = candidate.Controller.State.ActorRootPosition;
                best = Mathf.Min(
                    best,
                    Vector3.Distance(
                        position,
                        ClosestPointOnSegment(position, segmentStart, segmentEnd)));
            }
            return best;
        }

        private static Vector3 ClosestPointOnSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 1e-8f)
            {
                return start;
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return start + t * segment;
        }

        private Vector3 GetSpacingTarget(BasketballTeamMember member)
        {
            int rank = TeamRank(member);
            int count = TeamCount(member.TeamId);
            float normalized = count > 1 ? (float)rank / (count - 1) : 0.5f;
            float x = Mathf.Lerp(-5.4f, 5.4f, normalized);
            float centerWeight = 1f - Mathf.Abs(2f * normalized - 1f);
            float longitudinal = Mathf.Lerp(8.1f, 4.2f, centerWeight);
            float sign = member.TeamId == 0 ? 1f : -1f;
            return ClampTargetToCourt(
                court.CourtToWorldPoint(
                    new Vector3(x, 0f, sign * longitudinal)));
        }

        private Vector3 GetCutTarget(
            int teamId,
            BasketballTeamMember owner)
        {
            BasketballHoop hoop = court.GetAttackHoop(teamId);
            if (hoop == null)
            {
                return GetSpacingTarget(owner);
            }
            Vector3 localHoop = court.WorldToCourtPoint(hoop.Center);
            Vector3 localOwner = court.WorldToCourtPoint(
                owner.Controller.State.ActorRootPosition);
            float attackSign = localHoop.z >= 0f ? 1f : -1f;
            float laneX = localOwner.x >= 0f ? -cutLaneOffset : cutLaneOffset;
            Vector3 localTarget = new(
                laneX,
                0f,
                localHoop.z - attackSign * 3.1f);
            return ClampTargetToCourt(court.CourtToWorldPoint(localTarget));
        }

        private Vector3 GetSafetyTarget(int teamId)
        {
            float defendedSide = teamId == 0 ? -1f : 1f;
            return ClampTargetToCourt(
                court.CourtToWorldPoint(
                    new Vector3(0f, 0f, defendedSide * 5.8f)));
        }

        private Vector3 ClampTargetToCourt(Vector3 worldTarget)
        {
            return court.ClampToPlayableArea(
                worldTarget,
                courtBoundaryMargin,
                preserveHeight: false);
        }

        private int TeamRank(BasketballTeamMember member)
        {
            int rank = 0;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == member)
                {
                    return rank;
                }
                if (candidate != null && candidate.IsOnCourt &&
                    candidate.TeamId == member.TeamId)
                {
                    rank++;
                }
            }
            return rank;
        }

        private int TeamCount(int teamId)
        {
            int count = 0;
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] != null && players[index].IsOnCourt &&
                    players[index].TeamId == teamId)
                {
                    count++;
                }
            }
            return count;
        }

        private int FindRosterIndex(int playerIndex)
        {
            if (players == null)
            {
                return -1;
            }
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] != null && players[index].PlayerIndex == playerIndex)
                {
                    return index;
                }
            }
            return -1;
        }

        private void ApplyMove(
            ref BasketballIntent intent,
            BasketballAgentState state,
            Vector3 target,
            Vector3 facingTarget,
            bool sprint)
        {
            Vector3 up = court != null ? court.CourtUp : Vector3.up;
            Vector3 move = Vector3.ProjectOnPlane(
                target - state.ActorRootPosition,
                up);
            intent.UseWorldMove = move.magnitude > 0.22f;
            intent.WorldMove = Vector3.ClampMagnitude(move, 1f);
            intent.UseWorldFacing = true;
            intent.WorldFacing = Vector3.ProjectOnPlane(
                facingTarget - state.ActorRootPosition,
                up);
            intent.Sprint = sprint && move.magnitude > 1.5f;
        }

        private void ApplyMove(
            ref BasketballIntent intent,
            BasketballTeamMember member,
            Vector3 target,
            Vector3 facingTarget,
            bool sprint,
            bool avoidPlayers)
        {
            BasketballAgentState state = member.Controller.State;
            Vector3 adjustedTarget = ClampTargetToCourt(target);
            if (avoidPlayers && playerAvoidanceStrength > 0f)
            {
                Vector3 avoidance = Vector3.zero;
                for (int index = 0; index < players.Length; index++)
                {
                    BasketballTeamMember other = players[index];
                    if (other == null || other == member || !other.IsOnCourt ||
                        other.Controller?.State == null)
                    {
                        continue;
                    }
                    Vector3 away = Vector3.ProjectOnPlane(
                        state.ActorRootPosition -
                        other.Controller.State.ActorRootPosition,
                        court != null ? court.CourtUp : Vector3.up);
                    float distance = away.magnitude;
                    if (distance <= 1e-4f || distance >= playerAvoidanceRadius)
                    {
                        continue;
                    }
                    float weight = 1f - distance / playerAvoidanceRadius;
                    avoidance += away / distance * weight;
                }
                adjustedTarget += avoidance * playerAvoidanceStrength;
                adjustedTarget = ClampTargetToCourt(adjustedTarget);
            }
            ApplyMove(
                ref intent,
                state,
                adjustedTarget,
                facingTarget,
                sprint);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDecisionGizmos || players == null || roles == null)
            {
                return;
            }
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt || member.Controller?.State == null)
                {
                    continue;
                }
                BasketballTeamRole role = roles[index];
                Gizmos.color = RoleColor(role);
                Vector3 position = member.Controller.State.ActorRootPosition +
                                   0.08f * Vector3.up;
                Gizmos.DrawWireSphere(position, 0.2f);
                if (role == BasketballTeamRole.PassInterceptor ||
                    role == BasketballTeamRole.Cutter ||
                    role == BasketballTeamRole.TransitionSafety ||
                    role == BasketballTeamRole.HelpDefender ||
                    role == BasketballTeamRole.BoxOutDefender)
                {
                    Gizmos.DrawLine(position, roleTargets[index]);
                    Gizmos.DrawWireSphere(roleTargets[index], 0.16f);
                }
                else if (role == BasketballTeamRole.LooseBallChaser ||
                         role == BasketballTeamRole.Rebounder)
                {
                    Gizmos.DrawLine(position, roleTargets[index]);
                    Gizmos.DrawWireSphere(roleTargets[index], 0.2f);
                }
                int assignmentIndex = assignedOpponentRosterIndices[index];
                if (assignmentIndex >= 0 && assignmentIndex < players.Length &&
                    players[assignmentIndex]?.Controller?.State != null)
                {
                    Gizmos.DrawLine(
                        position,
                        players[assignmentIndex].Controller.State.ActorRootPosition);
                }
            }
        }

        private static Color RoleColor(BasketballTeamRole role)
        {
            return role switch
            {
                BasketballTeamRole.BallHandler => new Color(1f, 0.7f, 0.1f),
                BasketballTeamRole.IntendedReceiver => Color.cyan,
                BasketballTeamRole.PassInterceptor => new Color(1f, 0.2f, 0.75f),
                BasketballTeamRole.LooseBallChaser => Color.yellow,
                BasketballTeamRole.Rebounder => new Color(0.4f, 1f, 0.25f),
                BasketballTeamRole.BoxOutDefender => new Color(0.65f, 0.35f, 1f),
                BasketballTeamRole.OnBallDefender => Color.red,
                BasketballTeamRole.OffBallDefender => new Color(1f, 0.45f, 0.25f),
                BasketballTeamRole.HelpDefender => new Color(1f, 0.2f, 0.2f),
                BasketballTeamRole.Spacer => new Color(0.25f, 0.65f, 1f),
                BasketballTeamRole.Cutter => new Color(0.1f, 1f, 0.65f),
                BasketballTeamRole.TransitionSafety => new Color(0.25f, 0.85f, 1f),
                _ => Color.gray
            };
        }

#if UNITY_EDITOR
        public void UpgradeTuningDefaults()
        {
            if (tuningVersion >= 2)
            {
                return;
            }
            decisionInterval = 0.1f;
            maximumShotAlignTime = 0.22f;
            defenseSpacing = 0.9f;
            stealAttemptDistance = 1.15f;
            minimumStealFacingDot = 0.35f;
            stealAttemptDuration = 0.18f;
            stealAttemptCooldown = 1.15f;
            protectBallPressureDistance = 1.8f;
            protectBallControlStrength = 0.72f;
            protectedHandSwitchInterval = 0.85f;
            minimumPossessionSecondsBeforePass = 1.05f;
            returnPassLockoutSeconds = 2.25f;
            minimumOpenPassGain = 2.25f;
            minimumPressuredPassGain = 0.85f;
            defenseSprintDistance = 1.6f;
            offBallSprintDistance = 2.4f;
            tuningVersion = 2;
        }

        public void Configure(
            BasketballTeamMember[] roster,
            BasketballPossessionManager possessionManager,
            BasketballBallController sharedBall,
            BasketballCourt basketballCourt)
        {
            players = roster;
            possession = possessionManager;
            ball = sharedBall;
            court = basketballCourt;
        }
#endif
    }
}
