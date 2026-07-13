using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballAgentState
    {
        public const int PastKeys = 6;
        public const int FutureKeys = 6;
        public const int Resolution = 5;
        public const int Pivot = PastKeys * Resolution;
        public const int SampleCount = (PastKeys + FutureKeys) * Resolution + 1;
        public const int KeyCount = PastKeys + FutureKeys + 1;
        public const int StyleCount = 5;
        public const int ContactCount = 5;
        public const int PhaseCount = 5;
        public const float Framerate = 30f;
        public const float ControlRadius = 1.25f;
        public const float InteractionRadius = 5f;

        public readonly Vector3[] RootPositions = new Vector3[SampleCount];
        public readonly Quaternion[] RootRotations = new Quaternion[SampleCount];
        public readonly Vector3[] RootVelocities = new Vector3[SampleCount];
        // The legacy controller intentionally keeps the rendered Actor root
        // separate from the shifted RootSeries pivot during Feed/Control.
        public Vector3 ActorRootPosition;
        public Quaternion ActorRootRotation;
        public readonly Vector3[] Pivots = new Vector3[SampleCount];
        public readonly Vector3[] Momentums = new Vector3[SampleCount];
        public readonly Vector3[] BallPositions = new Vector3[SampleCount];
        public readonly Quaternion[] BallRotations = new Quaternion[SampleCount];
        public readonly Vector3[] BallVelocities = new Vector3[SampleCount];
        public readonly float[] Styles = new float[SampleCount * StyleCount];
        public readonly float[] Contacts = new float[SampleCount * ContactCount];
        public readonly float[] Phases = new float[SampleCount * PhaseCount];
        public readonly float[] Amplitudes = new float[SampleCount * PhaseCount];

        public readonly Vector3[] BonePositions = new Vector3[BasketballSkeleton.BoneCount];
        public readonly Quaternion[] BoneRotations = new Quaternion[BasketballSkeleton.BoneCount];
        public readonly Vector3[] BoneVelocities = new Vector3[BasketballSkeleton.BoneCount];

        public bool Carrier = true;
        public bool HoldIntent;
        public bool ShootIntent;
        public bool BallHorizontalControl;
        public bool BallHeightControl;
        public bool BallSpeedControl;
        public bool MoveIntent;
        public int TickCount;

        public static int KeyIndex(int key) => key * Resolution;
        public static int StyleIndex(int sample, int style) => sample * StyleCount + style;
        public static int ContactIndex(int sample, int contact) => sample * ContactCount + contact;
        public static int PhaseIndex(int sample, int phase) => sample * PhaseCount + phase;

        public void Initialize(Transform root, BasketballSkeleton skeleton, BasketballBallController ball)
        {
            ActorRootPosition = root.position;
            ActorRootRotation = root.rotation;
            for (int sample = 0; sample < SampleCount; sample++)
            {
                RootPositions[sample] = root.position;
                RootRotations[sample] = root.rotation;
                RootVelocities[sample] = Vector3.zero;
                Pivots[sample] = Vector3.forward;
                Momentums[sample] = Vector3.zero;
                BallPositions[sample] = ball.transform.position;
                BallRotations[sample] = ball.transform.rotation;
                BallVelocities[sample] = Vector3.zero;
                Styles[StyleIndex(sample, 0)] = 1f;
                Styles[StyleIndex(sample, 2)] = 1f;
            }

            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                Transform transform = skeleton.GetBone(bone);
                BonePositions[bone] = transform.position;
                BoneRotations[bone] = transform.rotation;
                BoneVelocities[bone] = Vector3.zero;
            }

            Carrier = true;
            TickCount = 0;
        }

        public void ShiftControlSeries()
        {
            for (int sample = 0; sample < SampleCount - 1; sample++)
            {
                RootPositions[sample] = RootPositions[sample + 1];
                RootRotations[sample] = RootRotations[sample + 1];
                RootVelocities[sample] = RootVelocities[sample + 1];
                Pivots[sample] = Pivots[sample + 1];
                Momentums[sample] = Momentums[sample + 1];
                for (int style = 0; style < StyleCount; style++)
                {
                    Styles[StyleIndex(sample, style)] = Styles[StyleIndex(sample + 1, style)];
                }
            }
        }

        public void ShiftPastOutputSeries()
        {
            for (int sample = 0; sample < Pivot; sample++)
            {
                BallPositions[sample] = BallPositions[sample + 1];
                BallRotations[sample] = BallRotations[sample + 1];
                BallVelocities[sample] = BallVelocities[sample + 1];
                for (int channel = 0; channel < ContactCount; channel++)
                {
                    Contacts[ContactIndex(sample, channel)] = Contacts[ContactIndex(sample + 1, channel)];
                }
                for (int channel = 0; channel < PhaseCount; channel++)
                {
                    Phases[PhaseIndex(sample, channel)] = Phases[PhaseIndex(sample + 1, channel)];
                    Amplitudes[PhaseIndex(sample, channel)] = Amplitudes[PhaseIndex(sample + 1, channel)];
                }
            }
        }
    }
}
