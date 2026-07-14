using System;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballOutputDecoder
    {
        private const float ContactPower = 3f;
        private const float BoneContactThreshold = 0.5f;
        private const float BallContactThreshold = 0.1f;
        private const float PhaseAmplitudeSafetyLimit = 20f;
        private static readonly ProfilerMarker DecodeMarker = new("Basketball.Decode");

        public void Decode(BasketballAgentState state, ReadOnlySpan<float> output)
        {
            if (output.Length != BasketballModelContract.OutputFeatureCount)
            {
                throw new ArgumentException("Basketball output buffer must contain exactly 588 floats.", nameof(output));
            }

            using (DecodeMarker.Auto())
            {
                state.ShiftPastOutputSeries();
                int cursor = 0;
                int pivot = BasketballAgentState.Pivot;
                Vector3 oldRootPosition = state.ActorRootPosition;
                Quaternion oldRootRotation = state.ActorRootRotation;

                Vector3 offset = ReadVector3(output, ref cursor);
                offset = Vector3.Lerp(offset, Vector3.zero,
                    state.Styles[BasketballAgentState.StyleIndex(pivot, 0)]);
                Vector3 rootPosition = oldRootPosition + oldRootRotation * new Vector3(offset.x, 0f, offset.z);
                Quaternion rootRotation = oldRootRotation * Quaternion.AngleAxis(offset.y, Vector3.up);
                state.RootPositions[pivot] = rootPosition;
                state.RootRotations[pivot] = rootRotation;
                state.ActorRootPosition = rootPosition;
                state.ActorRootRotation = rootRotation;
                state.RootVelocities[pivot] = BasketballMath.WorldDirection(ReadXZ(output, ref cursor), rootRotation);

                state.Pivots[pivot] = BasketballMath.InterpolatePivot(
                    state.Pivots[pivot], ReadVector3(output, ref cursor),
                    BallCorrection(pivot, state.BallHorizontalControl, 0.2f),
                    BallCorrection(pivot, state.BallHeightControl, 0.1f));
                state.Momentums[pivot] = BasketballMath.InterpolateMomentum(
                    state.Momentums[pivot], ReadVector3(output, ref cursor),
                    BallCorrection(pivot, state.BallHorizontalControl, 0.2f),
                    BallCorrection(pivot, state.BallSpeedControl, 0.1f));

                for (int style = 0; style < BasketballAgentState.StyleCount; style++)
                {
                    int index = BasketballAgentState.StyleIndex(pivot, style);
                    state.Styles[index] = Mathf.Lerp(
                        state.Styles[index], Mathf.Clamp01(Read(output, ref cursor)),
                        BasketballMath.GetCorrection(pivot, StyleCorrectionBias(state, style)));
                }

                for (int key = BasketballAgentState.PastKeys + 1;
                     key < BasketballAgentState.KeyCount;
                     key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    Vector3 predictedPosition = BasketballMath.WorldPosition(
                        ReadXZ(output, ref cursor), rootPosition, rootRotation);
                    Vector3 predictedDirection = BasketballMath.WorldDirection(
                        ReadXZ(output, ref cursor), rootRotation).normalized;
                    Quaternion predictedRotation = predictedDirection.sqrMagnitude > 1e-10f
                        ? Quaternion.LookRotation(predictedDirection, Vector3.up)
                        : state.RootRotations[sample];
                    state.RootPositions[sample] = Vector3.Lerp(
                        state.RootPositions[sample], predictedPosition,
                        BasketballMath.GetCorrection(sample, 0.25f));
                    state.RootRotations[sample] = Quaternion.Slerp(
                        state.RootRotations[sample], predictedRotation,
                        BasketballMath.GetCorrection(sample, 0.25f));
                    state.RootVelocities[sample] = Vector3.Lerp(
                        state.RootVelocities[sample],
                        BasketballMath.WorldDirection(ReadXZ(output, ref cursor), rootRotation),
                        BasketballMath.GetCorrection(sample, 0.25f));
                    state.Pivots[sample] = BasketballMath.InterpolatePivot(
                        state.Pivots[sample], ReadVector3(output, ref cursor),
                        BallCorrection(sample, state.BallHorizontalControl, 0.2f),
                        BallCorrection(sample, state.BallHeightControl, 0.1f));
                    state.Momentums[sample] = BasketballMath.InterpolateMomentum(
                        state.Momentums[sample], ReadVector3(output, ref cursor),
                        BallCorrection(sample, state.BallHorizontalControl, 0.2f),
                        BallCorrection(sample, state.BallSpeedControl, 0.1f));
                    for (int style = 0; style < BasketballAgentState.StyleCount; style++)
                    {
                        int index = BasketballAgentState.StyleIndex(sample, style);
                        state.Styles[index] = Mathf.Lerp(
                            state.Styles[index], Mathf.Clamp01(Read(output, ref cursor)),
                            BasketballMath.GetCorrection(sample, StyleCorrectionBias(state, style)));
                    }
                }

                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    Vector3 targetPosition = BasketballMath.WorldPosition(
                        ReadVector3(output, ref cursor), rootPosition, rootRotation);
                    Vector3 forward = BasketballMath.WorldDirection(
                        ReadVector3(output, ref cursor).normalized, rootRotation);
                    Vector3 up = BasketballMath.WorldDirection(
                        ReadVector3(output, ref cursor).normalized, rootRotation);
                    Vector3 velocity = BasketballMath.WorldDirection(
                        ReadVector3(output, ref cursor), rootRotation);
                    state.BoneVelocities[bone] = velocity;
                    state.BonePositions[bone] = Vector3.Lerp(
                        state.BonePositions[bone] + velocity / BasketballAgentState.Framerate,
                        targetPosition,
                        0.5f);
                    state.BoneRotations[bone] = BasketballMath.LookRotation(
                        forward, up, state.BoneRotations[bone]);
                }

                float controlWeight = Mathf.Clamp01(Read(output, ref cursor));
                Vector3 predictedBallPosition = ReadVector3(output, ref cursor);
                Vector3 predictedBallVelocity = ReadVector3(output, ref cursor);
                Vector3 ballForward = ReadVector3(output, ref cursor);
                Vector3 ballUp = ReadVector3(output, ref cursor);
                if (state.Carrier && controlWeight > 0f)
                {
                    predictedBallPosition = BasketballMath.WorldPosition(
                        predictedBallPosition / controlWeight, rootPosition, rootRotation);
                    predictedBallVelocity = BasketballMath.WorldDirection(
                        predictedBallVelocity / controlWeight, rootRotation);
                    Quaternion predictedBallRotation = BasketballMath.LookRotation(
                        (ballForward / controlWeight).normalized,
                        (ballUp / controlWeight).normalized,
                        Quaternion.identity);
                    float hold = state.Styles[BasketballAgentState.StyleIndex(pivot, 3)];
                    state.BallRotations[pivot] = state.BallRotations[pivot] *
                        Quaternion.Slerp(predictedBallRotation, Quaternion.identity, hold);
                    Vector3 previousBallPosition = state.BallPositions[pivot];
                    Vector3 blendedBallPosition = Vector3.Lerp(
                        predictedBallPosition,
                        previousBallPosition + predictedBallVelocity / BasketballAgentState.Framerate,
                        0.5f);
                    state.BallPositions[pivot] = previousBallPosition + Vector3.ClampMagnitude(
                        blendedBallPosition - previousBallPosition,
                        2f * predictedBallVelocity.magnitude / BasketballAgentState.Framerate);
                    state.BallVelocities[pivot] = predictedBallVelocity;
                }

                for (int contact = 0; contact < BasketballAgentState.ContactCount; contact++)
                {
                    float threshold = contact == 4 ? BallContactThreshold : BoneContactThreshold;
                    state.Contacts[BasketballAgentState.ContactIndex(pivot, contact)] =
                        BasketballMath.SmoothStep(
                            Mathf.Clamp01(Read(output, ref cursor)), ContactPower, threshold);
                }

                for (int key = BasketballAgentState.PastKeys;
                     key < BasketballAgentState.KeyCount;
                     key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    float stability = BasketballMath.GetCorrection(sample, 1f, 0.9f, 0.1f);
                    for (int phase = 0; phase < BasketballAgentState.PhaseCount; phase++)
                    {
                        Vector2 update = ReadVector2(output, ref cursor);
                        Vector2 predictedState = ReadVector2(output, ref cursor);
                        int index = BasketballAgentState.PhaseIndex(sample, phase);
                        float currentPhase = state.Phases[index];
                        Vector2 integrated = BasketballMath.PhaseVector(
                            Mathf.Repeat(currentPhase + BasketballMath.PhaseValue(update), 1f));
                        Vector2 corrected = BasketballMath.PhaseVector(
                            Mathf.Repeat(currentPhase + BasketballMath.SignedPhaseUpdate(
                                currentPhase, BasketballMath.PhaseValue(predictedState)), 1f));
                        Vector2 phaseVector = Vector2.Lerp(integrated, corrected, stability).normalized;
                        // Normal model amplitudes remain well below this limit. Until the
                        // legacy contact/IK feedback path is fully restored, an outlier can
                        // otherwise feed exponentially larger values into the next gating
                        // input and eventually overflow the closed loop.
                        state.Amplitudes[index] = Mathf.Min(
                            update.magnitude, PhaseAmplitudeSafetyLimit);
                        state.Phases[index] = BasketballMath.PhaseValue(phaseVector);
                    }
                }

                InterpolateFuture(state);
                state.TickCount++;
                if (cursor != output.Length)
                {
                    throw new InvalidOperationException($"Output decoder read {cursor} values instead of 588.");
                }
            }
        }

        private static void InterpolateFuture(BasketballAgentState state)
        {
            for (int sample = BasketballAgentState.Pivot;
                 sample < BasketballAgentState.SampleCount;
                 sample++)
            {
                int remainder = sample % BasketballAgentState.Resolution;
                if (remainder == 0)
                {
                    continue;
                }
                int previous = sample - remainder;
                int next = previous + BasketballAgentState.Resolution;
                float weight = (float)remainder / BasketballAgentState.Resolution;
                state.RootPositions[sample] = Vector3.Lerp(
                    state.RootPositions[previous], state.RootPositions[next], weight);
                Vector3 direction = Vector3.Lerp(
                    state.RootRotations[previous] * Vector3.forward,
                    state.RootRotations[next] * Vector3.forward,
                    weight).normalized;
                state.RootRotations[sample] = direction.sqrMagnitude > 1e-10f
                    ? Quaternion.LookRotation(direction, Vector3.up)
                    : state.RootRotations[previous];
                state.RootVelocities[sample] = Vector3.Lerp(
                    state.RootVelocities[previous], state.RootVelocities[next], weight);
                state.Pivots[sample] = BasketballMath.InterpolatePivot(
                    state.Pivots[previous], state.Pivots[next], weight, weight);
                state.Momentums[sample] = BasketballMath.InterpolateMomentum(
                    state.Momentums[previous], state.Momentums[next], weight, weight);
                for (int style = 0; style < BasketballAgentState.StyleCount; style++)
                {
                    state.Styles[BasketballAgentState.StyleIndex(sample, style)] = Mathf.Lerp(
                        state.Styles[BasketballAgentState.StyleIndex(previous, style)],
                        state.Styles[BasketballAgentState.StyleIndex(next, style)], weight);
                }
                for (int phase = 0; phase < BasketballAgentState.PhaseCount; phase++)
                {
                    int previousIndex = BasketballAgentState.PhaseIndex(previous, phase);
                    int nextIndex = BasketballAgentState.PhaseIndex(next, phase);
                    int index = BasketballAgentState.PhaseIndex(sample, phase);
                    Vector2 phaseVector = Vector2.Lerp(
                        BasketballMath.PhaseVector(state.Phases[previousIndex]),
                        BasketballMath.PhaseVector(state.Phases[nextIndex]), weight).normalized;
                    state.Phases[index] = BasketballMath.PhaseValue(phaseVector);
                    state.Amplitudes[index] = Mathf.Lerp(
                        state.Amplitudes[previousIndex], state.Amplitudes[nextIndex], weight);
                }
            }
        }

        private static float Read(ReadOnlySpan<float> values, ref int cursor) => values[cursor++];

        private static float BallCorrection(int sample, bool active, float activeBias)
            => BasketballMath.GetCorrection(sample, active ? activeBias : 1f, 0.5f, 0f);

        private static float StyleCorrectionBias(BasketballAgentState state, int style)
        {
            return style switch
            {
                0 => 0.1f,
                1 => 0.1f,
                2 => state.HoldIntent || state.ShootIntent ? 0.1f : 0f,
                3 => state.HoldIntent ? 0.1f : 0f,
                _ => state.ShootIntent ? 0.1f : 0f
            };
        }

        private static Vector2 ReadVector2(ReadOnlySpan<float> values, ref int cursor)
            => new(Read(values, ref cursor), Read(values, ref cursor));

        private static Vector3 ReadVector3(ReadOnlySpan<float> values, ref int cursor)
            => new(Read(values, ref cursor), Read(values, ref cursor), Read(values, ref cursor));

        private static Vector3 ReadXZ(ReadOnlySpan<float> values, ref int cursor)
            => new(Read(values, ref cursor), 0f, Read(values, ref cursor));
    }
}
