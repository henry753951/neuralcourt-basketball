using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballPoseBuffer
    {
        public Vector3 RootPosition;
        public Quaternion RootRotation;
        public Vector3 BallPosition;
        public Quaternion BallRotation;
        public Vector3 BallVelocity;
        public readonly Vector3[] BonePositions = new Vector3[BasketballSkeleton.BoneCount];
        public readonly Quaternion[] BoneRotations = new Quaternion[BasketballSkeleton.BoneCount];

        public void Capture(BasketballAgentState state)
        {
            RootPosition = state.ActorRootPosition;
            RootRotation = state.ActorRootRotation;
            CaptureBall(state);
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                BonePositions[bone] = state.BonePositions[bone];
                BoneRotations[bone] = state.BoneRotations[bone];
            }
        }

        public void CaptureBall(BasketballAgentState state)
        {
            BallPosition = state.BallPositions[BasketballAgentState.Pivot];
            BallRotation = state.BallRotations[BasketballAgentState.Pivot];
            BallVelocity = state.BallVelocities[BasketballAgentState.Pivot];
        }
    }
}
