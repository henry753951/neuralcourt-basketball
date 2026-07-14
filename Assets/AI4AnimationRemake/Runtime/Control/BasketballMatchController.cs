using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BasketballMatchController : MonoBehaviour
    {
        [SerializeField] private BasketballTeamMember[] players;
        [SerializeField] private BasketballBallController ball;
        [SerializeField] private BasketballPossessionManager possessionManager;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private ThirdPersonOrbitCamera orbitCamera;
        [SerializeField] private BasketballUIToolkitController hud;
        [SerializeField, Range(0.5f, 0.999f)] private float passLockDot = 0.82f;
        [SerializeField, Min(0.05f)] private float passCommitSeconds = 0.2f;
        [SerializeField, Min(0.25f)] private float passTimeoutSeconds = 1.5f;
        [SerializeField, Min(0.05f)] private float passFakeDuration = 0.18f;

        private int activePlayerIndex;
        private BasketballTeamMember lockedTarget;
        private BasketballTeamMember passTarget;
        private float passHeldSeconds;
        private float passCommittedSeconds;
        private float passFakeElapsed;
        private bool passAttempt;
        private bool passCommitted;
        private bool passFake;
        private bool initialized;
        private int hudStatusKey = int.MinValue;

        public int PlayerCount => players == null ? 0 : players.Length;
        public int ActivePlayerIndex => activePlayerIndex;
        public BasketballTeamMember ActivePlayer => GetPlayer(activePlayerIndex);
        public BasketballTeamMember Owner => possessionManager != null
            ? possessionManager.Owner
            : null;
        public BasketballPossessionManager PossessionManager => possessionManager;
        public BasketballTeamMember LockedTarget => lockedTarget;
        public bool IsTargetSelectionActive { get; private set; }
        public bool IsPassCommitted => passCommitted;
        public bool IsPassFakeActive => passFake;

        private void Start()
        {
            InitializeMatch();
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

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                SelectPlayer((activePlayerIndex + 1) % players.Length);
            }

            RouteIntents(keyboard);
            UpdateIndicators();
            UpdateHudStatus();
        }

        public void InitializeMatch()
        {
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

            activePlayerIndex = 0;
            possessionManager.Initialize();
            initialized = true;
            ApplyActivePlayer();
            UpdateIndicators();
        }

        public void SelectPlayer(int index)
        {
            if (!initialized || players == null || players.Length == 0)
            {
                return;
            }
            int normalized = ((index % players.Length) + players.Length) % players.Length;
            if (normalized == activePlayerIndex)
            {
                return;
            }
            CancelPass();
            activePlayerIndex = normalized;
            ApplyActivePlayer();
            UpdateIndicators();
        }

        public bool IsValidPassTarget(
            BasketballTeamMember passer,
            BasketballTeamMember target)
        {
            return passer != null && target != null && passer != target &&
                   passer.TeamId == target.TeamId;
        }

        private void RouteIntents(Keyboard keyboard)
        {
            BasketballTeamMember active = ActivePlayer;
            Mouse mouse = Mouse.current;
            bool controlHeld = keyboard != null &&
                               (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool shootHeld = keyboard != null && keyboard.spaceKey.isPressed;
            bool pointerAvailable = orbitCamera == null || orbitCamera.CursorLocked;
            bool primaryHeld = pointerAvailable && mouse != null && mouse.leftButton.isPressed;
            bool primaryPressed = pointerAvailable && mouse != null &&
                                  mouse.leftButton.wasPressedThisFrame;
            bool activeOwnsBall = active != null && Owner == active &&
                                  possessionManager.HasBall(active.Controller);

            IsTargetSelectionActive = activeOwnsBall && controlHeld && !shootHeld &&
                                      !passCommitted && !passFake;
            lockedTarget = IsTargetSelectionActive ? FindBestPassTarget(active) : null;

            if (IsTargetSelectionActive && lockedTarget != null &&
                (primaryHeld || primaryPressed) && !passAttempt)
            {
                passAttempt = true;
                passTarget = lockedTarget;
                passHeldSeconds = 0f;
            }

            if (passAttempt && !passCommitted)
            {
                if (primaryHeld && controlHeld && IsValidPassTarget(active, passTarget))
                {
                    passHeldSeconds += Time.unscaledDeltaTime;
                    if (passHeldSeconds >= passCommitSeconds)
                    {
                        passCommitted = possessionManager.RequestPass(active, passTarget);
                        if (passCommitted)
                        {
                            passCommittedSeconds = 0f;
                        }
                    }
                }
                else
                {
                    // Latch a short Pivot/Hold pass fake long enough to cross several 30 Hz
                    // neural ticks. CommitBallRelease remains false, so a short render-frame
                    // click can never hand the ball to physics.
                    passAttempt = false;
                    passFake = IsValidPassTarget(active, passTarget);
                    passFakeElapsed = 0f;
                    if (!passFake)
                    {
                        CancelPass();
                    }
                }
            }

            if (passFake)
            {
                passFakeElapsed += Time.unscaledDeltaTime;
                if (passFakeElapsed >= passFakeDuration || Owner != active)
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
                BasketballIntent intent;
                if (index == activePlayerIndex)
                {
                    intent = member.InputProvider != null
                        ? member.InputProvider.ReadIntent()
                        : default;
                    bool canAttemptSteal = Owner == null ||
                                           (Owner != member && Owner.TeamId != member.TeamId);
                    intent.Steal = primaryHeld && canAttemptSteal;
                    ApplyActivePassIntent(member, ref intent);
                }
                else
                {
                    intent = BuildStandbyIntent(member);
                }
                member.Controller.SetIntentOverride(intent);
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
                if (passAttempt || passFake)
                {
                    CancelPass();
                }
                intent.Hold = false;
                intent.Steal = false;
                return;
            }

            bool fakeAnimation = (passAttempt || passFake) &&
                                 IsValidPassTarget(active, target);
            if (fakeAnimation)
            {
                float fakeProgress = Mathf.Clamp01(passFakeElapsed /
                    Mathf.Max(passFakeDuration, 0.01f));
                intent.Hold = false;
                intent.Shoot = false;
                intent.PassTargeting = true;
                intent.PassControl = true;
                intent.PassHoldStyle = Mathf.Lerp(0.9f, 0.65f, fakeProgress);
                intent.PassShootStyle = 0f;
                intent.UseWorldFacing = true;
                intent.WorldFacing = target.AimPoint - active.transform.position;
                active.InputProvider?.SetExternalControlVisualization(Vector2.zero, false);
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
                              (passContested || ballDistance <= 2.4f ||
                               timeToArrival <= 0.5f);
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
            orbitCamera?.SetTarget(active.transform);
            if (hud != null)
            {
                hud.SetSources(active.Controller, active.InputProvider, active.Visualizer);
            }
            hudStatusKey = int.MinValue;
            UpdateHudStatus();
            for (int index = 0; index < players.Length; index++)
            {
                if (players[index].Visualizer != null)
                {
                    players[index].Visualizer.SetVisible(index == activePlayerIndex);
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
                BasketballTargetIndicatorState indicatorState = member == indicatorTarget
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
            if (passCommitted)
            {
                state = 4;
            }
            else if (passAttempt || passFake)
            {
                state = 3;
            }
            else if (IsTargetSelectionActive)
            {
                state = lockedTarget != null ? 2 : 1;
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

            int key = active.PlayerIndex * 10000 + active.TeamId * 1000 +
                      (targetIndex + 1) * 10 + state;
            if (key == hudStatusKey)
            {
                return;
            }
            hudStatusKey = key;

            string prefix = $"P{active.PlayerIndex + 1} / TEAM {(char)('A' + active.TeamId)}";
            string suffix = state switch
            {
                0 => "BALL",
                1 => "FIND TEAMMATE",
                2 => $"LOCK P{targetIndex + 1}",
                3 => $"FAKE P{targetIndex + 1}",
                4 => $"PASS P{targetIndex + 1}",
                5 => "LOOSE BALL",
                6 => "OFF BALL",
                _ => "DEFENSE"
            };
            hud.SetMatchStatus($"{prefix}  •  {suffix}");
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

        private BasketballTeamMember GetPlayer(int index)
        {
            return players != null && index >= 0 && index < players.Length
                ? players[index]
                : null;
        }

        private void CancelPass()
        {
            if (passCommitted)
            {
                possessionManager?.CancelPassPreparation(ActivePlayer);
            }
            passTarget = null;
            passAttempt = false;
            passCommitted = false;
            passFake = false;
            passHeldSeconds = 0f;
            passCommittedSeconds = 0f;
            passFakeElapsed = 0f;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballTeamMember[] teamMembers,
            BasketballBallController sharedBall,
            BasketballPossessionManager possession,
            Camera camera,
            ThirdPersonOrbitCamera orbit,
            BasketballUIToolkitController ui)
        {
            players = teamMembers;
            ball = sharedBall;
            possessionManager = possession;
            viewCamera = camera;
            orbitCamera = orbit;
            hud = ui;
            passLockDot = 0.82f;
            passCommitSeconds = 0.2f;
            passTimeoutSeconds = 1.5f;
            passFakeDuration = 0.18f;
        }
#endif
    }
}
