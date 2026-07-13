using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public sealed class BasketballPoseApplicator
    {
        private static readonly ProfilerMarker InterpolateMarker = new("Basketball.Interpolate");
        private static readonly ProfilerMarker ApplyPoseMarker = new("Basketball.ApplyPose");

        private readonly Transform root;
        private readonly BasketballSkeleton skeleton;
        private readonly BasketballBallController ball;

        public BasketballPoseApplicator(
            Transform root,
            BasketballSkeleton skeleton,
            BasketballBallController ball)
        {
            this.root = root;
            this.skeleton = skeleton;
            this.ball = ball;
        }

        public void Apply(BasketballPoseBuffer previous, BasketballPoseBuffer current, float alpha)
        {
            using (InterpolateMarker.Auto())
            {
                alpha = Mathf.Clamp01(alpha);
                using (ApplyPoseMarker.Auto())
                {
                    root.SetPositionAndRotation(
                        Vector3.Lerp(previous.RootPosition, current.RootPosition, alpha),
                        Quaternion.Slerp(previous.RootRotation, current.RootRotation, alpha));

                    for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                    {
                        skeleton.GetBone(bone).SetPositionAndRotation(
                            Vector3.Lerp(previous.BonePositions[bone], current.BonePositions[bone], alpha),
                            Quaternion.Slerp(previous.BoneRotations[bone], current.BoneRotations[bone], alpha));
                    }

                    ball.SetControlledPose(
                        Vector3.Lerp(previous.BallPosition, current.BallPosition, alpha),
                        Quaternion.Slerp(previous.BallRotation, current.BallRotation, alpha),
                        Vector3.Lerp(previous.BallVelocity, current.BallVelocity, alpha));
                }
            }
        }

        public void ApplySimulationState(BasketballAgentState state)
        {
            root.SetPositionAndRotation(
                state.ActorRootPosition,
                state.ActorRootRotation);
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                skeleton.GetBone(bone).SetPositionAndRotation(
                    state.BonePositions[bone],
                    state.BoneRotations[bone]);
            }
        }
    }
}
