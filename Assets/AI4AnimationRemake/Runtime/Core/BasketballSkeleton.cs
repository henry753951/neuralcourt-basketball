using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballSkeleton : MonoBehaviour
    {
        public const int BoneCount = 26;

        public static readonly string[] CanonicalNames =
        {
            "Player 01:Hips",
            "Player 01:LeftUpLeg",
            "Player 01:LeftLeg",
            "Player 01:LeftFoot",
            "Player 01:LeftToeBase",
            "Player 01:LeftFootEnd",
            "Player 01:RightUpLeg",
            "Player 01:RightLeg",
            "Player 01:RightFoot",
            "Player 01:RightToeBase",
            "Player 01:RightFootEnd",
            "Player 01:Spine",
            "Player 01:Spine1",
            "Player 01:Spine2",
            "Player 01:Spine3",
            "Player 01:LeftShoulder",
            "Player 01:LeftArm",
            "Player 01:LeftForeArm",
            "Player 01:LeftHand",
            "Player 01:Neck",
            "Player 01:Neck1",
            "Player 01:Head",
            "Player 01:RightShoulder",
            "Player 01:RightArm",
            "Player 01:RightForeArm",
            "Player 01:RightHand"
        };

        public static readonly int[] CanonicalParents =
        {
            -1,
            0, 1, 2, 3, 4,
            0, 6, 7, 8, 9,
            0, 11, 12, 13,
            14, 15, 16, 17,
            14, 19, 20,
            14, 22, 23, 24
        };

        [SerializeField]
        private Transform[] bones = new Transform[BoneCount];

        public int Count => bones == null ? 0 : bones.Length;

        public Transform GetBone(int index)
        {
            return bones[index];
        }

        public bool Validate(out string reason)
        {
            if (bones == null || bones.Length != BoneCount)
            {
                int count = bones == null ? 0 : bones.Length;
                reason = $"Expected {BoneCount} bones but found {count}.";
                return false;
            }

            for (int i = 0; i < BoneCount; i++)
            {
                if (bones[i] == null)
                {
                    reason = $"Bone {i} ({CanonicalNames[i]}) is not assigned.";
                    return false;
                }

                if (bones[i].name != CanonicalNames[i])
                {
                    reason = $"Bone {i} expected '{CanonicalNames[i]}' but found '{bones[i].name}'.";
                    return false;
                }

                int parent = CanonicalParents[i];
                if (parent >= 0 && !bones[i].IsChildOf(bones[parent]))
                {
                    reason = $"Bone {i} is not under canonical parent {parent}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public void Configure(Transform[] value)
        {
            bones = value;
        }
#endif
    }
}
