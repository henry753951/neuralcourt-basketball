using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BasketballMatchController : MonoBehaviour
    {
        [SerializeField] private BasketballTeamGroup[] teamGroups;
        [SerializeField] private BasketballTeamMember[] players;
        [SerializeField] private BasketballBallController ball;
        [SerializeField] private BasketballPossessionManager possessionManager;
        [SerializeField] private BasketballCourt court;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private ThirdPersonOrbitCamera orbitCamera;
        [SerializeField] private BasketballUIToolkitController hud;
        [SerializeField] private BasketballSentisBatchScheduler sentisBatchScheduler;
        [SerializeField] private BasketballRuntimeSettings runtimeSettings;
        [SerializeField] private BasketballMatchMode matchMode = BasketballMatchMode.FiveOnFive;
        [SerializeField] private BasketballMatchControlMode controlMode =
            BasketballMatchControlMode.SelectedPlayerWithRuleAI;
        [SerializeField, Range(1, 5)] private int customHomePlayers = 5;
        [SerializeField, Range(1, 5)] private int customAwayPlayers = 5;
        [SerializeField] private BasketballRuleBasedTeamAI ruleBasedTeamAI;
        [SerializeField] private MonoBehaviour decisionPolicyBehaviour;
        [SerializeField] private BasketballWorldEventStream worldEventStream;
        [SerializeField] private BasketballRewardTracker rewardTracker;
        [SerializeField] private BasketballSkillTelemetryRecorder telemetryRecorder;
        [SerializeField] private BasketballRulesManager rulesManager;
        [SerializeField, Range(0.5f, 0.999f)] private float passLockDot = 0.82f;
        [SerializeField, Min(0.25f)] private float passTimeoutSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float scoreRestartDelay = 1.25f;
        [SerializeField, Min(0.5f)] private float humanStealAttemptDistance = 1.45f;
        [SerializeField, Min(0.05f)] private float humanStealPulseSeconds = 0.22f;
        [SerializeField, Min(0.1f)] private float humanStealPulseInterval = 0.72f;

        private int activePlayerIndex;
        private BasketballTeamMember lockedTarget;
        private BasketballTeamMember passTarget;
        private BasketballScoreTracker scoreTracker;
        private float passCommittedSeconds;
        private bool passCommitted;
        private bool shotAttempt;
        private bool shotCommitted;
        private float shotElapsed;
        private bool scoreRestartPending;
        private float scoreRestartAt;
        private BasketballScoreEvent pendingScore;
        private bool initialized;
        private bool runtimeSettingsPrepared;
        private int hudStatusKey = int.MinValue;
        private BasketballWorldObservation worldObservation;
        private IBasketballDecisionPolicy decisionPolicy;
        private int lastPolicyEventSequence;
        private bool matchConfigurationPending;
        private bool pendingConfigurationResetScore;
        private BasketballMatchMode pendingMatchMode;
        private BasketballMatchControlMode pendingControlMode;
        private int pendingHomePlayers;
        private int pendingAwayPlayers;
        private float humanStealUntil = float.NegativeInfinity;
        private float nextHumanStealAt = float.NegativeInfinity;
        private bool freeCameraMode;
        private bool callForPassActive;

        public int PlayerCount => players == null ? 0 : players.Length;
        public int OnCourtPlayerCount
        {
            get
            {
                int count = 0;
                if (players != null)
                {
                    for (int index = 0; index < players.Length; index++)
                    {
                        if (players[index] != null && players[index].IsOnCourt)
                        {
                            count++;
                        }
                    }
                }
                return count;
            }
        }
        public int ActivePlayerIndex => activePlayerIndex;
        public BasketballTeamMember ActivePlayer => GetPlayer(activePlayerIndex);
        public BasketballTeamMember Owner => possessionManager != null
            ? possessionManager.Owner
            : null;
        public BasketballPossessionManager PossessionManager => possessionManager;
        public BasketballCourt Court => court;
        public BasketballMatchMode MatchMode => matchMode;
        public BasketballMatchControlMode ControlMode => controlMode;
        public BasketballTeamMember LockedTarget => lockedTarget;
        public BasketballSentisBatchScheduler SentisBatchScheduler => sentisBatchScheduler;
        public BasketballWorldObservation CurrentObservation => worldObservation;
        public BasketballWorldEventStream WorldEvents => worldEventStream;
        public BasketballRewardTracker RewardTracker => rewardTracker;
        public BasketballSkillTelemetryRecorder TelemetryRecorder => telemetryRecorder;
        public BasketballRulesManager Rules => rulesManager;
        public bool IsTargetSelectionActive { get; private set; }
        public bool IsPassCommitted => passCommitted;
        public bool IsMatchConfigurationPending => matchConfigurationPending;
        public bool IsFreeCameraMode => freeCameraMode;
        public bool IsCallingForPass => callForPassActive;

        private void Awake()
        {
            PrepareRuntimeSettings();
        }

        private void Start()
        {
            InitializeMatch();
        }

        private void OnDestroy()
        {
            if (scoreTracker != null)
            {
                scoreTracker.Scored -= HandleScore;
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                InitializeMatch();
            }
            if (!initialized)
            {
                return;
            }

            if (!TryApplyPendingMatchConfiguration())
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            {
                SetFreeCameraMode(!freeCameraMode);
            }
            if (keyboard != null && keyboard.uKey.wasPressedThisFrame)
            {
                hud?.TogglePrimaryHud();
            }
            if (!freeCameraMode && keyboard != null &&
                keyboard.tabKey.wasPressedThisFrame)
            {
                int next = FindNextOnCourtPlayer(activePlayerIndex);
                if (next >= 0)
                {
                    SelectPlayer(next);
                }
            }

            UpdateScoreRestart();
            if (rulesManager != null && rulesManager.EvaluateRules(scoreRestartPending))
            {
                CancelPass();
                CancelShot();
                IsTargetSelectionActive = false;
                lockedTarget = null;
            }
            CaptureWorldObservation();
            RouteIntents(keyboard);
            UpdateIndicators();
            UpdateHudStatus();
        }

        private void CollectPlayersFromTeamGroups()
        {
            if (teamGroups == null || teamGroups.Length == 0) return;
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

        public void InitializeMatch()
        {
            PrepareRuntimeSettings();

            CollectPlayersFromTeamGroups();
            if (possessionManager != null)
            {
                possessionManager.ConfigurePlayers(players);
            }

            if (initialized || players == null || players.Length == 0 || ball == null ||
                possessionManager == null)
            {
                return;
            }

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || member.Controller == null)
                {
                    return;
                }
                member.Controller.Initialize();
                member.Controller.SetIntentOverride(default);
                if (member.Visualizer != null)
                {
                    member.Visualizer.SetVisible(index == 0);
                }
            }

            ApplyMatchMode();
            activePlayerIndex = FindNextOnCourtPlayer(-1);
            if (activePlayerIndex < 0)
            {
                Debug.LogError("Basketball match has no active on-court players.", this);
                return;
            }
            EnsureWorldEventStream();
            EnsureRewardTracker();
            EnsureTelemetryRecorder();
            BasketballCourtBoundary courtBoundary = court != null
                ? court.GetComponentInChildren<BasketballCourtBoundary>(true)
                : null;
            courtBoundary?.SetBall(ball);
            possessionManager.SetWorldEventStream(worldEventStream);
            possessionManager.Initialize();
            EnsureRulesManager();
            EnsureRuleBasedTeamAI();
            BindScoreTracker();
            EnsureSentisBatchScheduler();
            worldObservation = new BasketballWorldObservation(players.Length);
            rewardTracker.ResetEpisode();
            initialized = true;
            CaptureWorldObservation();
            worldEventStream.Publish(
                BasketballWorldEventType.MatchInitialized,
                possessionManager.PossessionVersion,
                possessionManager.Owner,
                position: ball.transform.position);
            ApplyActivePlayer();
            UpdateIndicators();
        }

        private void PrepareRuntimeSettings()
        {
            if (runtimeSettingsPrepared)
            {
                return;
            }
            runtimeSettings = runtimeSettings != null
                ? runtimeSettings
                : BasketballRuntimeSettings.LoadDefault();
            if (runtimeSettings == null)
            {
                Debug.LogError(
                    $"Missing Basketball runtime profile at Resources/" +
                    $"{BasketballRuntimeSettings.DefaultResourcePath}.asset.",
                    this);
                return;
            }

            runtimeSettings.ApplyApplicationSettings();
            int disabledShadowCasters = runtimeSettings.ApplyShadowBudget();
            if (disabledShadowCasters > 0)
            {
                Debug.Log(
                    $"Basketball runtime shadow budget disabled {disabledShadowCasters} " +
                    "redundant realtime shadow caster(s). Lighting remains enabled.",
                    this);
            }

            orbitCamera?.SetRuntimeSettings(runtimeSettings);
            if (players != null)
            {
                for (int index = 0; index < players.Length; index++)
                {
                    players[index]?.Controller?.SetRuntimeSettings(runtimeSettings);
                }
            }
            runtimeSettingsPrepared = true;
        }

        public void SelectPlayer(int index)
        {
            if (!initialized || players == null || players.Length == 0)
            {
                return;
            }
            int normalized = ((index % players.Length) + players.Length) % players.Length;
            if (players[normalized] == null || !players[normalized].IsOnCourt)
            {
                normalized = FindNextOnCourtPlayer(normalized);
                if (normalized < 0)
                {
                    return;
                }
            }
            if (normalized == activePlayerIndex)
            {
                return;
            }
            CancelPass();
            CancelShot();
            activePlayerIndex = normalized;
            ApplyActivePlayer();
            UpdateIndicators();
        }

        public void SetFreeCameraMode(bool enabled)
        {
            if (freeCameraMode == enabled)
            {
                return;
            }

            freeCameraMode = enabled;
            callForPassActive = false;
            humanStealUntil = float.NegativeInfinity;
            nextHumanStealAt = float.NegativeInfinity;
            CancelPass();
            CancelShot();
            IsTargetSelectionActive = false;
            lockedTarget = null;
            orbitCamera?.SetFreeCameraMode(enabled);
            hud?.SetFreeCameraMode(enabled);
            hudStatusKey = int.MinValue;
            if (enabled)
            {
                for (int index = 0; index < players.Length; index++)
                {
                    players[index]?.Visualizer?.SetVisible(false);
                }
            }
            else
            {
                ApplyActivePlayer();
            }
            UpdateIndicators();
        }

        public bool ApplyMatchConfiguration(
            BasketballMatchMode newMatchMode,
            BasketballMatchControlMode newControlMode,
            int homePlayers = 5,
            int awayPlayers = 5,
            bool resetScore = true)
        {
            matchMode = newMatchMode;
            controlMode = newControlMode;
            customHomePlayers = Mathf.Clamp(homePlayers, 1, 5);
            customAwayPlayers = Mathf.Clamp(awayPlayers, 1, 5);
            if (!initialized)
            {
                return true;
            }

            matchConfigurationPending = true;
            pendingConfigurationResetScore = resetScore;
            pendingMatchMode = newMatchMode;
            pendingControlMode = newControlMode;
            pendingHomePlayers = customHomePlayers;
            pendingAwayPlayers = customAwayPlayers;
            if (sentisBatchScheduler != null &&
                sentisBatchScheduler.IsReadbackPending)
            {
                sentisBatchScheduler.RequestStateResetBarrier();
            }
            return TryApplyPendingMatchConfiguration();
        }

        private bool TryApplyPendingMatchConfiguration()
        {
            if (!matchConfigurationPending)
            {
                return true;
            }
            if (sentisBatchScheduler != null &&
                sentisBatchScheduler.IsReadbackPending)
            {
                // The already-dispatched batch still refers to the current
                // recurrent state. Apply the reset next frame after readback so
                // stale GPU output can never overwrite the new formation.
                return true;
            }

            bool resetScore = pendingConfigurationResetScore;
            BasketballMatchMode requestedMatchMode = pendingMatchMode;
            BasketballMatchControlMode requestedControlMode = pendingControlMode;
            int requestedHomePlayers = pendingHomePlayers;
            int requestedAwayPlayers = pendingAwayPlayers;
            matchConfigurationPending = false;
            try
            {
                return ApplyMatchConfigurationNow(
                    requestedMatchMode,
                    requestedControlMode,
                    requestedHomePlayers,
                    requestedAwayPlayers,
                    resetScore);
            }
            finally
            {
                sentisBatchScheduler?.ReleaseStateResetBarrier();
            }
        }

        private bool ApplyMatchConfigurationNow(
            BasketballMatchMode requestedMatchMode,
            BasketballMatchControlMode requestedControlMode,
            int requestedHomePlayers,
            int requestedAwayPlayers,
            bool resetScore)
        {
            matchMode = requestedMatchMode;
            controlMode = requestedControlMode;
            customHomePlayers = Mathf.Clamp(requestedHomePlayers, 1, 5);
            customAwayPlayers = Mathf.Clamp(requestedAwayPlayers, 1, 5);

            CancelPass();
            CancelShot();
            scoreRestartPending = false;
            pendingScore = default;
            IsTargetSelectionActive = false;
            lockedTarget = null;
            ApplyMatchMode();

            if (!ResetPlayersToFormation())
            {
                return false;
            }

            activePlayerIndex = FindFirstOnCourtPlayerForTeam(0);
            if (activePlayerIndex < 0)
            {
                activePlayerIndex = FindNextOnCourtPlayer(-1);
            }
            BasketballTeamMember fallbackOwner = ActivePlayer;
            BasketballAgentState ownerState = fallbackOwner?.Controller?.State;
            if (fallbackOwner == null || ownerState == null)
            {
                Debug.LogError(
                    "Basketball match configuration has no valid active fallback owner.",
                    this);
                return false;
            }

            Vector3 ownerForward = ownerState.ActorRootRotation * Vector3.forward;
            Vector3 controlledBallPosition = fallbackOwner.AimPoint +
                                             0.16f * ownerForward;
            if (!possessionManager.ResetPossession(
                    fallbackOwner,
                    controlledBallPosition,
                    Quaternion.identity))
            {
                Debug.LogError(
                    "Basketball match configuration could not reset possession.",
                    this);
                return false;
            }

            if (resetScore)
            {
                scoreTracker?.ResetScore();
            }
            rewardTracker?.ResetEpisode();
            rulesManager?.ConfigureRuntime(
                players,
                ball,
                possessionManager,
                court,
                worldEventStream);
            ruleBasedTeamAI?.ConfigureRuntime(players, possessionManager, ball, court);
            CaptureWorldObservation();
            ApplyActivePlayer();
            UpdateIndicators();
            worldEventStream?.Publish(
                BasketballWorldEventType.MatchConfigurationChanged,
                possessionManager.PossessionVersion,
                fallbackOwner,
                position: ball.transform.position,
                value: OnCourtPlayerCount);
            return true;
        }

        private bool ResetPlayersToFormation()
        {
            int homeActive = CountOnCourtPlayers(0);
            int awayActive = CountOnCourtPlayers(1);
            int homeSlot = 0;
            int awaySlot = 0;

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || member.Controller == null)
                {
                    continue;
                }

                int teamSlot;
                int activeCount;
                if (member.TeamId == 0)
                {
                    teamSlot = homeSlot++;
                    activeCount = homeActive;
                }
                else if (member.TeamId == 1)
                {
                    teamSlot = awaySlot++;
                    activeCount = awayActive;
                }
                else
                {
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                if (member.IsOnCourt)
                {
                    BasketballFormationLayout.GetWorldOnCourtPose(
                        court,
                        member.TeamId,
                        teamSlot,
                        activeCount,
                        out position,
                        out rotation);
                }
                else
                {
                    BasketballFormationLayout.GetWorldBenchPose(
                        court,
                        member.TeamId,
                        teamSlot,
                        out position,
                        out rotation);
                }

                if (!member.Controller.ResetSimulationPose(position, rotation))
                {
                    Debug.LogError(
                        $"Could not reset simulation pose for player " +
                        $"{member.PlayerIndex}.",
                        member);
                    return false;
                }
                member.Controller.SetIntentOverride(default);
            }
            return true;
        }

        private int CountOnCourtPlayers(int teamId)
        {
            int count = 0;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member != null && member.TeamId == teamId && member.IsOnCourt)
                {
                    count++;
                }
            }
            return count;
        }

        private int FindFirstOnCourtPlayerForTeam(int teamId)
        {
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member != null && member.TeamId == teamId && member.IsOnCourt)
                {
                    return index;
                }
            }
            return -1;
        }

        public bool IsValidPassTarget(
            BasketballTeamMember passer,
            BasketballTeamMember target)
        {
            return passer != null && target != null && passer.IsOnCourt &&
                   target.IsOnCourt && passer != target &&
                   passer.TeamId == target.TeamId;
        }

        private void RouteIntents(Keyboard keyboard)
        {
            if (scoreRestartPending ||
                (rulesManager != null && rulesManager.IsRestartPending))
            {
                callForPassActive = false;
                CancelPass();
                CancelShot();
                IsTargetSelectionActive = false;
                lockedTarget = null;
                for (int index = 0; index < players.Length; index++)
                {
                    players[index]?.Controller?.SetIntentOverride(default);
                }
                return;
            }

            BasketballTeamMember active = ActivePlayer;
            Mouse mouse = Mouse.current;
            bool allowHumanInput = !freeCameraMode &&
                                   controlMode != BasketballMatchControlMode.FullRuleAI;
            bool controlHeld = allowHumanInput && keyboard != null &&
                               (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool shootHeld = allowHumanInput && keyboard != null && keyboard.spaceKey.isPressed;
            bool pointerAvailable = orbitCamera == null || orbitCamera.CursorLocked;
            bool primaryHeld = allowHumanInput && pointerAvailable && mouse != null &&
                               mouse.leftButton.isPressed;
            bool primaryPressed = allowHumanInput && pointerAvailable && mouse != null &&
                                  mouse.leftButton.wasPressedThisFrame;
            bool activeOwnsBall = allowHumanInput && active != null && Owner == active &&
                                  possessionManager.HasBall(active.Controller);
            bool teammateOwnsBall = allowHumanInput && active != null && Owner != null &&
                                    Owner != active && Owner.TeamId == active.TeamId &&
                                    possessionManager.BallState ==
                                    BasketballPossessionState.Possessed;
            callForPassActive = teammateOwnsBall && controlHeld;

            if (shotAttempt && (!activeOwnsBall ||
                                possessionManager.BallState !=
                                BasketballPossessionState.Possessed))
            {
                CancelShot();
            }
            if (activeOwnsBall && keyboard != null &&
                keyboard.spaceKey.wasPressedThisFrame && !passCommitted)
            {
                CancelPass();
                shotAttempt = true;
                shotCommitted = false;
                shotElapsed = 0f;
            }

            IsTargetSelectionActive = activeOwnsBall && controlHeld && !shootHeld &&
                                      !passCommitted && !shotAttempt;
            lockedTarget = IsTargetSelectionActive ? FindBestPassTarget(active) : null;

            if (IsTargetSelectionActive && lockedTarget != null &&
                primaryPressed)
            {
                passTarget = lockedTarget;
                passCommitted = possessionManager.RequestPass(active, passTarget);
                if (passCommitted)
                {
                    passCommittedSeconds = 0f;
                }
                else
                {
                    CancelPass();
                }
            }

            if (passCommitted)
            {
                passCommittedSeconds += Time.unscaledDeltaTime;
                bool stillPreparing = possessionManager.BallState ==
                                      BasketballPossessionState.PassPreparing;
                if (passCommittedSeconds >= passTimeoutSeconds || Owner != active ||
                    !stillPreparing)
                {
                    CancelPass();
                }
            }

            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || !member.IsOnCourt)
                {
                    member?.Controller?.SetIntentOverride(default);
                    continue;
                }
                BasketballIntent intent;
                bool usePlayerInput = index == activePlayerIndex &&
                                      allowHumanInput;
                if (usePlayerInput)
                {
                    intent = member.InputProvider != null
                        ? member.InputProvider.ReadIntent()
                        : default;
                    bool enemyOwnsBall = Owner != null && Owner != member &&
                                         Owner.TeamId != member.TeamId;
                    intent.Steal = ResolveHumanStealPulse(
                        member,
                        primaryHeld && enemyOwnsBall);
                    intent.CallForPass = callForPassActive;
                    if (callForPassActive)
                    {
                        // Ctrl is a team request here, not a remote Hold target.
                        // CatchReady supplies only the original Hold style so the
                        // player can present a receiver posture without writing ball
                        // Pivot/Momentum controls.
                        intent.Hold = false;
                        intent.CatchReady = true;
                    }
                    else if (!activeOwnsBall && Owner != null && Owner != member)
                    {
                        intent.Hold = false;
                    }
                    if (shotAttempt)
                    {
                        ApplyActiveShotIntent(member, ref intent);
                    }
                    else
                    {
                        ApplyActivePassIntent(member, ref intent);
                        ApplyActiveReceiveIntent(member, ref intent);
                    }
                }
                else if ((freeCameraMode ||
                          controlMode != BasketballMatchControlMode.SelectedPlayerOnly) &&
                          decisionPolicy != null)
                {
                    BasketballPlayerCommand command = decisionPolicy.Decide(
                        worldObservation.GetPlayer(index));
                    intent = command.Intent;
                    ExecutePolicyCommand(member, command, ref intent);
                }
                else
                {
                    intent = BuildStandbyIntent(member);
                }
                member.Controller.SetIntentOverride(intent);
            }
        }

        private bool ResolveHumanStealPulse(
            BasketballTeamMember defender,
            bool requested)
        {
            if (!requested || defender?.Controller?.State == null ||
                Owner?.Controller?.State == null)
            {
                humanStealUntil = float.NegativeInfinity;
                nextHumanStealAt = Time.time;
                return false;
            }

            Vector3 defenderRoot = defender.Controller.State.ActorRootPosition;
            Vector3 ownerRoot = Owner.Controller.State.ActorRootPosition;
            float interactionDistance = Vector3.Distance(defenderRoot, ownerRoot);
            float ballDistance = Vector3.Distance(defenderRoot, ball.transform.position);
            if (interactionDistance > humanStealAttemptDistance ||
                ballDistance > humanStealAttemptDistance + 0.35f)
            {
                humanStealUntil = float.NegativeInfinity;
                return false;
            }

            if (Time.time >= nextHumanStealAt && Time.time >= humanStealUntil)
            {
                humanStealUntil = Time.time + humanStealPulseSeconds;
                nextHumanStealAt = Time.time + humanStealPulseInterval;
            }
            return Time.time < humanStealUntil;
        }

        private void ExecutePolicyCommand(
            BasketballTeamMember member,
            in BasketballPlayerCommand command,
            ref BasketballIntent intent)
        {
            if (!command.HasSkillRequest)
            {
                return;
            }

            BasketballSkillCommandResult result;
            if (Time.time > command.ExpiresAt)
            {
                result = BasketballSkillCommandResult.RejectedExpired;
            }
            else if (member == null || !member.IsOnCourt ||
                     member.PlayerIndex != command.ActorPlayerIndex)
            {
                result = BasketballSkillCommandResult.RejectedActor;
            }
            else if (command.PossessionVersion != possessionManager.PossessionVersion)
            {
                result = BasketballSkillCommandResult.RejectedStalePossession;
            }
            else
            {
                result = BasketballSkillCommandResult.RejectedState;
                switch (command.Skill)
                {
                    case BasketballSkillCommandType.RequestPass:
                    {
                        BasketballTeamMember target = FindMemberByPlayerIndex(
                            command.TargetPlayerIndex);
                        if (!IsValidPassTarget(member, target))
                        {
                            result = BasketballSkillCommandResult.RejectedTarget;
                            break;
                        }
                        bool accepted = possessionManager.RequestPass(
                            member,
                            target,
                            command.PassType);
                        result = accepted
                            ? BasketballSkillCommandResult.Accepted
                            : BasketballSkillCommandResult.RejectedState;
                        if (accepted && possessionManager.TryGetPassControl(
                                member,
                                out BasketballPassControlProfile profile))
                        {
                            intent.PassTargeting = true;
                            intent.CommitBallRelease = true;
                            intent.PassControl = true;
                            intent.PassHoldStyle = profile.HoldStyle;
                            intent.PassShootStyle = profile.ShootStyle;
                            intent.UseWorldFacing = true;
                            intent.WorldFacing = profile.Facing;
                        }
                        break;
                    }
                }
            }
            if (result != BasketballSkillCommandResult.Accepted)
            {
                worldEventStream?.Publish(
                    BasketballWorldEventType.CommandRejected,
                    possessionManager.PossessionVersion,
                    member,
                    FindMemberByPlayerIndex(command.TargetPlayerIndex),
                    ball.transform.position,
                    value: (float)result,
                    points: command.CommandId);
            }
            decisionPolicy.ReportCommandResult(command, result);
        }

        private void ApplyActiveShotIntent(
            BasketballTeamMember active,
            ref BasketballIntent intent)
        {
            BasketballHoop hoop = court != null
                ? court.GetAttackHoop(active.TeamId)
                : null;
            if (hoop == null || active.Controller == null ||
                active.Controller.State == null)
            {
                intent.Shoot = true;
                return;
            }

            shotElapsed += Time.unscaledDeltaTime;
            Vector3 rootPosition = active.Controller.State.ActorRootPosition;
            Vector3 toHoop = Vector3.ProjectOnPlane(
                hoop.AimPoint - rootPosition,
                Vector3.up);
            float timeout = runtimeSettings != null
                ? runtimeSettings.ShotCommandTimeoutSeconds
                : 1.8f;
            // Shoot is a latched one-shot command. Facing and neural pose evolve
            // during the release window, but the player does not wait in a
            // separate pre-commit state that can make the ball feel stuck.
            shotCommitted = true;

            intent.Move = Vector2.zero;
            intent.Sprint = false;
            intent.Hold = false;
            intent.Steal = false;
            intent.PassTargeting = false;
            intent.CommitBallRelease = false;
            intent.PassControl = false;
            intent.UseWorldFacing = true;
            intent.WorldFacing = toHoop;
            intent.Shoot = shotCommitted;
            active.InputProvider?.SetExternalControlVisualization(Vector2.zero, false);

            if (shotElapsed >= timeout)
            {
                CancelShot();
                intent.Shoot = false;
            }
        }

        private void ApplyActivePassIntent(
            BasketballTeamMember active,
            ref BasketballIntent intent)
        {
            BasketballTeamMember target = passTarget != null ? passTarget : lockedTarget;
            if (passCommitted && possessionManager.TryGetPassControl(
                    active,
                    out BasketballPassControlProfile profile))
            {
                intent.Hold = false;
                intent.Shoot = false;
                intent.PassTargeting = true;
                intent.CommitBallRelease = true;
                intent.PassControl = true;
                intent.PassHoldStyle = profile.HoldStyle;
                intent.PassShootStyle = profile.ShootStyle;
                intent.UseWorldFacing = true;
                intent.WorldFacing = profile.Facing;
                active.InputProvider?.SetExternalControlVisualization(Vector2.zero, false);
                return;
            }

            // Space remains an unambiguous Shoot command even while Ctrl is held.
            // Ctrl only modifies left-mouse into a pass command.
            if (intent.Shoot)
            {
                intent.Hold = false;
                intent.Steal = false;
                return;
            }

            if (IsTargetSelectionActive)
            {
                intent.Move = Vector2.zero;
                intent.Hold = false;
                intent.Shoot = false;
                intent.PassTargeting = true;
                intent.CommitBallRelease = false;
                intent.PassControl = true;
                intent.PassHoldStyle = 0.9f;
                intent.PassShootStyle = 0f;
                if (lockedTarget != null)
                {
                    intent.UseWorldFacing = true;
                    intent.WorldFacing = lockedTarget.AimPoint - active.transform.position;
                }
                active.InputProvider?.SetExternalControlVisualization(Vector2.zero, false);
            }
        }

        private BasketballIntent BuildStandbyIntent(BasketballTeamMember member)
        {
            BasketballIntent intent = default;
            BasketballTeamMember currentOwner = Owner;
            if (member == null || member == currentOwner)
            {
                return intent;
            }

            if (possessionManager.IsIntendedReceiver(member))
            {
                bool passContested = possessionManager.BallState ==
                                     BasketballPossessionState.Contested;
                Vector3 catchPoint = passContested
                    ? ball.transform.position
                    : possessionManager.CurrentPass.PredictedCatchPoint;
                Vector3 toCatchPoint = catchPoint -
                                       member.Controller.State.ActorRootPosition;
                Vector3 planarMove = Vector3.ProjectOnPlane(toCatchPoint, Vector3.up);
                bool ballInFlight = possessionManager.BallState ==
                                    BasketballPossessionState.PassFlight;
                bool ballCatchable = ballInFlight || passContested;
                float timeToArrival = ballInFlight
                    ? Mathf.Max(0f, possessionManager.ExpectedArrivalTime - Time.time)
                    : passContested ? 0f : possessionManager.CurrentPass.ExpectedFlightTime;
                float ballDistance = Vector3.Distance(
                    ball.transform.position,
                    member.Controller.State.ActorRootPosition);

                // Prepare early, but do not feed a distant held ball into the
                // receiver's Hold controls. Hold/Catch starts only during flight
                // when the real ball is close or entering the catch window.
                intent.CatchReady = true;
                intent.Hold = ballCatchable &&
                              (passContested || ballDistance <= 3f ||
                               timeToArrival <= 0.65f);
                intent.UseWorldMove = planarMove.magnitude > 0.22f;
                intent.WorldMove = Vector3.ClampMagnitude(planarMove, 1f);
                intent.UseWorldFacing = true;
                Vector3 facingTarget = ballCatchable
                    ? ball.transform.position
                    : possessionManager.CurrentPass.DesiredReleasePosition;
                intent.WorldFacing = facingTarget -
                                     member.Controller.State.ActorRootPosition;
            }
            // Every other unselected player truly stands by. Previously Hold was
            // forced here, which makes a non-carrier feed the shared ball into the
            // model's Pivot/Momentum controls and visibly pull the hands toward it.
            return intent;
        }

        private void ApplyActiveReceiveIntent(
            BasketballTeamMember member,
            ref BasketballIntent intent)
        {
            if (!possessionManager.IsIntendedReceiver(member))
            {
                return;
            }

            BasketballIntent receiverIntent = BuildStandbyIntent(member);
            intent.CallForPass = false;
            intent.Steal = false;
            intent.CatchReady = receiverIntent.CatchReady;
            intent.Hold = receiverIntent.Hold;
            intent.UseWorldMove = receiverIntent.UseWorldMove;
            intent.WorldMove = receiverIntent.WorldMove;
            intent.UseWorldFacing = receiverIntent.UseWorldFacing;
            intent.WorldFacing = receiverIntent.WorldFacing;
        }

        private BasketballTeamMember FindBestPassTarget(BasketballTeamMember passer)
        {
            if (passer == null || viewCamera == null)
            {
                return null;
            }

            Vector3 origin = viewCamera.transform.position;
            Vector3 forward = viewCamera.transform.forward;
            float bestScore = passLockDot;
            BasketballTeamMember best = null;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (!IsValidPassTarget(passer, candidate))
                {
                    continue;
                }
                Vector3 direction = candidate.AimPoint - origin;
                float distance = direction.magnitude;
                if (distance <= 1e-5f)
                {
                    continue;
                }
                float score = Vector3.Dot(forward, direction / distance) - 0.0025f * distance;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private void ApplyActivePlayer()
        {
            BasketballTeamMember active = ActivePlayer;
            if (active == null)
            {
                return;
            }
            if (!freeCameraMode)
            {
                orbitCamera?.SetTarget(active.transform);
            }
            if (hud != null)
            {
                hud.SetSources(active.Controller, active.InputProvider, active.Visualizer);
                hud.SetFreeCameraMode(freeCameraMode);
            }
            hudStatusKey = int.MinValue;
            UpdateHudStatus();
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index].Visualizer != null)
                {
                    players[index].Visualizer.SetVisible(
                        !freeCameraMode && index == activePlayerIndex);
                }
            }
        }

        private void UpdateIndicators()
        {
            if (players == null)
            {
                return;
            }
            BasketballTeamMember indicatorTarget = passTarget != null
                ? passTarget
                : lockedTarget;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null || member.Indicator == null)
                {
                    continue;
                }
                if (!member.IsOnCourt)
                {
                    member.Indicator.SetState(BasketballTargetIndicatorState.Hidden);
                    continue;
                }
                BasketballTargetIndicatorState indicatorState = freeCameraMode
                    ? BasketballTargetIndicatorState.Hidden
                    : member == indicatorTarget
                    ? BasketballTargetIndicatorState.PassLocked
                    : index == activePlayerIndex
                        ? BasketballTargetIndicatorState.ActivePlayer
                        : BasketballTargetIndicatorState.Hidden;
                member.Indicator.SetState(indicatorState);
            }
        }

        private void UpdateHudStatus()
        {
            BasketballTeamMember active = ActivePlayer;
            if (hud == null || active == null)
            {
                return;
            }

            BasketballTeamMember target = passTarget != null ? passTarget : lockedTarget;
            int targetIndex = target != null ? target.PlayerIndex : -1;
            int state;
            if (freeCameraMode)
            {
                state = 11;
            }
            else if (scoreRestartPending)
            {
                state = 10;
            }
            else if (passCommitted)
            {
                state = 4;
            }
            else if (shotAttempt)
            {
                state = shotCommitted ? 9 : 8;
            }
            else if (IsTargetSelectionActive)
            {
                state = lockedTarget != null ? 2 : 1;
            }
            else if (callForPassActive)
            {
                state = 12;
            }
            else if (Owner == active)
            {
                state = 0;
            }
            else if (Owner == null)
            {
                state = 5;
            }
            else
            {
                state = Owner.TeamId == active.TeamId ? 6 : 7;
            }

            int teamZeroScore = scoreTracker != null ? scoreTracker.TeamZeroScore : 0;
            int teamOneScore = scoreTracker != null ? scoreTracker.TeamOneScore : 0;
            int shotClockSecond = rulesManager != null
                ? Mathf.CeilToInt(rulesManager.ShotClockRemaining)
                : -1;
            bool showViolation = rulesManager != null &&
                                 Time.time - rulesManager.LastViolationTime <= 2.25f;
            int violationKey = showViolation ? (int)rulesManager.LastViolation : 0;
            int key = active.PlayerIndex * 10000 + active.TeamId * 1000 +
                      (targetIndex + 1) * 10 + state;
            key = unchecked(key * 397 ^ teamZeroScore * 31 ^ teamOneScore);
            key = unchecked(key * 397 ^ shotClockSecond * 31 ^ violationKey);
            if (key == hudStatusKey)
            {
                return;
            }
            hudStatusKey = key;

            string prefix = $"A {teamZeroScore} : {teamOneScore} B  •  " +
                            $"P{active.PlayerIndex + 1} / TEAM {(char)('A' + active.TeamId)}" +
                            (shotClockSecond >= 0 ? $"  •  {shotClockSecond}" : string.Empty);
            string suffix = showViolation
                ? FormatViolation(rulesManager.LastViolation)
                : state switch
            {
                0 => "BALL",
                1 => "FIND TEAMMATE",
                2 => $"LOCK P{targetIndex + 1}",
                3 => $"PASS P{targetIndex + 1}",
                4 => $"PASS P{targetIndex + 1}",
                5 => "LOOSE BALL",
                6 => "OFF BALL",
                8 => "AIM SHOT",
                9 => "SHOOT",
                10 => $"SCORE +{pendingScore.Points}",
                11 => "FREE CAMERA / ALL AI",
                12 => "CALL FOR PASS",
                _ => "DEFENSE"
            };
            hud.SetMatchStatus($"{prefix}  •  {suffix}");
        }

        private static string FormatViolation(BasketballViolationType violation)
        {
            return violation switch
            {
                BasketballViolationType.OutOfBounds => "OUT OF BOUNDS / 出界",
                BasketballViolationType.ShotClock => "SHOT CLOCK / 進攻逾時",
                BasketballViolationType.BackcourtEightSeconds => "8 SECONDS / 八秒",
                BasketballViolationType.BackcourtReturn => "BACKCOURT / 回場",
                BasketballViolationType.OffensiveThreeSeconds => "3 SECONDS / 進攻三秒",
                BasketballViolationType.Traveling => "TRAVEL / 走步",
                BasketballViolationType.DoubleDribble => "DOUBLE DRIBBLE / 二次運球",
                _ => "VIOLATION / 違例"
            };
        }

        private void BindScoreTracker()
        {
            BasketballScoreTracker next = court != null
                ? court.GetComponent<BasketballScoreTracker>()
                : null;
            if (scoreTracker == next)
            {
                return;
            }
            if (scoreTracker != null)
            {
                scoreTracker.Scored -= HandleScore;
            }
            scoreTracker = next;
            if (scoreTracker != null)
            {
                scoreTracker.Scored += HandleScore;
            }
        }

        private void HandleScore(BasketballScoreEvent scoreEvent)
        {
            possessionManager?.NotifyShotScored();
            BasketballTeamMember scorer = FindMemberByPlayerIndex(scoreEvent.PlayerIndex);
            worldEventStream?.Publish(
                BasketballWorldEventType.ShotMade,
                possessionManager != null ? possessionManager.PossessionVersion : -1,
                scorer,
                position: scoreEvent.ReleasePosition,
                points: scoreEvent.Points,
                targetPosition: scoreEvent.Hoop != null
                    ? scoreEvent.Hoop.AimPoint
                    : Vector3.zero);
            pendingScore = scoreEvent;
            scoreRestartPending = true;
            scoreRestartAt = Time.time + scoreRestartDelay;
            CancelPass();
            CancelShot();
            hudStatusKey = int.MinValue;
            UpdateHudStatus();
        }

        private void UpdateScoreRestart()
        {
            if (!scoreRestartPending || Time.time < scoreRestartAt)
            {
                return;
            }

            int inboundTeam = pendingScore.TeamId == 0 ? 1 : 0;
            BasketballTeamMember inbounder = FindClosestTeamMember(
                inboundTeam,
                pendingScore.Hoop != null
                    ? pendingScore.Hoop.Center
                    : Vector3.zero);
            if (inbounder == null || inbounder.Controller?.State == null)
            {
                Debug.LogError(
                    $"No valid Team {inboundTeam} player is available for the inbound restart.",
                    this);
                scoreRestartPending = false;
                pendingScore = default;
                return;
            }

            BasketballAgentState state = inbounder.Controller.State;
            Vector3 forward = state.ActorRootRotation * Vector3.forward;
            Vector3 inboundPosition = state.BonePositions[14] +
                                      0.2f * forward -
                                      0.05f * Vector3.up;
            if (!possessionManager.ResetPossession(
                    inbounder,
                    inboundPosition,
                    ball.transform.rotation))
            {
                Debug.LogError(
                    $"Could not restart possession for Player {inbounder.PlayerIndex + 1}.",
                    this);
                scoreRestartPending = false;
                pendingScore = default;
                return;
            }

            worldEventStream?.Publish(
                BasketballWorldEventType.MatchRestarted,
                possessionManager.PossessionVersion,
                inbounder,
                position: inboundPosition);
            rulesManager?.ResetRuleState();

            scoreRestartPending = false;
            pendingScore = default;
            hudStatusKey = int.MinValue;
        }

        private BasketballTeamMember FindClosestTeamMember(
            int teamId,
            Vector3 position)
        {
            BasketballTeamMember best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember candidate = players[index];
                if (candidate == null || !candidate.IsOnCourt ||
                    candidate.TeamId != teamId ||
                    candidate.Controller?.State == null)
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

        private BasketballTeamMember FindMember(BasketballNeuralController controller)
        {
            if (players == null)
            {
                return null;
            }
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index] != null && players[index].Controller == controller)
                {
                    return players[index];
                }
            }
            return null;
        }

        private BasketballTeamMember FindMemberByPlayerIndex(int playerIndex)
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

        private BasketballTeamMember GetPlayer(int index)
        {
            return players != null && index >= 0 && index < players.Length
                ? players[index]
                : null;
        }

        private int FindNextOnCourtPlayer(int afterIndex)
        {
            if (players == null || players.Length == 0)
            {
                return -1;
            }
            for (int offset = 1; offset <= players.Length; offset++)
            {
                int index = (afterIndex + offset + players.Length) % players.Length;
                if (players[index] != null && players[index].IsOnCourt)
                {
                    return index;
                }
            }
            return -1;
        }

        private void ApplyMatchMode()
        {
            int homeLimit = matchMode == BasketballMatchMode.Custom
                ? customHomePlayers
                : (int)matchMode;
            int awayLimit = matchMode == BasketballMatchMode.Custom
                ? customAwayPlayers
                : (int)matchMode;
            int homeCount = 0;
            int awayCount = 0;
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember member = players[index];
                if (member == null)
                {
                    continue;
                }
                bool onCourt;
                if (member.TeamId == 0)
                {
                    onCourt = homeCount < homeLimit;
                    homeCount++;
                }
                else if (member.TeamId == 1)
                {
                    onCourt = awayCount < awayLimit;
                    awayCount++;
                }
                else
                {
                    onCourt = false;
                }
                member.SetOnCourt(onCourt);
            }
        }

        private void EnsureSentisBatchScheduler()
        {
            if (players == null ||
                players.Length != BasketballSentisBatchScheduler.BatchSize)
            {
                return;
            }

            sentisBatchScheduler = sentisBatchScheduler != null
                ? sentisBatchScheduler
                : GetComponent<BasketballSentisBatchScheduler>();
            if (sentisBatchScheduler == null)
            {
                sentisBatchScheduler = gameObject.AddComponent<BasketballSentisBatchScheduler>();
            }
            sentisBatchScheduler.ConfigureRuntime(players);
        }

        private void EnsureRuleBasedTeamAI()
        {
            if (decisionPolicyBehaviour is IBasketballDecisionPolicy configuredPolicy)
            {
                decisionPolicy = configuredPolicy;
                if (decisionPolicyBehaviour is BasketballRuleBasedTeamAI configuredRuleAI)
                {
                    ruleBasedTeamAI = configuredRuleAI;
                    configuredRuleAI.ConfigureRuntime(players, possessionManager, ball, court);
                }
                return;
            }
            if (decisionPolicyBehaviour != null)
            {
                Debug.LogWarning(
                    $"{decisionPolicyBehaviour.GetType().Name} does not implement " +
                    $"{nameof(IBasketballDecisionPolicy)}; using the built-in rule AI.",
                    decisionPolicyBehaviour);
            }
            ruleBasedTeamAI = ruleBasedTeamAI != null
                ? ruleBasedTeamAI
                : GetComponent<BasketballRuleBasedTeamAI>();
            if (ruleBasedTeamAI == null)
            {
                ruleBasedTeamAI = gameObject.AddComponent<BasketballRuleBasedTeamAI>();
            }
            ruleBasedTeamAI.ConfigureRuntime(players, possessionManager, ball, court);
            decisionPolicyBehaviour = ruleBasedTeamAI;
            decisionPolicy = ruleBasedTeamAI;
        }

        private void EnsureWorldEventStream()
        {
            worldEventStream = worldEventStream != null
                ? worldEventStream
                : GetComponent<BasketballWorldEventStream>();
            if (worldEventStream == null)
            {
                worldEventStream = gameObject.AddComponent<BasketballWorldEventStream>();
            }
        }

        private void EnsureRewardTracker()
        {
            rewardTracker = rewardTracker != null
                ? rewardTracker
                : GetComponent<BasketballRewardTracker>();
            if (rewardTracker == null)
            {
                rewardTracker = gameObject.AddComponent<BasketballRewardTracker>();
            }
            rewardTracker.ConfigureRuntime(worldEventStream, players);
        }

        private void EnsureRulesManager()
        {
            rulesManager = rulesManager != null
                ? rulesManager
                : GetComponent<BasketballRulesManager>();
            if (rulesManager == null)
            {
                rulesManager = gameObject.AddComponent<BasketballRulesManager>();
            }
            rulesManager.ConfigureRuntime(
                players,
                ball,
                possessionManager,
                court,
                worldEventStream);
        }

        private void EnsureTelemetryRecorder()
        {
            telemetryRecorder = telemetryRecorder != null
                ? telemetryRecorder
                : GetComponent<BasketballSkillTelemetryRecorder>();
            if (telemetryRecorder == null)
            {
                telemetryRecorder =
                    gameObject.AddComponent<BasketballSkillTelemetryRecorder>();
            }
            telemetryRecorder.SetEventStream(worldEventStream);
            telemetryRecorder.SetRewardTracker(rewardTracker);
        }

        private void CaptureWorldObservation()
        {
            if (players == null || ball == null || possessionManager == null)
            {
                return;
            }
            if (worldObservation == null || worldObservation.PlayerCapacity != players.Length)
            {
                worldObservation = new BasketballWorldObservation(players.Length);
            }
            worldObservation.Capture(
                players,
                ball,
                possessionManager,
                court,
                scoreTracker,
                matchMode);
            DispatchPolicyWorldEvents();
            decisionPolicy?.BeginDecisionFrame(worldObservation);
            telemetryRecorder?.CaptureObservation(worldObservation);
        }

        private void DispatchPolicyWorldEvents()
        {
            if (worldEventStream == null)
            {
                return;
            }

            int latestSequence = worldEventStream.LatestSequence;
            if (decisionPolicy is not IBasketballWorldEventObserver observer)
            {
                lastPolicyEventSequence = latestSequence;
                return;
            }

            int nextSequence = Mathf.Max(
                lastPolicyEventSequence + 1,
                worldEventStream.OldestSequence);
            for (; nextSequence <= latestSequence; nextSequence++)
            {
                if (worldEventStream.TryGetBySequence(
                        nextSequence,
                        out BasketballWorldEvent worldEvent))
                {
                    observer.ObserveEvent(worldEvent);
                }
            }
            lastPolicyEventSequence = latestSequence;
        }

        private void CancelPass()
        {
            if (passCommitted)
            {
                possessionManager?.CancelPassPreparation(ActivePlayer);
            }
            passTarget = null;
            passCommitted = false;
            passCommittedSeconds = 0f;
        }

        private void CancelShot()
        {
            shotAttempt = false;
            shotCommitted = false;
            shotElapsed = 0f;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballTeamMember[] teamMembers,
            BasketballBallController sharedBall,
            BasketballPossessionManager possession,
            Camera camera,
            ThirdPersonOrbitCamera orbit,
            BasketballUIToolkitController ui,
            BasketballRuntimeSettings settings,
            BasketballCourt basketballCourt = null)
        {
            players = teamMembers;
            ball = sharedBall;
            possessionManager = possession;
            viewCamera = camera;
            orbitCamera = orbit;
            hud = ui;
            runtimeSettings = settings;
            court = basketballCourt;
            passLockDot = 0.82f;
            passTimeoutSeconds = 1.5f;
            scoreRestartDelay = 1.25f;
            matchMode = BasketballMatchMode.FiveOnFive;
            controlMode = BasketballMatchControlMode.SelectedPlayerWithRuleAI;
            customHomePlayers = 5;
            customAwayPlayers = 5;
        }
#endif
    }
}
