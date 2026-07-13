using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Matches BasketballController.Read's legacy Correct Twist pass. Primitive
    /// collider geometry is authored along each bone's right axis, so this pass
    /// is required to keep rendered limb segments aimed at their child joints.
    /// </summary>
    public sealed class BasketballTwistCorrector
    {
        private static readonly ProfilerMarker Marker = new("Basketball.IK");
        private readonly int[] singleChildren = new int[BasketballSkeleton.BoneCount];

        public BasketballTwistCorrector()
        {
            for (int bone = 0; bone < singleChildren.Length; bone++)
            {
                singleChildren[bone] = -1;
            }

            for (int child = 1; child < BasketballSkeleton.BoneCount; child++)
            {
                int parent = BasketballSkeleton.CanonicalParents[child];
                singleChildren[parent] = singleChildren[parent] == -1 ? child : -2;
            }
        }

        public void Correct(BasketballAgentState state)
        {
            using (Marker.Auto())
            {
                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    int child = singleChildren[bone];
                    if (child < 0)
                    {
                        continue;
                    }

                    Vector3 aligned = state.BonePositions[bone] - state.BonePositions[child];
                    if (aligned.sqrMagnitude < 1e-10f)
                    {
                        continue;
                    }

                    Quaternion rotation = state.BoneRotations[bone];
                    state.BoneRotations[bone] =
                        Quaternion.FromToRotation(rotation * Vector3.right, aligned.normalized) * rotation;
                }
            }
        }
    }
}
