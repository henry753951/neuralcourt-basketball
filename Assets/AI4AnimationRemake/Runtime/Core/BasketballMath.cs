using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    internal static class BasketballMath
    {
        public static Vector3 RelativePosition(Vector3 world, Vector3 rootPosition, Quaternion rootRotation)
            => Quaternion.Inverse(rootRotation) * (world - rootPosition);

        public static Vector3 RelativeDirection(Vector3 world, Quaternion rootRotation)
            => Quaternion.Inverse(rootRotation) * world;

        public static Vector3 WorldPosition(Vector3 local, Vector3 rootPosition, Quaternion rootRotation)
            => rootPosition + rootRotation * local;

        public static Vector3 WorldDirection(Vector3 local, Quaternion rootRotation)
            => rootRotation * local;

        public static float GetControl(int index, float bias, float min = 0f, float max = 1f)
            => ActivateCurve((float)(index - BasketballAgentState.Pivot) /
                (BasketballAgentState.SampleCount - 1 - BasketballAgentState.Pivot), bias, min, max);

        public static float GetCorrection(int index, float bias, float max = 1f, float min = 0f)
            => ActivateCurve((float)(index - BasketballAgentState.Pivot) /
                (BasketballAgentState.SampleCount - 1 - BasketballAgentState.Pivot), bias, max, min);

        public static float ActivateCurve(float value, float bias, float start, float end)
        {
            bias = Mathf.Clamp01(bias);
            float result;
            if (end < start)
            {
                result = 1f - Mathf.Pow(1f - Mathf.Pow(1f - value, 1f - bias), bias);
                return Mathf.Lerp(end, start, result);
            }

            result = 1f - Mathf.Pow(1f - Mathf.Pow(value, 1f - bias), bias);
            return Mathf.Lerp(start, end, result);
        }

        public static Vector3 InterpolatePivot(Vector3 from, Vector3 to, float horizontal, float height)
        {
            Vector3 fromHorizontal = new(from.x, 0f, from.z);
            Vector3 toHorizontal = new(to.x, 0f, to.z);
            float magnitude = Mathf.Lerp(fromHorizontal.magnitude, toHorizontal.magnitude, horizontal);
            Vector3 direction = Vector3.Lerp(fromHorizontal, toHorizontal, horizontal).normalized;
            return new Vector3(direction.x * magnitude, Mathf.Lerp(from.y, to.y, height), direction.z * magnitude);
        }

        public static Vector3 InterpolateMomentum(Vector3 from, Vector3 to, float horizontal, float height)
            => new(
                Mathf.Lerp(from.x, to.x, horizontal),
                Mathf.Lerp(from.y, to.y, height),
                Mathf.Lerp(from.z, to.z, horizontal));

        public static float SmoothStep(float value, float power, float threshold)
        {
            value = Mathf.Clamp01(value);
            power = Mathf.Max(power, 0f);
            threshold = Mathf.Clamp01(threshold);
            if (threshold == 0f || threshold == 1f)
            {
                value = 1f - threshold;
            }
            else if (threshold < 0.5f)
            {
                value = 1f - Mathf.Pow(1f - value, 0.5f / threshold);
            }
            else if (threshold > 0.5f)
            {
                value = Mathf.Pow(value, 0.5f / (1f - threshold));
            }

            if (value < 0.5f)
            {
                return 0.5f * Mathf.Pow(2f * value, power);
            }
            if (value > 0.5f)
            {
                return 1f - 0.5f * Mathf.Pow(2f - 2f * value, power);
            }
            return 0.5f;
        }

        public static Vector2 PhaseVector(float phase, float magnitude = 1f)
        {
            float radians = phase * 2f * Mathf.PI;
            return magnitude * new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        public static float PhaseValue(Vector2 phase)
        {
            if (phase.sqrMagnitude == 0f)
            {
                return 0f;
            }
            float angle = -Vector2.SignedAngle(Vector2.up, phase.normalized);
            if (angle < 0f)
            {
                angle += 360f;
            }
            return Mathf.Repeat(angle / 360f, 1f);
        }

        public static float SignedPhaseUpdate(float from, float to)
            => -Vector2.SignedAngle(PhaseVector(from), PhaseVector(to)) / 360f;

        public static Quaternion LookRotation(Vector3 forward, Vector3 up, Quaternion fallback)
        {
            if (forward.sqrMagnitude < 1e-10f || up.sqrMagnitude < 1e-10f)
            {
                return fallback;
            }
            return Quaternion.LookRotation(forward.normalized, up.normalized);
        }
    }
}
