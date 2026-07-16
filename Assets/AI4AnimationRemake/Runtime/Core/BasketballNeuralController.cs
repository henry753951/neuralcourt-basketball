using System;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BasketballReferenceRig), typeof(BasketballKeyboardMouseInputProvider))]
    public sealed class BasketballNeuralController : MonoBehaviour
    {
        private const float WalkFactor = 3.75f;
        private const float SprintFactor = 2.5f;
        private static readonly ProfilerMarker ControlMarker = new("Basketball.Control");
        private static readonly ProfilerMarker BallMarker = new("Basketball.Ball");

        [SerializeField] private BasketballReferenceRig rig;
        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private Camera movementCamera;
        [SerializeField] private LayerMask collisionMask = 4097;

        private readonly float[] input = new float[BasketballModelContract.InputFeatureCount];
        private readonly float[] output = new float[BasketballModelContract.OutputFeatureCount];
        private readonly float[] externalGatingWeights =
            new float[BasketballModelContract.ExpertCount];
        private readonly BasketballFeatureBuilder featureBuilder = new();
        private readonly BasketballOutputDecoder outputDecoder = new();
        private readonly BasketballPoseBuffer previousPose = new();
        private readonly BasketballPoseBuffer currentPose = new();

        private BasketballAgentState state;
        private BasketballPoseApplicator poseApplicator;
        private BasketballTwistCorrector twistCorrector;
        private BasketballRootCollisionResolver collisionResolver;
        private BasketballPlayerBodyContact playerBodyContact;
        private BasketballLegacyContactIK contactIK;
        private ThirdPersonOrbitCamera orbitCamera;
        private BasketballIntent intent;
        private bool intentOverride;
        private int reacquireTicksRemaining;
        private bool initialized;
        private IBasketballPossessionAuthority possessionAuthority;
        private Transform passTarget;
        private Vector3 passTargetOffset;
        private BasketballSentisBatchScheduler externalBatchScheduler;
        private float externalInterpolationAlpha = 1f;

        public bool IsInitialized => initialized;
        public BasketballRuntimeSettings RuntimeSettings => rig != null
            ? rig.RuntimeSettings
            : BasketballRuntimeSettings.LoadDefault();
        public int NeuralTickRate => rig != null
            ? rig.NeuralTickRate
            : BasketballRuntimeSettings.CanonicalNeuralTickRate;
        public int SimulationTickCount => state?.TickCount ?? 0;
        public BasketballAgentState State => state;
        public bool IsCarrier => state != null && state.Carrier;
        public string InferenceBackendName => externalBatchScheduler != null
            ? externalBatchScheduler.BackendName
            : "GPU COMPUTE WAITING";
        public float InferenceRoundTripMilliseconds => externalBatchScheduler != null
            ? externalBatchScheduler.LastRoundTripMilliseconds
            : -1f;
        public bool IsInferencePending => externalBatchScheduler != null &&
                                          externalBatchScheduler.IsReadbackPending;
        public BasketballIntent CurrentIntent => intent;
        public BasketballBallAuthorityState BallState =>
            rig != null && rig.Ball != null ? rig.Ball.State : BasketballBallAuthorityState.FreePhysics;

        private void Awake() => Initialize();

        private void OnEnable()
        {
            // Unity can restore a playing scene after a script-domain reload while
            // non-serialized runtime buffers are empty. Rebuild them before Update.
            if (Application.isPlaying &&
                (state == null || poseApplicator == null))
            {
                initialized = false;
                Initialize();
            }
        }

        private void Update()
        {
            if (!initialized || state == null)
            {
                return;
            }

            if (!intentOverride)
            {
                intent = inputProvider.ReadIntent();
            }
        }

        private void LateUpdate()
        {
            if (!initialized || state == null || poseApplicator == null)
            {
                return;
            }
            float alpha = rig.RenderInterpolation ? externalInterpolationAlpha : 1f;
            if (rig.RuntimeSettings != null)
            {
                alpha = rig.RuntimeSettings.ShapeInterpolationAlpha(alpha);
            }
            bool applyBall = possessionAuthority != null
                ? possessionAuthority.CanWriteBall(this)
                : state.Carrier;
            poseApplicator.Apply(previousPose, currentPose, alpha, applyBall);
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }
            rig = rig != null ? rig : GetComponent<BasketballReferenceRig>();
            inputProvider = inputProvider != null
                ? inputProvider
                : GetComponent<BasketballKeyboardMouseInputProvider>();
            string reason = string.Empty;
            if (rig == null || !rig.Validate(out reason))
            {
                throw new InvalidOperationException($"Basketball reference rig is invalid: {reason}");
            }

            state = new BasketballAgentState();
            state.Initialize(transform, rig.Skeleton, rig.Ball);
            twistCorrector = new BasketballTwistCorrector();
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            collisionResolver = new BasketballRootCollisionResolver(
                capsule != null ? capsule.radius : 0.25f,
                collisionMask);
            playerBodyContact = GetComponent<BasketballPlayerBodyContact>();
            previousPose.Capture(state);
            currentPose.Capture(state);
            poseApplicator = new BasketballPoseApplicator(transform, rig.Skeleton, rig.Ball);
            contactIK = new BasketballLegacyContactIK(transform, rig.Skeleton);
            movementCamera = movementCamera != null ? movementCamera : Camera.main;
            orbitCamera = movementCamera != null
                ? movementCamera.GetComponent<ThirdPersonOrbitCamera>()
                : null;
            initialized = true;
        }

        /// <summary>
        /// Re-seeds this agent's closed-loop simulation at a deterministic root
        /// pose. The shared GPU model and scheduler stay alive; only per-agent
        /// recurrent state and post-processing caches are reset.
        /// </summary>
        public bool ResetSimulationPose(Vector3 rootPosition, Quaternion rootRotation)
        {
            Initialize();
            if (!initialized || state == null || rig == null || rig.Ball == null)
            {
                return false;
            }

            transform.SetPositionAndRotation(rootPosition, rootRotation);
            Array.Clear(input, 0, input.Length);
            Array.Clear(output, 0, output.Length);
            Array.Clear(externalGatingWeights, 0, externalGatingWeights.Length);
            state.Initialize(transform, rig.Skeleton, rig.Ball);

            intent = default;
            intentOverride = true;
            reacquireTicksRemaining = 0;
            passTarget = null;
            passTargetOffset = Vector3.zero;
            externalInterpolationAlpha = 1f;

            // Contact locks and collision/twist helpers contain pose-relative
            // caches. Rebuilding them prevents old-world anchors from pulling a
            // newly teleported character back toward its previous formation.
            twistCorrector = new BasketballTwistCorrector();
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            collisionResolver = new BasketballRootCollisionResolver(
                capsule != null ? capsule.radius : 0.25f,
                collisionMask);
            playerBodyContact = GetComponent<BasketballPlayerBodyContact>();
            contactIK = new BasketballLegacyContactIK(transform, rig.Skeleton);

            previousPose.Capture(state);
            currentPose.Capture(state);
            poseApplicator.ApplySimulationState(state);
            return true;
        }

        internal void SetRuntimeSettings(BasketballRuntimeSettings value)
        {
            rig = rig != null ? rig : GetComponent<BasketballReferenceRig>();
            rig?.SetRuntimeSettings(value);
        }

        internal bool PrepareExternalTick(Span<float> destination)
        {
            if (externalBatchScheduler == null)
            {
                throw new InvalidOperationException(
                    "The controller is not registered with a Sentis batch scheduler.");
            }
            if (destination.Length < input.Length)
            {
                throw new ArgumentException(
                    $"Expected at least {input.Length} destination floats.",
                    nameof(destination));
            }
            if (!PrepareTick())
            {
                return false;
            }
            input.AsSpan().CopyTo(destination);
            return true;
        }

        internal void CompleteExternalTick(
            ReadOnlySpan<float> neuralOutput,
            ReadOnlySpan<float> gatingWeights)
        {
            if (neuralOutput.Length < output.Length)
            {
                throw new ArgumentException(
                    $"Expected at least {output.Length} neural output floats.",
                    nameof(neuralOutput));
            }
            neuralOutput.Slice(0, output.Length).CopyTo(output);
            int gatingCount = Mathf.Min(gatingWeights.Length, externalGatingWeights.Length);
            gatingWeights.Slice(0, gatingCount).CopyTo(externalGatingWeights);
            if (gatingCount < externalGatingWeights.Length)
            {
                externalGatingWeights.AsSpan(gatingCount).Clear();
            }
            CompleteTick(output);
        }

        internal void SetExternalBatchScheduler(BasketballSentisBatchScheduler scheduler)
        {
            externalBatchScheduler = scheduler;
            externalInterpolationAlpha = scheduler != null ? 0f : 1f;
            if (scheduler == null)
            {
                Array.Clear(externalGatingWeights, 0, externalGatingWeights.Length);
            }
        }

        internal void SetExternalInterpolationAlpha(float value)
        {
            externalInterpolationAlpha = Mathf.Clamp01(value);
        }

        private bool PrepareTick()
        {
            previousPose.Capture(state);
            ApplyControl();
            collisionResolver.Resolve(state);
            featureBuilder.Build(state, input);
            if (ValidateFinite(input, "input", out int invalidInput))
            {
                return true;
            }
            FailNonFinite("input", invalidInput);
            return false;
        }

        private void CompleteTick(float[] values)
        {
            if (!ValidateFinite(values, "output", out int invalidOutput))
            {
                FailNonFinite("output", invalidOutput);
                return;
            }
            outputDecoder.Decode(state, values);
            // The SIGGRAPH 2020 controller corrects single-child bone twist
            // unconditionally, before any optional contact IK pass.
            twistCorrector.Correct(state);
            collisionResolver.ResolveAfterDecode(state);
            playerBodyContact?.ResolveAfterDecode(state);
            ProcessBallAfterDecode();
            possessionAuthority?.ReportNeuralTick(this, BuildBallObservation());
            if (rig.EnableContactIK)
            {
                poseApplicator.ApplySimulationState(state);
                contactIK.Apply(state);
            }
            currentPose.Capture(state);
        }

        public void SetIntentOverride(BasketballIntent value)
        {
            intent = value;
            intentOverride = true;
        }

        public void ClearIntentOverride()
        {
            intentOverride = false;
            intent = default;
        }

        public void SetPossessionAuthority(IBasketballPossessionAuthority authority)
        {
            possessionAuthority = authority;
        }

        public void SetRival(BasketballNeuralController rival)
        {
            state.Rival = rival != null ? rival.State : null;
        }

        public void SetCarrier(bool value, bool reacquire = true)
        {
            if (!initialized || state == null || rig == null || rig.Ball == null)
            {
                return;
            }

            if (state.Carrier == value)
            {
                return;
            }

            state.Carrier = value;
            reacquireTicksRemaining = 0;
            if (value)
            {
                int pivot = BasketballAgentState.Pivot;
                state.BallPositions[pivot] = rig.Ball.transform.position;
                state.BallRotations[pivot] = rig.Ball.transform.rotation;
                state.BallVelocities[pivot] = rig.Ball.Velocity;
                if (possessionAuthority != null)
                {
                    // Ball authority belongs to the central possession manager.
                }
                else if (reacquire)
                {
                    reacquireTicksRemaining = 6;
                    rig.Ball.BeginReacquire();
                }
                else
                {
                    rig.Ball.SetState(BasketballBallAuthorityState.Controlled);
                }
                previousPose.CaptureBall(state);
                currentPose.CaptureBall(state);
            }
        }

        public void SetPassTarget(Transform target, Vector3 localOffset)
        {
            passTarget = target;
            passTargetOffset = localOffset;
        }

        public void ClearPassTarget()
        {
            passTarget = null;
            passTargetOffset = Vector3.zero;
        }

        public void CopyGatingWeights(float[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (externalBatchScheduler != null)
            {
                int count = Mathf.Min(destination.Length, externalGatingWeights.Length);
                externalGatingWeights.AsSpan(0, count).CopyTo(destination);
                if (destination.Length > count)
                {
                    destination.AsSpan(count).Clear();
                }
                return;
            }
            Array.Clear(destination, 0, destination.Length);
        }

        private void ApplyControl()
        {
            using (ControlMarker.Auto())
            {
                if (possessionAuthority != null)
                {
                    state.Carrier = possessionAuthority.HasBall(this);
                }
                state.ShiftControlSeries();
                int pivot = BasketballAgentState.Pivot;
                ProcessBallBeforeControl(pivot);
                bool hasWorldMove = intent.UseWorldMove &&
                                    Vector3.ProjectOnPlane(
                                        intent.WorldMove,
                                        Vector3.up).magnitude >= 0.25f;
                bool stand = hasWorldMove
                    ? false
                    : intent.Move.magnitude < 0.25f;
                bool passControl = state.Carrier && intent.PassControl;
                float stealReachDistance = RuntimeSettings != null
                    ? RuntimeSettings.StealReachAnimationDistance
                    : 1.55f;
                float stealRootDistance = !state.Carrier && intent.Steal
                    ? Vector3.Distance(
                        state.ActorRootPosition,
                        state.BallPositions[pivot])
                    : float.PositiveInfinity;
                // Feed the one real shared ball into the original Hold response
                // before contact. This is animation intent only: possession
                // authority still prevents this non-owner from writing the ball.
                // It gives the pretrained model time to reach/pat toward the ball
                // without inventing a visible proxy or teleporting ownership.
                bool stealReachHold = !state.Carrier && intent.Steal &&
                                      stealRootDistance <= stealReachDistance;
                bool directHold = intent.Hold || stealReachHold;
                bool catchReadyStyle = !state.Carrier && intent.CatchReady;
                bool modelHoldStyle = directHold || catchReadyStyle;
                float holdAction = passControl
                    ? Mathf.Clamp01(intent.PassHoldStyle)
                    : modelHoldStyle ? 1f : 0f;
                float shootAction = passControl
                    ? Mathf.Clamp01(intent.PassShootStyle)
                    : state.Carrier && intent.Shoot ? 1f : 0f;
                bool hold = !passControl && directHold;
                bool shoot = !passControl && state.Carrier && intent.Shoot;
                bool moveActive = !stand && !intent.Shoot;
                bool dribble = state.Carrier && !hold && !shoot && !passControl;
                float manualTurn = ShapeTurn(intent.Turn);
                float turn = manualTurn;
                float spin = moveActive ? intent.Spin : 0f;
                bool horizontalControl =
                    !state.Carrier && hold ||
                    state.Carrier && spin == 0f && intent.BallControl.magnitude > 0.1f;
                bool heightControl =
                    !state.Carrier && hold ||
                    state.Carrier && intent.BallHeight != 0f ||
                    state.Carrier && hold && horizontalControl;
                bool speedControl =
                    !state.Carrier && hold ||
                    state.Carrier && dribble && state.Pivots[pivot].y > 1.5f ||
                    state.Carrier && dribble && state.Momentums[pivot].y < 2.5f;
                state.HoldIntent = holdAction > 0.5f;
                state.ShootIntent = shootAction > 0.1f;
                state.BallHorizontalControl = horizontalControl;
                state.BallHeightControl = heightControl;
                state.BallSpeedControl = speedControl;
                state.MoveIntent = moveActive;

                Vector3 ballControl;
                if (state.Carrier)
                {
                    ballControl = new Vector3(intent.BallControl.x, 0f, intent.BallControl.y);
                }
                else
                {
                    Vector3 toBall = state.BallPositions[pivot] - state.ActorRootPosition;
                    toBall.y = 0f;
                    ballControl = BasketballMath.RelativeDirection(
                        toBall.normalized, state.RootRotations[pivot]);
                }
                float ballHeight = 0f;
                for (int sample = 0; sample < pivot; sample++)
                {
                    ballHeight += state.BallPositions[sample].y;
                }
                ballHeight /= pivot;
                float ballSpeed = Mathf.Abs(state.BallVelocities[pivot].y);
                if (state.Carrier && dribble)
                {
                    if (state.Pivots[pivot].y > 1.5f)
                    {
                        ballHeight -= 3f;
                    }
                    if (state.Momentums[pivot].y < 2.5f)
                    {
                        ballSpeed += 5f;
                    }
                }
                if (state.Carrier && hold && horizontalControl)
                {
                    ballControl = ToHoldTarget(ballControl);
                    ballHeight = ballControl.y;
                    ballSpeed = (ballHeight - state.BallPositions[pivot].y) *
                        BasketballAgentState.Framerate;
                }
                if (!intent.IsGamepad)
                {
                    ballControl = ballControl.magnitude > 0.1f
                        ? ballControl.normalized
                        : Vector3.zero;
                }

                Vector3 cameraDirection = Vector3.forward;
                if (movementCamera != null)
                {
                    cameraDirection = orbitCamera != null && orbitCamera.enabled
                        ? orbitCamera.PlanarForward
                        : Vector3.ProjectOnPlane(
                            state.ActorRootPosition - movementCamera.transform.position,
                            Vector3.up);
                }
                cameraDirection = cameraDirection.sqrMagnitude > 1e-10f
                    ? cameraDirection.normalized
                    : Vector3.forward;
                Quaternion cameraRotation = Quaternion.LookRotation(cameraDirection, Vector3.up);
                bool useOrbitAutoTurn =
                    !intent.IsGamepad && orbitCamera != null && orbitCamera.enabled;
                Vector3 moveInput = new(intent.Move.x, 0f, intent.Move.y);
                Vector3 move;
                Vector3 orbitFacing = Vector3.zero;
                bool useOrbitFacing = false;
                bool useWorldControl = intent.UseWorldMove || intent.UseWorldFacing;
                Vector3 worldMoveInput = intent.UseWorldMove
                    ? Vector3.ProjectOnPlane(intent.WorldMove, Vector3.up)
                    : Vector3.zero;
                if (useWorldControl)
                {
                    Vector3 desiredWorldMove = worldMoveInput;
                    Vector3 desiredFacing = intent.UseWorldFacing
                        ? Vector3.ProjectOnPlane(intent.WorldFacing, Vector3.up)
                        : desiredWorldMove;
                    if (desiredFacing.sqrMagnitude > 1e-10f)
                    {
                        Vector3 actorForward = state.ActorRootRotation * Vector3.forward;
                        float headingError = Vector3.SignedAngle(
                            actorForward,
                            desiredFacing.normalized,
                            Vector3.up);
                        turn = ShapeTurn(Mathf.Clamp(headingError / 90f, -1f, 1f));
                    }
                    move = BasketballMath.RelativeDirection(
                        Vector3.ClampMagnitude(desiredWorldMove, 1f),
                        state.ActorRootRotation);
                }
                else if (useOrbitAutoTurn)
                {
                    Vector3 desiredWorldMove = cameraRotation * moveInput;
                    if (manualTurn != 0f)
                    {
                        desiredWorldMove = Quaternion.AngleAxis(
                            60f * manualTurn, Vector3.up) * desiredWorldMove;
                    }
                    orbitFacing = intent.Sprint && moveActive &&
                                  desiredWorldMove.sqrMagnitude > 1e-10f
                        ? desiredWorldMove.normalized
                        : cameraDirection;
                    if (!intent.Sprint && manualTurn != 0f)
                    {
                        orbitFacing = Quaternion.AngleAxis(
                            60f * manualTurn,
                            Vector3.up) * orbitFacing;
                    }
                    useOrbitFacing = orbitFacing.sqrMagnitude > 1e-10f;
                    if (useOrbitFacing)
                    {
                        Vector3 actorForward = state.ActorRootRotation * Vector3.forward;
                        float headingError = Vector3.SignedAngle(
                            actorForward, orbitFacing, Vector3.up);
                        turn = ShapeTurn(Mathf.Clamp(headingError / 90f, -1f, 1f));
                    }
                    move = BasketballMath.RelativeDirection(
                        desiredWorldMove, state.ActorRootRotation);
                }
                else
                {
                    move = cameraRotation * moveInput;
                }
                move = Vector3.ClampMagnitude(move, 1f) * WalkFactor;
                bool sprintMoveActive = useWorldControl
                    ? worldMoveInput.magnitude > 0.25f
                    : useOrbitAutoTurn
                        ? moveInput.magnitude > 0.25f
                        : intent.Move.y > 0.25f;
                if (moveActive && intent.Sprint && sprintMoveActive)
                {
                    move *= SprintFactor;
                }
                spin *= 35f;

                if (!intent.IsGamepad && !useOrbitAutoTurn)
                {
                    float originalLength = move.magnitude;
                    if (move.x != 0f && move.z < 0f && Mathf.Abs(turn) > 0.1f)
                    {
                        move.z = 0f;
                        move.x *= 0.5f;
                    }
                    if (move.x != 0f && move.z == 0f && Mathf.Abs(turn) > 0.1f)
                    {
                        move.z = Mathf.Abs(move.x);
                    }
                    if (move.z < 0f && Mathf.Abs(turn) < 0.1f)
                    {
                        move.z *= 0.5f;
                    }
                    if (move.z < 0f && move.x == 0f && Mathf.Abs(turn) < 0.1f)
                    {
                        move.z = -1f;
                        float leftContact = AverageContact(2);
                        float rightContact = AverageContact(3);
                        if (leftContact > rightContact)
                        {
                            move.x = 1f;
                        }
                        if (rightContact > leftContact)
                        {
                            move.x = -1f;
                        }
                    }
                    move = Vector3.ClampMagnitude(move, originalLength);
                }

                if (hold && state.Carrier)
                {
                    move = Vector3.zero;
                    turn = 0f;
                    spin = 0f;
                }
                else if (shoot)
                {
                    move = Vector3.zero;
                }

                if (move != Vector3.zero)
                {
                    move = BasketballMath.WorldDirection(move, state.ActorRootRotation);
                    if (!useOrbitAutoTurn && !useWorldControl)
                    {
                        move = Quaternion.AngleAxis(60f * turn, Vector3.up) * move;
                    }
                }

                Vector3 pivotRoot = state.ActorRootPosition;
                for (int sample = pivot; sample < BasketballAgentState.SampleCount; sample++)
                {
                    float ratio = (float)(sample - pivot) /
                        (BasketballAgentState.SampleCount - 1 - pivot);
                    state.RootPositions[sample] = Vector3.Lerp(
                        state.RootPositions[sample], pivotRoot + ratio * move,
                        BasketballMath.GetControl(sample, 0.25f, 0.1f, 1f));
                    if (intent.UseWorldFacing && intent.WorldFacing.sqrMagnitude > 1e-10f)
                    {
                        Vector3 facing = Vector3.ProjectOnPlane(intent.WorldFacing, Vector3.up);
                        state.RootRotations[sample] = Quaternion.Slerp(
                            state.RootRotations[sample],
                            Quaternion.LookRotation(facing.normalized, Vector3.up),
                            BasketballMath.GetControl(sample, 0.5f, 0.1f, 1f));
                    }
                    else if (useOrbitFacing)
                    {
                        state.RootRotations[sample] = Quaternion.Slerp(
                            state.RootRotations[sample],
                            Quaternion.LookRotation(orbitFacing, Vector3.up),
                            Mathf.Abs(turn) *
                            BasketballMath.GetControl(sample, 0.5f, 0.1f, 1f));
                    }
                    else if (moveActive && move.sqrMagnitude > 0f && turn != 0f)
                    {
                        state.RootRotations[sample] = Quaternion.Slerp(
                            state.RootRotations[sample], Quaternion.LookRotation(move, Vector3.up),
                            Mathf.Abs(turn) * BasketballMath.GetControl(sample, 0.5f, 0.1f, 1f));
                        if (!intent.IsGamepad && !intent.Sprint)
                        {
                            float turnWeight = BasketballMath.ActivateCurve(
                                (float)(sample - pivot) /
                                (BasketballAgentState.SampleCount - 1 - pivot),
                                0.75f,
                                0f,
                                1f);
                            state.RootRotations[sample] = state.RootRotations[pivot] *
                                Quaternion.AngleAxis(60f * turn * turnWeight, Vector3.up);
                        }
                    }
                    if (spin != 0f)
                    {
                        float spinWeight = BasketballMath.ActivateCurve(
                            ratio, 0.25f, 0.75f, 0f);
                        state.RootRotations[sample] *= Quaternion.AngleAxis(
                            spin * spinWeight, Vector3.up);
                    }
                    state.RootVelocities[sample] = Vector3.Lerp(
                        state.RootVelocities[sample], move,
                        BasketballMath.GetControl(sample, 0.75f, 0.1f, 1f));

                    Vector3 ballTarget = new(ballControl.x, ballHeight, ballControl.z);
                    state.Pivots[sample] = BasketballMath.InterpolatePivot(
                        state.Pivots[sample], ballTarget,
                        BallControl(sample, horizontalControl, 0.2f),
                        BallControl(sample, heightControl, 0.1f));
                    Vector3 momentumTarget = new(
                        0.5f * BasketballAgentState.Framerate *
                        (ballTarget.x - state.Pivots[sample].x),
                        ballSpeed,
                        0.5f * BasketballAgentState.Framerate *
                        (ballTarget.z - state.Pivots[sample].z));
                    state.Momentums[sample] = BasketballMath.InterpolateMomentum(
                        state.Momentums[sample], momentumTarget,
                        BallControl(sample, horizontalControl, 0.2f),
                        BallControl(sample, speedControl, 0.1f));

                    for (int style = 0; style < BasketballAgentState.StyleCount; style++)
                    {
                        float action = style switch
                        {
                            0 => stand ? 1f : 0f,
                            1 => moveActive ? 1f : 0f,
                            2 => dribble ? 1f : 0f,
                            3 => holdAction,
                            _ => shootAction
                        };
                        int index = BasketballAgentState.StyleIndex(sample, style);
                        state.Styles[index] = Mathf.Lerp(
                            state.Styles[index], action,
                            BasketballMath.GetControl(
                                sample,
                                StyleControlBias(
                                    style,
                                    holdAction > 0.5f,
                                    shootAction > 0.1f),
                                0.1f,
                                1f));
                    }
                }
            }
        }

        private static float BallControl(int sample, bool active, float activeBias)
            => BasketballMath.GetControl(sample, active ? activeBias : 0f, 0f, 0.5f);

        private static float ShapeTurn(float value)
            => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), 1f / 1.25f);

        private static float StyleControlBias(int style, bool hold, bool shoot)
        {
            return style switch
            {
                0 => 0.5f,
                1 => 0.5f,
                2 => hold || shoot ? 0.5f : 1f,
                3 => hold ? 0.5f : 1f,
                _ => shoot ? 0.5f : 1f
            };
        }

        private float AverageContact(int contact)
        {
            float sum = 0f;
            for (int sample = 0; sample < BasketballAgentState.SampleCount; sample++)
            {
                sum += state.Contacts[BasketballAgentState.ContactIndex(sample, contact)];
            }
            return sum / BasketballAgentState.SampleCount;
        }

        private static Vector3 ToHoldTarget(Vector3 target)
        {
            target.x *= -1f;
            Vector3 horizontal = new(target.x, 0f, target.z);
            Vector3 scaled = Vector3.Scale(new Vector3(0.5f, 1f, 0.8f), -horizontal);
            return Quaternion.AngleAxis(65f, Vector3.right) * scaled +
                   new Vector3(0f, 1.5f, 0.15f);
        }

        private static bool ValidateFinite(float[] values, string block, out int invalidIndex)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!float.IsFinite(values[index]))
                {
                    invalidIndex = index;
                    return false;
                }
            }
            invalidIndex = -1;
            return true;
        }

        private void FailNonFinite(string block, int index)
        {
            Debug.LogError(
                $"Basketball neural {block} became non-finite at index {index}, " +
                $"tick {state.TickCount}. Simulation was stopped before applying an invalid pose.",
                this);
            enabled = false;
        }

        private void ProcessBallBeforeControl(int pivot)
        {
            using (BallMarker.Auto())
            {
                if (!state.Carrier)
                {
                    // Every non-owner observes the one real shared ball. In
                    // particular, a steal attempt must not invent a reachable
                    // proxy near the defender: that makes the learned Hold
                    // response reach one location while possession resolves at
                    // another, producing the visible snap on ownership change.
                    state.BallPositions[pivot] = rig.Ball.transform.position;
                    state.BallRotations[pivot] = rig.Ball.transform.rotation;
                    state.BallVelocities[pivot] = rig.Ball.Velocity;

                    if (possessionAuthority == null && intent.Hold)
                    {
                        float catchDistance = 1.25f * rig.Ball.Radius;
                        bool leftContact = Vector3.Distance(
                            state.BonePositions[18], state.BallPositions[pivot]) <= catchDistance;
                        bool rightContact = Vector3.Distance(
                            state.BonePositions[25], state.BallPositions[pivot]) <= catchDistance;
                        if (leftContact || rightContact)
                        {
                            SetCarrier(true, reacquire: true);
                        }
                    }
                    return;
                }

                if (possessionAuthority == null && reacquireTicksRemaining == 0)
                {
                    BasketballBallAuthorityState desired = intent.Hold
                        ? BasketballBallAuthorityState.Held
                        : BasketballBallAuthorityState.Controlled;
                    if (rig.Ball.State != desired)
                    {
                        rig.Ball.SetState(desired);
                    }
                }
            }
        }

        private Vector3 CalculatePassVelocity(Vector3 origin)
        {
            Vector3 target = passTarget.TransformPoint(passTargetOffset);
            Vector3 displacement = target - origin;
            Vector3 horizontal = Vector3.ProjectOnPlane(displacement, Vector3.up);
            float travelTime = Mathf.Clamp(horizontal.magnitude / 7f, 0.38f, 0.95f);
            return displacement / travelTime - 0.5f * Physics.gravity * travelTime;
        }

        private void ProcessBallAfterDecode()
        {
            using (BallMarker.Auto())
            {
                int pivot = BasketballAgentState.Pivot;
                if (state.Carrier)
                {
                    int ballContactIndex = BasketballAgentState.ContactIndex(pivot, 4);
                    float contact = state.Contacts[ballContactIndex];
                    Vector3 position = state.BallPositions[pivot];
                    Vector3 velocity = state.BallVelocities[pivot];
                    float groundWeight = Physics.Raycast(
                        position,
                        velocity,
                        rig.Ball.Radius + velocity.magnitude,
                        1 << 9)
                        ? contact
                        : 0f;
                    float previousY = position.y;
                    position.y = Mathf.Lerp(position.y, rig.Ball.Radius, groundWeight);
                    velocity.y += BasketballAgentState.Framerate *
                        (position.y - previousY);
                    state.BallPositions[pivot] = position;
                    state.BallVelocities[pivot] = velocity;
                }

                if (possessionAuthority == null)
                {
                    float currentSpeed = state.BallVelocities[pivot].magnitude;
                    float previousSpeed = state.BallVelocities[pivot - 1].magnitude;
                    float handContact =
                        state.Contacts[BasketballAgentState.ContactIndex(pivot, 2)] +
                        state.Contacts[BasketballAgentState.ContactIndex(pivot, 3)];
                    if (intent.Shoot && currentSpeed < previousSpeed &&
                        state.BallPositions[pivot].y > 1.5f && handContact < 0.1f)
                    {
                        Vector3 releaseVelocity = passTarget != null
                            ? CalculatePassVelocity(state.BallPositions[pivot])
                            : state.BallVelocities[pivot];
                        SetCarrier(false, reacquire: false);
                        rig.Ball.Release(releaseVelocity, Vector3.zero);
                        ClearPassTarget();
                    }
                }

                if (possessionAuthority == null && reacquireTicksRemaining > 0)
                {
                    reacquireTicksRemaining--;
                    if (reacquireTicksRemaining == 0)
                    {
                        rig.Ball.CompleteReacquire(intent.Hold);
                    }
                }
            }
        }

        private BasketballBallObservation BuildBallObservation()
        {
            int pivot = BasketballAgentState.Pivot;
            return new BasketballBallObservation(
                state.TickCount,
                state.BallPositions[pivot],
                state.BallVelocities[pivot],
                state.BallVelocities[pivot - 1],
                state.ActorRootPosition,
                state.ActorRootRotation * Vector3.forward,
                state.BonePositions[18],
                state.BonePositions[25],
                state.BoneVelocities[18],
                state.BoneVelocities[25],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 2)],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 3)],
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 4)],
                intent.Hold,
                state.ShootIntent,
                intent.Steal);
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballReferenceRig referenceRig,
            BasketballKeyboardMouseInputProvider provider,
            Camera camera)
        {
            rig = referenceRig;
            inputProvider = provider;
            movementCamera = camera;
            orbitCamera = movementCamera != null
                ? movementCamera.GetComponent<ThirdPersonOrbitCamera>()
                : null;
        }
#endif
    }
}
