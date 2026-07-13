using System;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballFeatureBuilder
    {
        private static readonly ProfilerMarker BuildMarker = new("Basketball.BuildFeatures");

        public void Build(BasketballAgentState state, Span<float> input)
        {
            if (input.Length != BasketballModelAsset.InputFeatureCount)
            {
                throw new ArgumentException("Basketball input buffer must contain exactly 864 floats.", nameof(input));
            }

            using (BuildMarker.Auto())
            {
                int cursor = 0;
                Vector3 rootPosition = state.ActorRootPosition;
                Quaternion rootRotation = state.ActorRootRotation;

                for (int key = 0; key < BasketballAgentState.KeyCount; key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    WriteXZ(input, ref cursor, BasketballMath.RelativePosition(
                        state.RootPositions[sample], rootPosition, rootRotation));
                    WriteXZ(input, ref cursor, BasketballMath.RelativeDirection(
                        state.RootRotations[sample] * Vector3.forward, rootRotation));
                    WriteXZ(input, ref cursor, BasketballMath.RelativeDirection(
                        state.RootVelocities[sample], rootRotation));
                    Write(input, ref cursor, state.Pivots[sample]);
                    Write(input, ref cursor, state.Momentums[sample]);
                    for (int style = 0; style < BasketballAgentState.StyleCount; style++)
                    {
                        input[cursor++] = state.Styles[BasketballAgentState.StyleIndex(sample, style)];
                    }
                }

                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    Write(input, ref cursor, BasketballMath.RelativePosition(
                        state.BonePositions[bone], rootPosition, rootRotation));
                    Write(input, ref cursor, BasketballMath.RelativeDirection(
                        state.BoneRotations[bone] * Vector3.forward, rootRotation));
                    Write(input, ref cursor, BasketballMath.RelativeDirection(
                        state.BoneRotations[bone] * Vector3.up, rootRotation));
                    Write(input, ref cursor, BasketballMath.RelativeDirection(
                        state.BoneVelocities[bone], rootRotation));
                }

                for (int key = 0; key <= BasketballAgentState.PastKeys; key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    float controlWeight = 1f - Vector3.Distance(
                        new Vector3(rootPosition.x, 0f, rootPosition.z),
                        new Vector3(state.BallPositions[sample].x, 0f, state.BallPositions[sample].z)) /
                        BasketballAgentState.ControlRadius;
                    controlWeight = Mathf.Clamp01(controlWeight);
                    input[cursor++] = controlWeight;
                    Vector3 weightedPosition = rootPosition +
                        controlWeight * (state.BallPositions[sample] - rootPosition);
                    Write(input, ref cursor, BasketballMath.RelativePosition(
                        weightedPosition, rootPosition, rootRotation));
                    Write(input, ref cursor, BasketballMath.RelativeDirection(
                        controlWeight * state.BallVelocities[sample], rootRotation));
                }

                for (int key = 0; key <= BasketballAgentState.PastKeys; key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    for (int contact = 0; contact < BasketballAgentState.ContactCount; contact++)
                    {
                        input[cursor++] = state.Contacts[BasketballAgentState.ContactIndex(sample, contact)];
                    }
                }

                for (int key = 0; key < BasketballAgentState.KeyCount; key++)
                {
                    input[cursor++] = 0f;
                }
                for (int key = 0; key < BasketballAgentState.KeyCount; key++)
                {
                    for (int value = 0; value < 6; value++)
                    {
                        input[cursor++] = 0f;
                    }
                }
                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    input[cursor++] = BasketballAgentState.InteractionRadius;
                }

                for (int key = 0; key < BasketballAgentState.KeyCount; key++)
                {
                    int sample = BasketballAgentState.KeyIndex(key);
                    for (int phase = 0; phase < BasketballAgentState.PhaseCount; phase++)
                    {
                        int index = BasketballAgentState.PhaseIndex(sample, phase);
                        Vector2 alignment = BasketballMath.PhaseVector(
                            state.Phases[index], state.Amplitudes[index]);
                        input[cursor++] = alignment.x;
                        input[cursor++] = alignment.y;
                    }
                }

                if (cursor != input.Length)
                {
                    throw new InvalidOperationException($"Feature builder wrote {cursor} values instead of 864.");
                }
            }
        }

        private static void Write(Span<float> output, ref int cursor, Vector3 value)
        {
            output[cursor++] = value.x;
            output[cursor++] = value.y;
            output[cursor++] = value.z;
        }

        private static void WriteXZ(Span<float> output, ref int cursor, Vector3 value)
        {
            output[cursor++] = value.x;
            output[cursor++] = value.z;
        }
    }
}
