using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public static class BasketballBallTrajectoryPredictor
    {
        private const float Epsilon = 1e-5f;

        public static Vector3 EvaluatePosition(
            Vector3 position,
            Vector3 velocity,
            Vector3 gravity,
            float time)
        {
            float clampedTime = Mathf.Max(0f, time);
            return position + velocity * clampedTime +
                   0.5f * gravity * clampedTime * clampedTime;
        }

        public static bool TryPredictPlaneContact(
            Vector3 position,
            Vector3 velocity,
            Vector3 gravity,
            Vector3 planePoint,
            Vector3 planeNormal,
            float sphereRadius,
            float maximumTime,
            out Vector3 contactCenter,
            out float contactTime)
        {
            contactCenter = position;
            contactTime = 0f;
            if (!IsFinite(position) || !IsFinite(velocity) ||
                !IsFinite(gravity) || !IsFinite(planePoint) ||
                !IsFinite(planeNormal) || !IsFinite(sphereRadius) ||
                !IsFinite(maximumTime) || maximumTime < 0f ||
                planeNormal.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            Vector3 normal = planeNormal.normalized;
            float radius = Mathf.Max(0f, sphereRadius);
            float a = 0.5f * Vector3.Dot(gravity, normal);
            float b = Vector3.Dot(velocity, normal);
            float c = Vector3.Dot(position - planePoint, normal) - radius;

            if (c <= 0.01f)
            {
                contactCenter = ProjectCenterToPlane(position, planePoint, normal, radius);
                return true;
            }

            float time = float.PositiveInfinity;
            if (Mathf.Abs(a) <= Epsilon)
            {
                if (b >= -Epsilon)
                {
                    return false;
                }
                time = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f)
                {
                    return false;
                }
                float root = Mathf.Sqrt(discriminant);
                float denominator = 2f * a;
                float first = (-b - root) / denominator;
                float second = (-b + root) / denominator;
                if (first >= 0f)
                {
                    time = first;
                }
                if (second >= 0f && second < time)
                {
                    time = second;
                }
            }

            if (!IsFinite(time) || time < 0f || time > maximumTime)
            {
                return false;
            }

            contactTime = time;
            contactCenter = ProjectCenterToPlane(
                EvaluatePosition(position, velocity, gravity, time),
                planePoint,
                normal,
                radius);
            return IsFinite(contactCenter);
        }

        public static bool TryFindEarliestReachableIntercept(
            Vector3 playerPosition,
            float playerSpeed,
            float reactionTime,
            float reachRadius,
            Vector3 ballPosition,
            Vector3 ballVelocity,
            Vector3 gravity,
            Vector3 courtPoint,
            Vector3 courtNormal,
            float minimumCatchHeight,
            float maximumCatchHeight,
            float maximumTime,
            int sampleCount,
            float arrivalSlack,
            out Vector3 rootTarget,
            out float interceptTime,
            out float arrivalMargin)
        {
            rootTarget = playerPosition;
            interceptTime = 0f;
            arrivalMargin = float.NegativeInfinity;
            if (!IsFinite(playerPosition) || !IsFinite(ballPosition) ||
                !IsFinite(ballVelocity) || !IsFinite(gravity) ||
                !IsFinite(courtPoint) || !IsFinite(courtNormal) ||
                courtNormal.sqrMagnitude <= Epsilon || playerSpeed <= Epsilon ||
                maximumTime <= Epsilon)
            {
                return false;
            }

            Vector3 normal = courtNormal.normalized;
            Vector3 groundedPlayer = ProjectPointToPlane(playerPosition, courtPoint, normal);
            int samples = Mathf.Clamp(sampleCount, 2, 64);
            float minimumHeight = Mathf.Max(0f, minimumCatchHeight);
            float maximumHeight = Mathf.Max(minimumHeight, maximumCatchHeight);
            float safeReactionTime = Mathf.Max(0f, reactionTime);
            float safeReachRadius = Mathf.Max(0f, reachRadius);
            float safeSlack = Mathf.Max(0f, arrivalSlack);

            for (int sample = 1; sample <= samples; sample++)
            {
                float time = maximumTime * sample / samples;
                Vector3 ballPoint = EvaluatePosition(
                    ballPosition,
                    ballVelocity,
                    gravity,
                    time);
                float height = Vector3.Dot(ballPoint - courtPoint, normal);
                if (height < minimumHeight || height > maximumHeight)
                {
                    continue;
                }

                Vector3 target = ProjectPointToPlane(ballPoint, courtPoint, normal);
                float planarDistance = Vector3.Distance(groundedPlayer, target);
                float travelDistance = Mathf.Max(0f, planarDistance - safeReachRadius);
                float playerArrivalTime = safeReactionTime + travelDistance / playerSpeed;
                float margin = time + safeSlack - playerArrivalTime;
                if (margin < 0f)
                {
                    continue;
                }

                rootTarget = target;
                interceptTime = time;
                arrivalMargin = margin;
                return true;
            }
            return false;
        }

        private static Vector3 ProjectCenterToPlane(
            Vector3 center,
            Vector3 planePoint,
            Vector3 normal,
            float radius)
        {
            float height = Vector3.Dot(center - planePoint, normal);
            return center - normal * (height - radius);
        }

        private static Vector3 ProjectPointToPlane(
            Vector3 point,
            Vector3 planePoint,
            Vector3 normal)
        {
            return point - normal * Vector3.Dot(point - planePoint, normal);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
