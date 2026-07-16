using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballCourt : MonoBehaviour
    {
        [Header("FIBA Court")]
        [SerializeField, Min(1f)] private float courtLength = 28f;
        [SerializeField, Min(1f)] private float courtWidth = 15f;
        [SerializeField, Min(0.1f)] private float threePointRadius = 6.75f;
        [SerializeField, Min(0.1f)] private float threePointCornerDistance = 6.6f;
        [SerializeField] private BasketballHoop positiveZHoop;
        [SerializeField] private BasketballHoop negativeZHoop;

        public float CourtLength => courtLength;
        public float CourtWidth => courtWidth;
        public float ThreePointRadius => threePointRadius;
        public BasketballHoop PositiveZHoop => positiveZHoop;
        public BasketballHoop NegativeZHoop => negativeZHoop;
        public Vector3 CourtCenter
        {
            get
            {
                ResolveCourtFrame(
                    out Vector3 center,
                    out _,
                    out _,
                    out _);
                return center;
            }
        }
        public Vector3 CourtRight
        {
            get
            {
                ResolveCourtFrame(
                    out _,
                    out Vector3 right,
                    out _,
                    out _);
                return right;
            }
        }
        public Vector3 CourtUp
        {
            get
            {
                ResolveCourtFrame(
                    out _,
                    out _,
                    out Vector3 up,
                    out _);
                return up;
            }
        }
        public Vector3 CourtForward
        {
            get
            {
                ResolveCourtFrame(
                    out _,
                    out _,
                    out _,
                    out Vector3 forward);
                return forward;
            }
        }

        public BasketballHoop GetAttackHoop(int teamId)
        {
            if (positiveZHoop != null && positiveZHoop.AttackedByTeamId == teamId)
            {
                return positiveZHoop;
            }
            if (negativeZHoop != null && negativeZHoop.AttackedByTeamId == teamId)
            {
                return negativeZHoop;
            }
            return teamId == 0 ? positiveZHoop : negativeZHoop;
        }

        /// <summary>
        /// Converts a world point into the canonical basketball frame:
        /// X spans the sidelines, Y is court up, and +Z points from the
        /// negative hoop toward the positive hoop. The frame is derived from
        /// the actual hoop centers so legacy parent rotations cannot rotate
        /// team tactics away from the rendered court.
        /// </summary>
        public Vector3 WorldToCourtPoint(Vector3 worldPoint)
        {
            ResolveCourtFrame(
                out Vector3 center,
                out Vector3 right,
                out Vector3 up,
                out Vector3 forward);
            Vector3 delta = worldPoint - center;
            return new Vector3(
                Vector3.Dot(delta, right),
                Vector3.Dot(delta, up),
                Vector3.Dot(delta, forward));
        }

        public Vector3 CourtToWorldPoint(Vector3 courtPoint)
        {
            ResolveCourtFrame(
                out Vector3 center,
                out Vector3 right,
                out Vector3 up,
                out Vector3 forward);
            return center +
                   right * courtPoint.x +
                   up * courtPoint.y +
                   forward * courtPoint.z;
        }

        public Vector3 WorldToCourtDirection(Vector3 worldDirection)
        {
            ResolveCourtFrame(
                out _,
                out Vector3 right,
                out Vector3 up,
                out Vector3 forward);
            return new Vector3(
                Vector3.Dot(worldDirection, right),
                Vector3.Dot(worldDirection, up),
                Vector3.Dot(worldDirection, forward));
        }

        public Vector3 CourtToWorldDirection(Vector3 courtDirection)
        {
            ResolveCourtFrame(
                out _,
                out Vector3 right,
                out Vector3 up,
                out Vector3 forward);
            return right * courtDirection.x +
                   up * courtDirection.y +
                   forward * courtDirection.z;
        }

        public Vector3 ClampToPlayableArea(
            Vector3 worldPoint,
            float margin = 0f,
            bool preserveHeight = true)
        {
            Vector3 local = WorldToCourtPoint(worldPoint);
            float safeMargin = Mathf.Max(0f, margin);
            float halfWidth = Mathf.Max(0f, courtWidth * 0.5f - safeMargin);
            float halfLength = Mathf.Max(0f, courtLength * 0.5f - safeMargin);
            local.x = Mathf.Clamp(local.x, -halfWidth, halfWidth);
            local.z = Mathf.Clamp(local.z, -halfLength, halfLength);
            if (!preserveHeight)
            {
                local.y = 0f;
            }
            return CourtToWorldPoint(local);
        }

        public bool IsThreePoint(Vector3 releasePosition, BasketballHoop hoop)
        {
            if (hoop == null)
            {
                return false;
            }

            Vector3 localRelease = WorldToCourtPoint(releasePosition);
            Vector3 localRim = WorldToCourtPoint(hoop.Center);
            float sign = localRim.z >= 0f ? 1f : -1f;
            float cornerX = Mathf.Min(
                threePointCornerDistance,
                threePointRadius - 0.001f);
            float arcZOffset = Mathf.Sqrt(Mathf.Max(
                0f,
                threePointRadius * threePointRadius - cornerX * cornerX));
            float straightIntersectionZ = localRim.z - sign * arcZOffset;
            bool inCornerSection = sign > 0f
                ? localRelease.z >= straightIntersectionZ
                : localRelease.z <= straightIntersectionZ;
            if (inCornerSection)
            {
                return Mathf.Abs(localRelease.x) >= cornerX;
            }

            Vector2 releaseXZ = new(localRelease.x, localRelease.z);
            Vector2 rimXZ = new(localRim.x, localRim.z);
            return Vector2.Distance(releaseXZ, rimXZ) >= threePointRadius;
        }

        private void ResolveCourtFrame(
            out Vector3 center,
            out Vector3 right,
            out Vector3 up,
            out Vector3 forward)
        {
            up = transform.up.sqrMagnitude > 1e-8f
                ? transform.up.normalized
                : Vector3.up;
            forward = Vector3.ProjectOnPlane(transform.forward, up);
            Vector3 midpoint = transform.position;
            if (positiveZHoop != null && negativeZHoop != null)
            {
                Vector3 hoopAxis = Vector3.ProjectOnPlane(
                    positiveZHoop.Center - negativeZHoop.Center,
                    up);
                if (hoopAxis.sqrMagnitude > 1e-8f)
                {
                    forward = hoopAxis.normalized;
                }
                midpoint = 0.5f *
                           (positiveZHoop.Center + negativeZHoop.Center);
            }
            if (forward.sqrMagnitude <= 1e-8f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude <= 1e-8f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }
            forward = Vector3.Cross(right, up).normalized;

            Vector3 floorOrigin = transform.position;
            center = midpoint -
                     up * Vector3.Dot(midpoint - floorOrigin, up);
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballHoop positive,
            BasketballHoop negative,
            float length = 28f,
            float width = 15f)
        {
            positiveZHoop = positive;
            negativeZHoop = negative;
            courtLength = Mathf.Max(1f, length);
            courtWidth = Mathf.Max(1f, width);
            threePointRadius = 6.75f;
            threePointCornerDistance = 6.6f;
        }
#endif
    }
}
