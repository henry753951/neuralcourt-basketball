using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public static class BasketballPassReleaseDetector
    {
        public static float Evaluate(
            in BasketballPassPlan plan,
            in BasketballBallObservation observation)
        {
            float speed = observation.Velocity.magnitude;
            if (speed < 0.5f)
            {
                return 0f;
            }

            Vector3 desired = plan.DesiredReleaseVelocity;
            float desiredSpeed = Mathf.Max(desired.magnitude, 0.001f);
            float alignment = Mathf.InverseLerp(
                0.35f,
                1f,
                Vector3.Dot(observation.Velocity / speed, desired / desiredSpeed));
            float speedQuality = 1f - Mathf.Clamp01(
                Mathf.Abs(speed - desiredSpeed) / desiredSpeed);
            float verticalQuality = 1f - Mathf.Clamp01(
                Mathf.Abs(observation.Velocity.y - desired.y) /
                Mathf.Max(3f, Mathf.Abs(desired.y) + 1f));
            Vector3 rootToBall = observation.Position - observation.RootPosition;
            float forwardQuality = rootToBall.sqrMagnitude > 1e-8f
                ? Mathf.InverseLerp(
                    -0.1f,
                    0.65f,
                    Vector3.Dot(rootToBall.normalized, observation.RootForward))
                : 0f;
            float contactRelease = 1f - observation.HandContact;

            return 0.35f * alignment +
                   0.2f * speedQuality +
                   0.15f * verticalQuality +
                   0.15f * forwardQuality +
                   0.15f * contactRelease;
        }

        public static bool TryBuildReleaseVelocity(
            in BasketballPassPlan plan,
            in BasketballBallObservation observation,
            out Vector3 releaseVelocity)
        {
            releaseVelocity = observation.Velocity;
            float modelSpeed = releaseVelocity.magnitude;
            float desiredSpeed = plan.DesiredReleaseVelocity.magnitude;
            if (modelSpeed < 0.5f || desiredSpeed < 0.5f)
            {
                return false;
            }

            MeasureErrors(
                plan,
                observation,
                out float angle,
                out float speedScale,
                out float verticalError);
            float minimumScale = 1f / Mathf.Max(plan.MaximumSpeedScale, 1f);
            if (angle > plan.MaximumDirectionCorrection ||
                speedScale < minimumScale ||
                speedScale > plan.MaximumSpeedScale ||
                verticalError > plan.MaximumVerticalCorrection)
            {
                return false;
            }

            Quaternion correction = Quaternion.FromToRotation(
                releaseVelocity.normalized,
                plan.DesiredReleaseDirection);
            correction = Quaternion.Slerp(
                Quaternion.identity,
                correction,
                Mathf.Clamp01(plan.MaximumDirectionCorrection / Mathf.Max(angle, 0.001f)));
            releaseVelocity = correction * releaseVelocity;
            releaseVelocity *= Mathf.Clamp(speedScale, minimumScale, plan.MaximumSpeedScale);
            releaseVelocity.y = Mathf.MoveTowards(
                releaseVelocity.y,
                plan.DesiredReleaseVelocity.y,
                plan.MaximumVerticalCorrection);
            return true;
        }

        public static void MeasureErrors(
            in BasketballPassPlan plan,
            in BasketballBallObservation observation,
            out float directionError,
            out float speedScale,
            out float verticalError)
        {
            float modelSpeed = observation.Velocity.magnitude;
            float desiredSpeed = plan.DesiredReleaseVelocity.magnitude;
            directionError = modelSpeed > 0.001f && desiredSpeed > 0.001f
                ? Vector3.Angle(observation.Velocity, plan.DesiredReleaseVelocity)
                : 180f;
            speedScale = modelSpeed > 0.001f
                ? desiredSpeed / modelSpeed
                : float.PositiveInfinity;
            verticalError = Mathf.Abs(
                observation.Velocity.y - plan.DesiredReleaseVelocity.y);
        }
    }
}
