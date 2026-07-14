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
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 pivotOffset = new(0f, 1.35f, 0f);

        [Header("Orbit")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minimumPitch = -15f;
        [SerializeField] private float maximumPitch = 70f;
        [SerializeField] private float recenterPitch = 18f;
        [SerializeField, Min(0f)] private float rotationSmoothTime = 0.045f;

        [Header("Distance")]
        [SerializeField, Min(0.1f)] private float distance = 5.8f;
        [SerializeField, Min(0.1f)] private float minimumDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float maximumDistance = 9f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 0.015f;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.06f;
        [SerializeField, Min(0f)] private float distanceSmoothTime = 0.08f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = (1 << 0) | (1 << 9);
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float collisionPadding = 0.08f;
        [SerializeField, Min(0.05f)] private float minimumCollisionDistance = 0.35f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnPlay = true;

        private float desiredYaw;
        private float desiredPitch;
        private float currentYaw;
        private float currentPitch;
        private float currentDistance;
        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;
        private Vector3 positionVelocity;
        private bool initialized;
        private bool cursorLocked;

        public Transform Target => target;
        public float CurrentDistance => currentDistance;
        public bool CursorLocked => cursorLocked;
        public Vector3 PlanarForward =>
            Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        public bool IsBallControlMode =>
            Mouse.current != null && Mouse.current.rightButton.isPressed;

        private void OnEnable()
        {
            InitializeFromCurrentPose();
            if (Application.isPlaying)
            {
                SetCursorLocked(lockCursorOnPlay);
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
            if (target == null)
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
                desiredYaw += delta.x * mouseSensitivity;
                desiredPitch = Mathf.Clamp(
                    desiredPitch - delta.y * mouseSensitivity,
                    minimumPitch,
                    maximumPitch);
            }

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                distance = Mathf.Clamp(
                    distance - scroll * zoomSensitivity,
                    minimumDistance,
                    maximumDistance);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }
            if (!initialized)
            {
                InitializeFromCurrentPose();
            }

            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            currentYaw = Mathf.SmoothDampAngle(
                currentYaw, desiredYaw, ref yawVelocity, rotationSmoothTime,
                Mathf.Infinity, deltaTime);
            currentPitch = Mathf.SmoothDampAngle(
                currentPitch, desiredPitch, ref pitchVelocity, rotationSmoothTime,
                Mathf.Infinity, deltaTime);

            Vector3 pivot = target.position + pivotOffset;
            Quaternion orbitRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            Vector3 cameraDirection = -(orbitRotation * Vector3.forward);
            float collisionDistance = ResolveCollisionDistance(pivot, cameraDirection, distance);

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
                    distanceSmoothTime, Mathf.Infinity, deltaTime);
            }

            Vector3 desiredPosition = pivot + cameraDirection * currentDistance;
            Vector3 smoothedPosition = Vector3.SmoothDamp(
                transform.position, desiredPosition, ref positionVelocity,
                positionSmoothTime, Mathf.Infinity, deltaTime);
            Vector3 lookDirection = pivot - smoothedPosition;
            Quaternion desiredRotation = lookDirection.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(lookDirection, Vector3.up)
                : orbitRotation;
            transform.SetPositionAndRotation(smoothedPosition, desiredRotation);
        }

        public void Recenter()
        {
            if (target == null)
            {
                return;
            }
            SetHeading(target.eulerAngles.y, false);
            desiredPitch = Mathf.Clamp(recenterPitch, minimumPitch, maximumPitch);
        }

        public void SetTarget(Transform followTarget)
        {
            if (target == followTarget)
            {
                return;
            }
            target = followTarget;
            positionVelocity = Vector3.zero;
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
            if (target == null)
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
                distance = Mathf.Clamp(toPivot.magnitude, minimumDistance, maximumDistance);
            }
            else
            {
                desiredYaw = currentYaw = target.eulerAngles.y;
                desiredPitch = currentPitch = recenterPitch;
                distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            }

            desiredPitch = currentPitch = Mathf.Clamp(
                desiredPitch, minimumPitch, maximumPitch);
            currentDistance = distance;
            positionVelocity = Vector3.zero;
            initialized = true;
        }

        private float ResolveCollisionDistance(
            Vector3 pivot,
            Vector3 cameraDirection,
            float requestedDistance)
        {
            float resolved = requestedDistance;
            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    cameraDirection,
                    out RaycastHit hit,
                    requestedDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                resolved = Mathf.Max(
                    minimumCollisionDistance,
                    hit.distance - collisionPadding);
            }
            return resolved;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

#if UNITY_EDITOR
        public void Configure(Transform followTarget)
        {
            target = followTarget;
            pivotOffset = new Vector3(0f, 1.35f, 0f);
            mouseSensitivity = 0.12f;
            minimumPitch = -15f;
            maximumPitch = 70f;
            recenterPitch = 18f;
            rotationSmoothTime = 0.045f;
            minimumDistance = 1.5f;
            maximumDistance = 9f;
            zoomSensitivity = 0.015f;
            positionSmoothTime = 0.06f;
            distanceSmoothTime = 0.08f;
            collisionMask = (1 << 0) | (1 << 9);
            collisionRadius = 0.2f;
            collisionPadding = 0.08f;
            minimumCollisionDistance = 0.35f;
            lockCursorOnPlay = true;
            initialized = false;
            InitializeFromCurrentPose();
        }
#endif
    }
}
