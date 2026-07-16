using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballPoseInterpolationMode
    {
        Disabled,
        Linear,
        SmoothStep
    }

    /// <summary>
    /// Shared runtime profile for every Basketball agent in a match. The model's
    /// canonical closed-loop rate remains 30 Hz; lower values are experimental
    /// scheduling modes and intentionally do not rewrite the model contract.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BasketballRuntimeSettings",
        menuName = "AI4Animation/Basketball Runtime Settings")]
    public sealed class BasketballRuntimeSettings : ScriptableObject
    {
        public const string DefaultResourcePath = "Settings/BasketballRuntimeSettings";
        public const int CanonicalNeuralTickRate = 30;

        [Header("Neural Simulation")]
        [Tooltip("30 Hz is the canonical Basketball 2020 rate. Lower values are experimental and do not retime the trained model mathematics.")]
        [SerializeField, Range(10, CanonicalNeuralTickRate)]
        private int neuralTickRate = CanonicalNeuralTickRate;
        [Tooltip("Presentation-only interpolation between previous/current neural states. It never overwrites recurrent simulation state.")]
        [SerializeField] private BasketballPoseInterpolationMode interpolationMode =
            BasketballPoseInterpolationMode.Linear;
        [SerializeField, Range(1, 8)] private int maximumCatchUpTicks = 4;
        [SerializeField] private bool enableContactIK = true;
        [SerializeField] private bool enableDebugDraw;
        [SerializeField] private bool deterministicMode = true;

        [Header("Application")]
        [SerializeField, Range(-1, 360)] private int targetFrameRate = -1;
        [SerializeField, Range(0, 4)] private int vSyncCount;

        [Header("Camera Orbit")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minimumPitch = -15f;
        [SerializeField] private float maximumPitch = 70f;
        [SerializeField] private float recenterPitch = 18f;
        [SerializeField, Min(0f)] private float rotationSmoothTime = 0.045f;
        [SerializeField, Min(0.1f)] private float cameraDistance = 5.8f;
        [SerializeField, Min(0.1f)] private float minimumCameraDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float maximumCameraDistance = 9f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 0.015f;
        [Tooltip("Presentation-only smoothing for the rendered player root before it becomes the camera pivot.")]
        [SerializeField, Min(0f)] private float cameraTargetSmoothTime = 0.035f;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.06f;
        [SerializeField, Min(0f)] private float distanceSmoothTime = 0.08f;

        [Header("Passing")]
        [Tooltip("Minimum height above both release and catch points for a normal chest/lead pass.")]
        [SerializeField, Min(0.05f)] private float directPassApexClearance = 0.18f;
        [Tooltip("Minimum height above both endpoints for an intentional lob pass.")]
        [SerializeField, Min(0.1f)] private float lobPassApexClearance = 0.9f;
        [Tooltip("Maximum horizontal speed for chest and lead passes. Longer passes gain only the arc required to stay below this speed.")]
        [SerializeField, Min(1f)] private float maximumDirectPassSpeed = 11.5f;
        [Tooltip("Maximum horizontal speed for an intentional lob pass.")]
        [SerializeField, Min(1f)] private float maximumLobPassSpeed = 7f;
        [Tooltip("Limits receiver prediction so a noisy recurrent velocity cannot move the catch point far away.")]
        [SerializeField, Min(0f)] private float maximumReceiverLeadDistance = 1.35f;
        [SerializeField, Range(0f, 1.5f)] private float receiverLeadScale = 0.9f;
        [Tooltip("Normal passes wait for the model ball to rise near the passer's chest before physics takes ownership.")]
        [SerializeField, Min(0f)] private float passReleaseBelowChestTolerance = 0.48f;
        [SerializeField, Min(0f)] private float passGatherSeconds = 0.08f;
        [SerializeField, Min(0f)] private float passAlignSeconds = 0.16f;
        [SerializeField, Min(0.05f)] private float passPushSeconds = 0.34f;
        [SerializeField, Min(0.05f)] private float minimumPassReleaseSeconds = 0.3f;
        [Tooltip("Fallback release deadline when the pretrained model does not produce a clean chest-height release pose.")]
        [SerializeField, Min(0.35f)] private float forcedPassReleaseSeconds = 0.58f;

        [Header("Shooting")]
        [Tooltip("Minimum apex height above the release point or rim for a normal shot.")]
        [SerializeField, Min(0.2f)] private float shotApexClearance = 1.05f;
        [Tooltip("Additional apex height per metre of horizontal shot distance.")]
        [SerializeField, Min(0f)] private float shotDistanceApexScale = 0.035f;
        [Tooltip("Upper bound for the distance-adjusted apex clearance.")]
        [SerializeField, Min(0.5f)] private float maximumShotApexClearance = 2.1f;
        [Tooltip("Safety ceiling for the one-time physical release velocity.")]
        [SerializeField, Min(1f)] private float maximumShotLaunchSpeed = 18f;
        [Tooltip("Required planar facing alignment before the Shoot style is committed.")]
        [SerializeField, Range(0f, 1f)] private float shotFacingDot = 0.9f;
        [Tooltip("Maximum time spent turning toward the hoop before committing the Shoot style.")]
        [SerializeField, Min(0f)] private float maximumShotAlignSeconds = 0.45f;
        [Tooltip("Cancels a latched shot if the model has not released the ball by this deadline.")]
        [SerializeField, Min(0.5f)] private float shotCommandTimeoutSeconds = 1.8f;
        [Tooltip("Minimum time given to the neural pose before a natural shot release is accepted.")]
        [SerializeField, Min(0.05f)] private float minimumShotReleaseSeconds = 0.2f;
        [Tooltip("A latched Shoot command always releases by this deadline, even when the pretrained contact signal is imperfect.")]
        [SerializeField, Min(0.15f)] private float forcedShotReleaseSeconds = 0.58f;
        [SerializeField, Min(0.2f)] private float minimumShotReleaseHeight = 1.25f;
        [SerializeField, Range(0f, 1f)] private float naturalShotReleaseHandContact = 0.18f;

        [Header("Defensive Interaction")]
        [Tooltip("Maximum root-to-ball range where a non-owner steal intent may feed the real ball into the pretrained Hold response. This only drives animation; it never grants ball authority.")]
        [SerializeField, Min(0.5f)] private float stealReachAnimationDistance = 1.55f;

        [Header("Camera Collision and Culling")]
        [SerializeField] private bool cameraCollisionEnabled = true;
        [SerializeField] private LayerMask cameraCollisionMask = (1 << 0) | (1 << 9);
        [SerializeField, Min(0.01f)] private float cameraCollisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float cameraCollisionPadding = 0.08f;
        [SerializeField, Min(0.05f)] private float minimumCollisionDistance = 0.35f;
        [SerializeField, Range(1, 8)] private int collisionQueryIntervalFrames = 2;
        [Tooltip("Enable only after baking occlusion data for the active scene.")]
        [SerializeField] private bool useOcclusionCulling;
        [SerializeField] private bool allowDynamicResolution;
        [SerializeField] private bool lockCursorOnPlay = true;

        [Header("Rendering Performance")]
        [Tooltip("Keeps the scene lighting but disables redundant realtime shadow maps.")]
        [SerializeField] private bool applyRealtimeShadowBudget = true;
        [SerializeField, Range(0, 4)] private int maximumShadowedDirectionalLights = 1;
        [SerializeField, Range(0, 8)] private int maximumShadowedAdditionalLights;

        [Header("HUD and Profiling")]
        [SerializeField, Range(1, 60)] private int telemetryRefreshRate = 15;
        [SerializeField, Range(1, 30)] private int performanceRefreshRate = 4;
        [SerializeField, Range(1, 60)] private int controlDiskRefreshRate = 30;
        [SerializeField, Range(1, 30)] private int frameTimingCaptureInterval = 15;

        private static BasketballRuntimeSettings cachedDefault;

        public int NeuralTickRate => neuralTickRate;
        public BasketballPoseInterpolationMode InterpolationMode => interpolationMode;
        public bool RenderInterpolation => interpolationMode != BasketballPoseInterpolationMode.Disabled;
        public int MaximumCatchUpTicks => maximumCatchUpTicks;
        public bool EnableContactIK => enableContactIK;
        public bool EnableDebugDraw => enableDebugDraw;
        public bool DeterministicMode => deterministicMode;
        public int TargetFrameRate => targetFrameRate;
        public int VSyncCount => vSyncCount;

        public float MouseSensitivity => mouseSensitivity;
        public float MinimumPitch => minimumPitch;
        public float MaximumPitch => maximumPitch;
        public float RecenterPitch => recenterPitch;
        public float RotationSmoothTime => rotationSmoothTime;
        public float CameraDistance => cameraDistance;
        public float MinimumCameraDistance => minimumCameraDistance;
        public float MaximumCameraDistance => maximumCameraDistance;
        public float ZoomSensitivity => zoomSensitivity;
        public float CameraTargetSmoothTime => cameraTargetSmoothTime;
        public float PositionSmoothTime => positionSmoothTime;
        public float DistanceSmoothTime => distanceSmoothTime;
        public float DirectPassApexClearance => directPassApexClearance;
        public float LobPassApexClearance => lobPassApexClearance;
        public float MaximumDirectPassSpeed => maximumDirectPassSpeed;
        public float MaximumLobPassSpeed => maximumLobPassSpeed;
        public float MaximumReceiverLeadDistance => maximumReceiverLeadDistance;
        public float ReceiverLeadScale => receiverLeadScale;
        public float PassReleaseBelowChestTolerance => passReleaseBelowChestTolerance;
        public float PassGatherSeconds => passGatherSeconds;
        public float PassAlignSeconds => passAlignSeconds;
        public float PassPushSeconds => passPushSeconds;
        public float MinimumPassReleaseSeconds => minimumPassReleaseSeconds;
        public float ForcedPassReleaseSeconds => forcedPassReleaseSeconds;
        public float ShotApexClearance => shotApexClearance;
        public float ShotDistanceApexScale => shotDistanceApexScale;
        public float MaximumShotApexClearance => maximumShotApexClearance;
        public float MaximumShotLaunchSpeed => maximumShotLaunchSpeed;
        public float ShotFacingDot => shotFacingDot;
        public float MaximumShotAlignSeconds => maximumShotAlignSeconds;
        public float ShotCommandTimeoutSeconds => shotCommandTimeoutSeconds;
        public float MinimumShotReleaseSeconds => minimumShotReleaseSeconds;
        public float ForcedShotReleaseSeconds => forcedShotReleaseSeconds;
        public float MinimumShotReleaseHeight => minimumShotReleaseHeight;
        public float NaturalShotReleaseHandContact => naturalShotReleaseHandContact;
        public float StealReachAnimationDistance => stealReachAnimationDistance;
        public bool CameraCollisionEnabled => cameraCollisionEnabled;
        public LayerMask CameraCollisionMask => cameraCollisionMask;
        public float CameraCollisionRadius => cameraCollisionRadius;
        public float CameraCollisionPadding => cameraCollisionPadding;
        public float MinimumCollisionDistance => minimumCollisionDistance;
        public int CollisionQueryIntervalFrames => collisionQueryIntervalFrames;
        public bool UseOcclusionCulling => useOcclusionCulling;
        public bool AllowDynamicResolution => allowDynamicResolution;
        public bool LockCursorOnPlay => lockCursorOnPlay;

        public bool ApplyRealtimeShadowBudget => applyRealtimeShadowBudget;
        public int MaximumShadowedDirectionalLights => maximumShadowedDirectionalLights;
        public int MaximumShadowedAdditionalLights => maximumShadowedAdditionalLights;
        public float TelemetryRefreshInterval => 1f / telemetryRefreshRate;
        public float PerformanceRefreshInterval => 1f / performanceRefreshRate;
        public float ControlDiskRefreshInterval => 1f / controlDiskRefreshRate;
        public int FrameTimingCaptureInterval => frameTimingCaptureInterval;

        public static BasketballRuntimeSettings LoadDefault()
        {
            cachedDefault = cachedDefault != null
                ? cachedDefault
                : Resources.Load<BasketballRuntimeSettings>(DefaultResourcePath);
            return cachedDefault;
        }

        public float ShapeInterpolationAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            return interpolationMode == BasketballPoseInterpolationMode.SmoothStep
                ? alpha * alpha * (3f - 2f * alpha)
                : interpolationMode == BasketballPoseInterpolationMode.Disabled
                    ? 1f
                    : alpha;
        }

        public void ApplyApplicationSettings()
        {
            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = targetFrameRate;
        }

        public int ApplyShadowBudget()
        {
            if (!applyRealtimeShadowBudget)
            {
                return 0;
            }

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            Light preferredDirectional = RenderSettings.sun;
            if (preferredDirectional == null || !preferredDirectional.isActiveAndEnabled ||
                preferredDirectional.type != LightType.Directional ||
                preferredDirectional.shadows == LightShadows.None)
            {
                preferredDirectional = null;
                float strongestIntensity = float.NegativeInfinity;
                for (int index = 0; index < lights.Length; index++)
                {
                    Light candidate = lights[index];
                    if (candidate.type != LightType.Directional ||
                        candidate.shadows == LightShadows.None ||
                        candidate.intensity <= strongestIntensity)
                    {
                        continue;
                    }
                    preferredDirectional = candidate;
                    strongestIntensity = candidate.intensity;
                }
            }

            int directionalCount = 0;
            int additionalCount = 0;
            int disabledCount = 0;
            if (preferredDirectional != null && maximumShadowedDirectionalLights > 0)
            {
                directionalCount = 1;
            }

            for (int index = 0; index < lights.Length; index++)
            {
                Light light = lights[index];
                if (light.shadows == LightShadows.None)
                {
                    continue;
                }

                bool keep;
                if (light.type == LightType.Directional)
                {
                    if (light == preferredDirectional && maximumShadowedDirectionalLights > 0)
                    {
                        continue;
                    }
                    keep = directionalCount < maximumShadowedDirectionalLights;
                    if (keep)
                    {
                        directionalCount++;
                    }
                }
                else
                {
                    keep = additionalCount < maximumShadowedAdditionalLights;
                    if (keep)
                    {
                        additionalCount++;
                    }
                }

                if (!keep)
                {
                    light.shadows = LightShadows.None;
                    disabledCount++;
                }
            }
            return disabledCount;
        }

        private void OnValidate()
        {
            neuralTickRate = Mathf.Clamp(neuralTickRate, 10, CanonicalNeuralTickRate);
            maximumCatchUpTicks = Mathf.Clamp(maximumCatchUpTicks, 1, 8);
            maximumPitch = Mathf.Max(minimumPitch, maximumPitch);
            recenterPitch = Mathf.Clamp(recenterPitch, minimumPitch, maximumPitch);
            minimumCameraDistance = Mathf.Max(0.1f, minimumCameraDistance);
            maximumCameraDistance = Mathf.Max(minimumCameraDistance, maximumCameraDistance);
            cameraDistance = Mathf.Clamp(
                cameraDistance,
                minimumCameraDistance,
                maximumCameraDistance);
            directPassApexClearance = Mathf.Max(0.05f, directPassApexClearance);
            lobPassApexClearance = Mathf.Max(
                directPassApexClearance,
                lobPassApexClearance);
            maximumDirectPassSpeed = Mathf.Max(1f, maximumDirectPassSpeed);
            maximumLobPassSpeed = Mathf.Max(1f, maximumLobPassSpeed);
            maximumReceiverLeadDistance = Mathf.Max(0f, maximumReceiverLeadDistance);
            passGatherSeconds = Mathf.Max(0f, passGatherSeconds);
            passAlignSeconds = Mathf.Max(passGatherSeconds, passAlignSeconds);
            passPushSeconds = Mathf.Max(passAlignSeconds + 0.05f, passPushSeconds);
            minimumPassReleaseSeconds = Mathf.Clamp(
                minimumPassReleaseSeconds,
                0.05f,
                passPushSeconds);
            forcedPassReleaseSeconds = Mathf.Max(
                minimumPassReleaseSeconds + 0.05f,
                forcedPassReleaseSeconds);
            shotApexClearance = Mathf.Max(0.2f, shotApexClearance);
            maximumShotApexClearance = Mathf.Max(
                shotApexClearance,
                maximumShotApexClearance);
            maximumShotLaunchSpeed = Mathf.Max(1f, maximumShotLaunchSpeed);
            shotFacingDot = Mathf.Clamp01(shotFacingDot);
            maximumShotAlignSeconds = Mathf.Max(0f, maximumShotAlignSeconds);
            shotCommandTimeoutSeconds = Mathf.Max(0.5f, shotCommandTimeoutSeconds);
            minimumShotReleaseSeconds = Mathf.Max(0.05f, minimumShotReleaseSeconds);
            forcedShotReleaseSeconds = Mathf.Max(
                minimumShotReleaseSeconds + 0.05f,
                forcedShotReleaseSeconds);
            minimumShotReleaseHeight = Mathf.Max(0.2f, minimumShotReleaseHeight);
            naturalShotReleaseHandContact = Mathf.Clamp01(
                naturalShotReleaseHandContact);
            stealReachAnimationDistance = Mathf.Max(0.5f, stealReachAnimationDistance);
        }
    }
}
