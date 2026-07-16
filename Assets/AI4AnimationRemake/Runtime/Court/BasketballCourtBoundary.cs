using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Owns the four invisible physical walls around a basketball court.
    /// The court lives below the legacy World root, whose 90-degree rotation
    /// maps this prefab's local X length axis onto BasketballDemo world Z.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasketballCourtBoundary : MonoBehaviour
    {
        [Header("Playable Area")]
        [SerializeField, Min(1f)] private float courtLength = 28f;
        [SerializeField, Min(1f)] private float courtWidth = 15f;

        [Header("Invisible Walls")]
        [SerializeField, Min(0.05f)] private float wallThickness = 0.35f;
        [SerializeField, Min(1f)] private float wallHeight = 12f;
        [SerializeField] private float wallBottom = -0.25f;
        [SerializeField] private BoxCollider positiveLengthWall;
        [SerializeField] private BoxCollider negativeLengthWall;
        [SerializeField] private BoxCollider positiveWidthWall;
        [SerializeField] private BoxCollider negativeWidthWall;

        [Header("Loose Ball Recovery")]
        [SerializeField] private BasketballBallController ball;
        [SerializeField, Min(0f)] private float ballRecoveryInset = 1.25f;
        [SerializeField, Min(0f)] private float minimumReachableInset = 1.25f;
        [SerializeField, Min(0.05f)] private float ballRecoveryMaximumHeight = 0.65f;
        [SerializeField, Min(0.1f)] private float ballRecoveryMaximumSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float ballReturnAcceleration = 10f;
        [SerializeField, Min(0.1f)] private float ballReturnMaximumSpeed = 2.25f;
        [SerializeField, Min(0.1f)] private float ballReturnPositionSpeed = 3.25f;
        [SerializeField, Min(0.05f)] private float hardRecoveryOverflow = 0.75f;
        [SerializeField, Min(0.05f)] private float hardRecoveryBelowCourt = 1.25f;

        private float nextBallLookupTime;

        public float CourtLength => courtLength;
        public float CourtWidth => courtWidth;
        public float WallThickness => wallThickness;
        public float WallHeight => wallHeight;
        public float BallRecoveryInset => ballRecoveryInset;

        public void SetBall(BasketballBallController value)
        {
            ball = value;
        }

        private void Awake()
        {
            RefreshColliders();
            ResolveBallReference();
        }

        private void FixedUpdate()
        {
            if (ball == null)
            {
                if (Time.unscaledTime < nextBallLookupTime)
                {
                    return;
                }

                nextBallLookupTime = Time.unscaledTime + 1f;
                ResolveBallReference();
                if (ball == null)
                {
                    return;
                }
            }

            RecoverPhysicsBall();
        }

        public bool ContainsWorldPoint(Vector3 worldPosition, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float safeMargin = Mathf.Max(0f, margin);
            float halfLength = Mathf.Max(0f, courtLength * 0.5f - safeMargin);
            float halfWidth = Mathf.Max(0f, courtWidth * 0.5f - safeMargin);
            return Mathf.Abs(local.x) <= halfLength &&
                   Mathf.Abs(local.z) <= halfWidth;
        }

        public Vector3 ClampWorldPosition(Vector3 worldPosition, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float safeMargin = Mathf.Max(0f, margin);
            float halfLength = Mathf.Max(0f, courtLength * 0.5f - safeMargin);
            float halfWidth = Mathf.Max(0f, courtWidth * 0.5f - safeMargin);
            local.x = Mathf.Clamp(local.x, -halfLength, halfLength);
            local.z = Mathf.Clamp(local.z, -halfWidth, halfWidth);
            return transform.TransformPoint(local);
        }

        public void RefreshColliders()
        {
            float thickness = Mathf.Max(0.05f, wallThickness);
            float height = Mathf.Max(1f, wallHeight);
            float length = Mathf.Max(1f, courtLength);
            float width = Mathf.Max(1f, courtWidth);
            float centerY = wallBottom + height * 0.5f;
            float halfThickness = thickness * 0.5f;

            ConfigureWall(
                positiveLengthWall,
                new Vector3(length * 0.5f + halfThickness, centerY, 0f),
                new Vector3(thickness, height, width + thickness * 2f));
            ConfigureWall(
                negativeLengthWall,
                new Vector3(-length * 0.5f - halfThickness, centerY, 0f),
                new Vector3(thickness, height, width + thickness * 2f));
            ConfigureWall(
                positiveWidthWall,
                new Vector3(0f, centerY, width * 0.5f + halfThickness),
                new Vector3(length + thickness * 2f, height, thickness));
            ConfigureWall(
                negativeWidthWall,
                new Vector3(0f, centerY, -width * 0.5f - halfThickness),
                new Vector3(length + thickness * 2f, height, thickness));
        }

        private void ResolveBallReference()
        {
            if (ball == null)
            {
                ball = FindAnyObjectByType<BasketballBallController>(
                    FindObjectsInactive.Exclude);
            }
        }

        private void RecoverPhysicsBall()
        {
            BasketballBallAuthorityState authority = ball.State;
            if (authority != BasketballBallAuthorityState.Released &&
                authority != BasketballBallAuthorityState.FreePhysics)
            {
                return;
            }

            Rigidbody body = ball.Body;
            Vector3 localPosition = transform.InverseTransformPoint(body.position);
            float outerHalfWidth = Mathf.Max(0.5f, courtWidth * 0.5f);
            float outerHalfLength = Mathf.Max(0.5f, courtLength * 0.5f);
            // A ball resting directly against a solid wall can be physically
            // unreachable by a player capsule. Keep the effective recovery
            // zone at least one player-control radius inside the wall even
            // when an older prefab still serializes the former 0.65 m value.
            float reachableInset = Mathf.Max(
                ballRecoveryInset,
                minimumReachableInset,
                ball.Radius + 0.85f);
            float inset = Mathf.Clamp(
                reachableInset,
                0f,
                Mathf.Min(outerHalfWidth, outerHalfLength) - 0.05f);
            float recoveryHalfLength = outerHalfLength - inset;
            float recoveryHalfWidth = outerHalfWidth - inset;

            bool escaped = Mathf.Abs(localPosition.x) >
                               outerHalfLength + hardRecoveryOverflow ||
                           Mathf.Abs(localPosition.z) >
                               outerHalfWidth + hardRecoveryOverflow ||
                           localPosition.y < wallBottom - hardRecoveryBelowCourt;
            if (escaped)
            {
                HardRecoverBall(
                    body,
                    localPosition,
                    recoveryHalfLength,
                    recoveryHalfWidth);
                return;
            }

            bool inEdgeRecoveryZone = Mathf.Abs(localPosition.x) > recoveryHalfLength ||
                                      Mathf.Abs(localPosition.z) > recoveryHalfWidth;
            if (!inEdgeRecoveryZone ||
                localPosition.y > ballRecoveryMaximumHeight ||
                body.linearVelocity.magnitude > ballRecoveryMaximumSpeed)
            {
                return;
            }

            Vector3 target = localPosition;
            target.x = Mathf.Clamp(target.x, -recoveryHalfLength, recoveryHalfLength);
            target.z = Mathf.Clamp(target.z, -recoveryHalfWidth, recoveryHalfWidth);
            Vector3 localDelta = target - localPosition;
            localDelta.y = 0f;
            if (localDelta.sqrMagnitude <= 1e-8f)
            {
                return;
            }

            CancelOutwardVelocity(
                body,
                localPosition,
                recoveryHalfLength,
                recoveryHalfWidth);
            Vector3 targetWorld = transform.TransformPoint(target);
            body.MovePosition(Vector3.MoveTowards(
                body.position,
                targetWorld,
                ballReturnPositionSpeed * Time.fixedDeltaTime));
            Vector3 inward = transform.TransformDirection(localDelta.normalized);
            float inwardSpeed = Vector3.Dot(body.linearVelocity, inward);
            if (inwardSpeed < ballReturnMaximumSpeed)
            {
                body.AddForce(inward * ballReturnAcceleration, ForceMode.Acceleration);
            }
        }

        private void HardRecoverBall(
            Rigidbody body,
            Vector3 localPosition,
            float recoveryHalfLength,
            float recoveryHalfWidth)
        {
            localPosition.x = Mathf.Clamp(
                localPosition.x,
                -recoveryHalfLength,
                recoveryHalfLength);
            localPosition.z = Mathf.Clamp(
                localPosition.z,
                -recoveryHalfWidth,
                recoveryHalfWidth);
            localPosition.y = Mathf.Max(localPosition.y, ball.Radius + 0.02f);
            body.position = transform.TransformPoint(localPosition);

            Vector3 localVelocity = transform.InverseTransformDirection(
                body.linearVelocity);
            localVelocity.x *= 0.25f;
            localVelocity.z *= 0.25f;
            localVelocity.y = Mathf.Max(0f, localVelocity.y);
            body.linearVelocity = transform.TransformDirection(localVelocity);
        }

        private void CancelOutwardVelocity(
            Rigidbody body,
            Vector3 localPosition,
            float recoveryHalfLength,
            float recoveryHalfWidth)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(
                body.linearVelocity);
            if ((localPosition.x > recoveryHalfLength && localVelocity.x > 0f) ||
                (localPosition.x < -recoveryHalfLength && localVelocity.x < 0f))
            {
                localVelocity.x = 0f;
            }
            if ((localPosition.z > recoveryHalfWidth && localVelocity.z > 0f) ||
                (localPosition.z < -recoveryHalfWidth && localVelocity.z < 0f))
            {
                localVelocity.z = 0f;
            }
            body.linearVelocity = transform.TransformDirection(localVelocity);
        }

        private static void ConfigureWall(
            BoxCollider wall,
            Vector3 localPosition,
            Vector3 size)
        {
            if (wall == null)
            {
                return;
            }

            wall.transform.SetLocalPositionAndRotation(
                localPosition,
                Quaternion.identity);
            wall.transform.localScale = Vector3.one;
            wall.center = Vector3.zero;
            wall.size = size;
            wall.isTrigger = false;
        }

#if UNITY_EDITOR
        public void Configure(
            float length,
            float width,
            BoxCollider lengthPositive,
            BoxCollider lengthNegative,
            BoxCollider widthPositive,
            BoxCollider widthNegative,
            float thickness = 0.35f,
            float height = 12f,
            float bottom = -0.25f)
        {
            courtLength = Mathf.Max(1f, length);
            courtWidth = Mathf.Max(1f, width);
            wallThickness = Mathf.Max(0.05f, thickness);
            wallHeight = Mathf.Max(1f, height);
            wallBottom = bottom;
            positiveLengthWall = lengthPositive;
            negativeLengthWall = lengthNegative;
            positiveWidthWall = widthPositive;
            negativeWidthWall = widthNegative;
            RefreshColliders();
        }

        private void OnValidate()
        {
            RefreshColliders();
        }
#endif
    }
}
