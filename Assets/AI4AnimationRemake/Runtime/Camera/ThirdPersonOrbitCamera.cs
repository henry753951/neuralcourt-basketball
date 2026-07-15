using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Presentation-only third-person camera. It follows the rendered player Transform and
    /// never writes to BasketballAgentState or any neural-series data.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonOrbitCamera : MonoBehaviour
    {
        private static readonly ProfilerMarker CameraMarker = new("Basketball.Camera");
        private static readonly ProfilerMarker CameraInputMarker =
            new("Basketball.Camera.Input");
        private static readonly ProfilerMarker CameraCollisionMarker =
            new("Basketball.Camera.Collision");

        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 pivotOffset = new(0f, 1.35f, 0f);

        private BasketballRuntimeSettings runtimeSettings;
        private Camera attachedCamera;
        private float desiredYaw;
        private float desiredPitch;
        private float desiredDistance;
        private float currentYaw;
        private float currentPitch;
        private float currentDistance;
        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;
        private Vector3 positionVelocity;
        private Vector3 targetPositionVelocity;
        private Vector3 smoothedTargetPosition;
        private bool initialized;
        private bool cursorLocked;
        private int collisionQueryCountdown;
        private float cachedCollisionDistance;
        private readonly RaycastHit[] collisionHits = new RaycastHit[32];

        public BasketballRuntimeSettings RuntimeSettings => runtimeSettings != null
            ? runtimeSettings
            : BasketballRuntimeSettings.LoadDefault();
        public Transform Target => target;
        public float CurrentDistance => currentDistance;
        public bool CursorLocked => cursorLocked;
        public Vector3 PlanarForward =>
            Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        public bool IsBallControlMode =>
            Mouse.current != null && Mouse.current.rightButton.isPressed;

        private void OnEnable()
        {
            runtimeSettings = runtimeSettings != null
                ? runtimeSettings
                : BasketballRuntimeSettings.LoadDefault();
            attachedCamera = GetComponent<Camera>();
            ApplyCameraSettings();
            InitializeFromCurrentPose();
            if (Application.isPlaying)
            {
                SetCursorLocked(RuntimeSettings == null || RuntimeSettings.LockCursorOnPlay);
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                SetCursorLocked(false);
            }
        }

        private void Update()
        {
            using (CameraInputMarker.Auto())
            {
                BasketballRuntimeSettings settings = RuntimeSettings;
                if (target == null || settings == null)
                {
                    return;
                }

                Keyboard keyboard = Keyboard.current;
                Mouse mouse = Mouse.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    SetCursorLocked(!cursorLocked);
                }

                if (!cursorLocked)
                {
                    if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    {
                        SetCursorLocked(true);
                    }
                    return;
                }

                if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                {
                    Recenter();
                }

                // Right mouse is reserved for the original ball-location control.
                if (mouse != null && !mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    desiredYaw += delta.x * settings.MouseSensitivity;
                    desiredPitch = Mathf.Clamp(
                        desiredPitch - delta.y * settings.MouseSensitivity,
                        settings.MinimumPitch,
                        settings.MaximumPitch);
                }

                if (mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    desiredDistance = Mathf.Clamp(
                        desiredDistance - scroll * settings.ZoomSensitivity,
                        settings.MinimumCameraDistance,
                        settings.MaximumCameraDistance);
                }
            }
        }

        private void LateUpdate()
        {
            using (CameraMarker.Auto())
            {
                BasketballRuntimeSettings settings = RuntimeSettings;
                if (target == null || settings == null)
                {
                    return;
                }
                if (!initialized)
                {
                    InitializeFromCurrentPose();
                }

                float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                currentYaw = Mathf.SmoothDampAngle(
                    currentYaw, desiredYaw, ref yawVelocity, settings.RotationSmoothTime,
                    Mathf.Infinity, deltaTime);
                currentPitch = Mathf.SmoothDampAngle(
                    currentPitch, desiredPitch, ref pitchVelocity, settings.RotationSmoothTime,
                    Mathf.Infinity, deltaTime);

                smoothedTargetPosition = settings.CameraTargetSmoothTime <= 0f
                    ? target.position
                    : Vector3.SmoothDamp(
                        smoothedTargetPosition,
                        target.position,
                        ref targetPositionVelocity,
                        settings.CameraTargetSmoothTime,
                        Mathf.Infinity,
                        deltaTime);
                Vector3 pivot = smoothedTargetPosition + pivotOffset;
                Quaternion orbitRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
                Vector3 cameraDirection = -(orbitRotation * Vector3.forward);
                float collisionDistance = ResolveCollisionDistance(
                    pivot,
                    cameraDirection,
                    desiredDistance,
                    settings);

                // Move inward immediately so an obstacle cannot be crossed. Ease back out when clear.
                if (collisionDistance < currentDistance)
                {
                    currentDistance = collisionDistance;
                    distanceVelocity = 0f;
                }
                else
                {
                    currentDistance = Mathf.SmoothDamp(
                        currentDistance, collisionDistance, ref distanceVelocity,
                        settings.DistanceSmoothTime, Mathf.Infinity, deltaTime);
                }

                Vector3 desiredPosition = pivot + cameraDirection * currentDistance;
                Vector3 smoothedPosition = Vector3.SmoothDamp(
                    transform.position, desiredPosition, ref positionVelocity,
                    settings.PositionSmoothTime, Mathf.Infinity, deltaTime);
                Vector3 lookDirection = pivot - smoothedPosition;
                Quaternion desiredRotation = lookDirection.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(lookDirection, Vector3.up)
                    : orbitRotation;
                transform.SetPositionAndRotation(smoothedPosition, desiredRotation);
            }
        }

        public void Recenter()
        {
            if (target == null)
            {
                return;
            }
            SetHeading(target.eulerAngles.y, false);
            BasketballRuntimeSettings settings = RuntimeSettings;
            if (settings != null)
            {
                desiredPitch = Mathf.Clamp(
                    settings.RecenterPitch,
                    settings.MinimumPitch,
                    settings.MaximumPitch);
            }
        }

        public void SetTarget(Transform followTarget)
        {
            if (target == followTarget)
            {
                return;
            }
            target = followTarget;
            collisionQueryCountdown = 0;
            if (!initialized && target != null)
            {
                InitializeFromCurrentPose();
            }
        }

        public void SetHeading(float yaw, bool snap)
        {
            desiredYaw = yaw;
            if (snap)
            {
                currentYaw = yaw;
                yawVelocity = 0f;
            }
        }

        public void SetCursorLocked(bool value)
        {
            cursorLocked = value;
            if (!Application.isPlaying)
            {
                return;
            }
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void InitializeFromCurrentPose()
        {
            BasketballRuntimeSettings settings = RuntimeSettings;
            if (target == null || settings == null)
            {
                initialized = false;
                return;
            }

            Vector3 pivot = target.position + pivotOffset;
            Vector3 toPivot = pivot - transform.position;
            if (toPivot.sqrMagnitude > 1e-8f)
            {
                Vector3 angles = Quaternion.LookRotation(toPivot, Vector3.up).eulerAngles;
                desiredYaw = currentYaw = angles.y;
                desiredPitch = currentPitch = NormalizeAngle(angles.x);
                desiredDistance = Mathf.Clamp(
                    toPivot.magnitude,
                    settings.MinimumCameraDistance,
                    settings.MaximumCameraDistance);
            }
            else
            {
                desiredYaw = currentYaw = target.eulerAngles.y;
                desiredPitch = currentPitch = settings.RecenterPitch;
                desiredDistance = Mathf.Clamp(
                    settings.CameraDistance,
                    settings.MinimumCameraDistance,
                    settings.MaximumCameraDistance);
            }

            desiredPitch = currentPitch = Mathf.Clamp(
                desiredPitch, settings.MinimumPitch, settings.MaximumPitch);
            currentDistance = desiredDistance;
            cachedCollisionDistance = desiredDistance;
            collisionQueryCountdown = 0;
            positionVelocity = Vector3.zero;
            targetPositionVelocity = Vector3.zero;
            smoothedTargetPosition = target.position;
            initialized = true;
        }

        private float ResolveCollisionDistance(
            Vector3 pivot,
            Vector3 cameraDirection,
            float requestedDistance,
            BasketballRuntimeSettings settings)
        {
            if (!settings.CameraCollisionEnabled)
            {
                return requestedDistance;
            }
            if (collisionQueryCountdown > 0)
            {
                collisionQueryCountdown--;
                return Mathf.Min(requestedDistance, cachedCollisionDistance);
            }

            float resolved = requestedDistance;
            using (CameraCollisionMarker.Auto())
            {
                int hitCount = Physics.SphereCastNonAlloc(
                    pivot,
                    settings.CameraCollisionRadius,
                    cameraDirection,
                    collisionHits,
                    requestedDistance,
                    settings.CameraCollisionMask,
                    QueryTriggerInteraction.Ignore);
                float nearestDistance = float.PositiveInfinity;
                for (int index = 0; index < hitCount; index++)
                {
                    RaycastHit hit = collisionHits[index];
                    Collider collider = hit.collider;
                    if (collider == null || IsDynamicBasketballObject(collider))
                    {
                        continue;
                    }
                    nearestDistance = Mathf.Min(nearestDistance, hit.distance);
                }
                if (!float.IsPositiveInfinity(nearestDistance))
                {
                    resolved = Mathf.Max(
                        settings.MinimumCollisionDistance,
                        nearestDistance - settings.CameraCollisionPadding);
                }
            }
            cachedCollisionDistance = resolved;
            collisionQueryCountdown = settings.CollisionQueryIntervalFrames - 1;
            return resolved;
        }

        private static bool IsDynamicBasketballObject(Collider collider)
        {
            return collider.GetComponentInParent<BasketballTeamMember>() != null ||
                   collider.GetComponentInParent<BasketballBallController>() != null;
        }

        internal void SetRuntimeSettings(BasketballRuntimeSettings value)
        {
            runtimeSettings = value;
            ApplyCameraSettings();
            initialized = false;
        }

        private void ApplyCameraSettings()
        {
            BasketballRuntimeSettings settings = RuntimeSettings;
            if (attachedCamera == null)
            {
                attachedCamera = GetComponent<Camera>();
            }
            if (attachedCamera == null || settings == null)
            {
                return;
            }
            attachedCamera.useOcclusionCulling = settings.UseOcclusionCulling;
            attachedCamera.allowDynamicResolution = settings.AllowDynamicResolution;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

#if UNITY_EDITOR
        public void Configure(
            Transform followTarget,
            BasketballRuntimeSettings settings = null)
        {
            target = followTarget;
            runtimeSettings = settings != null
                ? settings
                : BasketballRuntimeSettings.LoadDefault();
            pivotOffset = new Vector3(0f, 1.35f, 0f);
            attachedCamera = GetComponent<Camera>();
            ApplyCameraSettings();
            initialized = false;
            InitializeFromCurrentPose();
        }
#endif
    }
}
