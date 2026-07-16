using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Keeps the canonical 26-bone neural rig as simulation authority and drives
    /// a Humanoid presentation rig. Limb swing follows canonical joint positions;
    /// arm roll follows the canonical shoulder/elbow/wrist plane and hand rotation
    /// remains authored by the neural model.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class BasketballHumanoidVisualRetargeter : MonoBehaviour
    {
        private static readonly BoneBinding[] Bindings =
        {
            new(0, HumanBodyBones.Hips),
            new(1, HumanBodyBones.LeftUpperLeg, 2, HumanBodyBones.LeftLowerLeg),
            new(2, HumanBodyBones.LeftLowerLeg, 3, HumanBodyBones.LeftFoot),
            new(3, HumanBodyBones.LeftFoot, 4, HumanBodyBones.LeftToes),
            new(4, HumanBodyBones.LeftToes),
            new(6, HumanBodyBones.RightUpperLeg, 7, HumanBodyBones.RightLowerLeg),
            new(7, HumanBodyBones.RightLowerLeg, 8, HumanBodyBones.RightFoot),
            new(8, HumanBodyBones.RightFoot, 9, HumanBodyBones.RightToes),
            new(9, HumanBodyBones.RightToes),
            new(11, HumanBodyBones.Spine),
            new(13, HumanBodyBones.Chest),
            new(14, HumanBodyBones.UpperChest),
            new(15, HumanBodyBones.LeftShoulder, 16, HumanBodyBones.LeftUpperArm),
            new(16, HumanBodyBones.LeftUpperArm, 17, HumanBodyBones.LeftLowerArm),
            new(17, HumanBodyBones.LeftLowerArm, 18, HumanBodyBones.LeftHand),
            new(18, HumanBodyBones.LeftHand, wrist: true),
            new(19, HumanBodyBones.Neck),
            new(21, HumanBodyBones.Head),
            new(22, HumanBodyBones.RightShoulder, 23, HumanBodyBones.RightUpperArm),
            new(23, HumanBodyBones.RightUpperArm, 24, HumanBodyBones.RightLowerArm),
            new(24, HumanBodyBones.RightLowerArm, 25, HumanBodyBones.RightHand),
            new(25, HumanBodyBones.RightHand, wrist: true)
        };

        [Header("Humanoid Visual")]
        [SerializeField] private Animator humanoidAnimator;
        [SerializeField, Min(0.1f)] private float scaleMultiplier = 1f;
        [SerializeField] private Vector3 hipsWorldOffset;

        [Header("Retarget Quality")]
        [SerializeField] private bool alignLimbsFromJointPositions = true;
        [SerializeField] private bool showCanonicalRigWhenVisualUnavailable = true;

        private readonly Transform[] sourceBones = new Transform[Bindings.Length];
        private readonly Transform[] sourceChildBones = new Transform[Bindings.Length];
        private readonly Transform[] targetBones = new Transform[Bindings.Length];
        private readonly Quaternion[] worldRotationOffsets = new Quaternion[Bindings.Length];
        private readonly Vector3[] targetSegmentAxes = new Vector3[Bindings.Length];
        private readonly Vector3[] targetPlaneAxes = new Vector3[Bindings.Length];
        private readonly Vector3[] previousSourcePlaneNormals = new Vector3[Bindings.Length];

        private BasketballReferenceRig referenceRig;
        private BasketballTeamMember teamMember;
        private Transform targetHips;
        private Renderer[] canonicalRenderers = Array.Empty<Renderer>();
        private bool[] canonicalRendererStates = Array.Empty<bool>();
        private Renderer[] visualRenderers = Array.Empty<Renderer>();
        private bool[] visualRendererStates = Array.Empty<bool>();
        private bool initialized;
        private bool visibilityCached;

        public bool IsReady => initialized;
        public Animator HumanoidAnimator => humanoidAnimator;

        private readonly struct BoneBinding
        {
            public BoneBinding(
                int sourceIndex,
                HumanBodyBones targetBone,
                int sourceChildIndex = -1,
                HumanBodyBones targetChildBone = HumanBodyBones.LastBone,
                bool wrist = false)
            {
                SourceIndex = sourceIndex;
                TargetBone = targetBone;
                SourceChildIndex = sourceChildIndex;
                TargetChildBone = targetChildBone;
                IsWrist = wrist;
            }

            public int SourceIndex { get; }
            public HumanBodyBones TargetBone { get; }
            public int SourceChildIndex { get; }
            public HumanBodyBones TargetChildBone { get; }
            public bool IsWrist { get; }
            public bool HasSegment => SourceChildIndex >= 0 &&
                                      TargetChildBone != HumanBodyBones.LastBone;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                Initialize();
            }
            else
            {
                SetPresentationVisibility(true);
            }
        }

        private void OnDisable()
        {
            if (showCanonicalRigWhenVisualUnavailable)
            {
                SetPresentationVisibility(false);
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            if (targetHips != null && sourceBones[0] != null)
            {
                targetHips.position = sourceBones[0].position +
                                      referenceRig.transform.TransformVector(hipsWorldOffset);
            }

            for (int index = 0; index < Bindings.Length; index++)
            {
                Transform source = sourceBones[index];
                Transform target = targetBones[index];
                if (source == null || target == null)
                {
                    continue;
                }

                Quaternion rawRotation = source.rotation * worldRotationOffsets[index];
                BoneBinding binding = Bindings[index];
                if (binding.IsWrist)
                {
                    // The Basketball model does output LeftHand/RightHand rotation.
                    // The lower arm may already have been anatomically corrected
                    // away from its raw mapped rotation. Carry that same correction
                    // into the hand so the neural wrist pose remains relative to the
                    // corrected forearm instead of bending in world space.
                    target.rotation = GetForearmCorrectedHandRotation(
                        binding.SourceIndex,
                        rawRotation);
                }
                else if (alignLimbsFromJointPositions && binding.HasSegment)
                {
                    ApplyAnatomicalSegment(index, source, target, rawRotation);
                }
                else
                {
                    target.rotation = rawRotation;
                }
            }
        }

        private Quaternion GetForearmCorrectedHandRotation(
            int handSourceIndex,
            Quaternion rawHandRotation)
        {
            int forearmSourceIndex = handSourceIndex == 18
                ? 17
                : handSourceIndex == 25
                    ? 24
                    : -1;
            if (forearmSourceIndex < 0)
            {
                return rawHandRotation;
            }

            int forearmBindingIndex = FindBindingIndex(forearmSourceIndex);
            if (forearmBindingIndex < 0)
            {
                return rawHandRotation;
            }

            Transform sourceForearm = sourceBones[forearmBindingIndex];
            Transform targetForearm = targetBones[forearmBindingIndex];
            if (sourceForearm == null || targetForearm == null)
            {
                return rawHandRotation;
            }

            Quaternion rawMappedForearm = sourceForearm.rotation *
                                            worldRotationOffsets[forearmBindingIndex];
            Quaternion forearmCorrection = targetForearm.rotation *
                                             Quaternion.Inverse(rawMappedForearm);
            return forearmCorrection * rawHandRotation;
        }

        private static int FindBindingIndex(int sourceIndex)
        {
            for (int index = 0; index < Bindings.Length; index++)
            {
                if (Bindings[index].SourceIndex == sourceIndex)
                {
                    return index;
                }
            }
            return -1;
        }

        private void ApplyAnatomicalSegment(
            int index,
            Transform source,
            Transform target,
            Quaternion rawRotation)
        {
            Transform sourceChild = sourceChildBones[index];
            Vector3 localAxis = targetSegmentAxes[index];
            if (sourceChild == null || localAxis.sqrMagnitude < 1e-8f)
            {
                target.rotation = rawRotation;
                return;
            }

            Vector3 desiredDirection = sourceChild.position - source.position;
            if (desiredDirection.sqrMagnitude < 1e-8f)
            {
                target.rotation = rawRotation;
                return;
            }

            desiredDirection.Normalize();
            Vector3 localPlaneAxis = targetPlaneAxes[index];
            if (localPlaneAxis.sqrMagnitude > 1e-8f &&
                TryGetSourceArmPlaneNormal(Bindings[index].SourceIndex, out Vector3 planeNormal))
            {
                if (previousSourcePlaneNormals[index].sqrMagnitude > 1e-8f &&
                    Vector3.Dot(previousSourcePlaneNormals[index], planeNormal) < 0f)
                {
                    planeNormal = -planeNormal;
                }
                previousSourcePlaneNormals[index] = planeNormal.normalized;
                planeNormal = Vector3.ProjectOnPlane(planeNormal, desiredDirection);
                if (planeNormal.sqrMagnitude > 1e-8f)
                {
                    Quaternion localBasis = Quaternion.LookRotation(
                        localAxis.normalized,
                        localPlaneAxis.normalized);
                    Quaternion worldBasis = Quaternion.LookRotation(
                        desiredDirection,
                        planeNormal.normalized);
                    target.rotation = worldBasis * Quaternion.Inverse(localBasis);
                    return;
                }
            }

            Vector3 rawDirection = rawRotation * localAxis;
            if (rawDirection.sqrMagnitude < 1e-8f)
            {
                target.rotation = rawRotation;
                return;
            }

            target.rotation = Quaternion.FromToRotation(
                rawDirection.normalized,
                desiredDirection) * rawRotation;
        }

        private bool TryGetSourceArmPlaneNormal(
            int sourceIndex,
            out Vector3 normal)
        {
            int first;
            int second;
            int third;
            switch (sourceIndex)
            {
                case 15:
                    first = 15;
                    second = 16;
                    third = 17;
                    break;
                case 16:
                case 17:
                    first = 16;
                    second = 17;
                    third = 18;
                    break;
                case 22:
                    first = 22;
                    second = 23;
                    third = 24;
                    break;
                case 23:
                case 24:
                    first = 23;
                    second = 24;
                    third = 25;
                    break;
                default:
                    normal = Vector3.zero;
                    return false;
            }

            Transform a = referenceRig.Skeleton.GetBone(first);
            Transform b = referenceRig.Skeleton.GetBone(second);
            Transform c = referenceRig.Skeleton.GetBone(third);
            normal = Vector3.Cross(b.position - a.position, c.position - b.position);
            return normal.sqrMagnitude > 1e-8f;
        }

        private void Initialize()
        {
            referenceRig = GetComponentInParent<BasketballReferenceRig>();
            teamMember = GetComponentInParent<BasketballTeamMember>();
            humanoidAnimator = humanoidAnimator != null
                ? humanoidAnimator
                : GetComponentInChildren<Animator>(true);
            CacheRendererVisibility();

            string failure = ValidateSetup();
            if (!string.IsNullOrEmpty(failure))
            {
                initialized = false;
                SetPresentationVisibility(false);
                Debug.LogWarning(
                    $"Humanoid visual on '{name}' is unavailable; using the canonical " +
                    $"debug rig instead. {failure}",
                    this);
                enabled = false;
                return;
            }

            humanoidAnimator.applyRootMotion = false;
            humanoidAnimator.runtimeAnimatorController = null;
            humanoidAnimator.enabled = false;

            ApplyAutomaticScale();
            for (int index = 0; index < Bindings.Length; index++)
            {
                BoneBinding binding = Bindings[index];
                Transform source = referenceRig.Skeleton.GetBone(binding.SourceIndex);
                Transform target = humanoidAnimator.GetBoneTransform(binding.TargetBone);
                Transform sourceChild = binding.HasSegment
                    ? referenceRig.Skeleton.GetBone(binding.SourceChildIndex)
                    : null;
                Transform targetChild = binding.HasSegment
                    ? humanoidAnimator.GetBoneTransform(binding.TargetChildBone)
                    : null;

                sourceBones[index] = source;
                sourceChildBones[index] = sourceChild;
                targetBones[index] = target;
                worldRotationOffsets[index] = Quaternion.Inverse(source.rotation) * target.rotation;
                targetSegmentAxes[index] = targetChild != null
                    ? target.InverseTransformDirection(
                        (targetChild.position - target.position).normalized)
                    : Vector3.zero;
                targetPlaneAxes[index] = TryGetTargetArmPlaneNormal(
                        binding.SourceIndex,
                        out Vector3 targetPlaneNormal)
                    ? target.InverseTransformDirection(targetPlaneNormal.normalized)
                    : Vector3.zero;
            }
            targetHips = humanoidAnimator.GetBoneTransform(HumanBodyBones.Hips);

            initialized = true;
            SetPresentationVisibility(true);
        }

        private string ValidateSetup()
        {
            if (referenceRig == null || referenceRig.Skeleton == null)
            {
                return "A BasketballReferenceRig with a canonical skeleton is required.";
            }
            if (humanoidAnimator == null || humanoidAnimator.avatar == null ||
                !humanoidAnimator.avatar.isHuman)
            {
                return "Assign an Animator with a valid Humanoid Avatar.";
            }

            for (int index = 0; index < Bindings.Length; index++)
            {
                BoneBinding binding = Bindings[index];
                if (referenceRig.Skeleton.GetBone(binding.SourceIndex) == null)
                {
                    return $"Canonical bone {binding.SourceIndex} is missing.";
                }
                if (humanoidAnimator.GetBoneTransform(binding.TargetBone) == null)
                {
                    return $"Humanoid bone {binding.TargetBone} is missing.";
                }
                if (binding.HasSegment &&
                    humanoidAnimator.GetBoneTransform(binding.TargetChildBone) == null)
                {
                    return $"Humanoid child bone {binding.TargetChildBone} is missing.";
                }
            }
            return string.Empty;
        }

        private bool TryGetTargetArmPlaneNormal(
            int sourceIndex,
            out Vector3 normal)
        {
            HumanBodyBones first;
            HumanBodyBones second;
            HumanBodyBones third;
            switch (sourceIndex)
            {
                case 15:
                    first = HumanBodyBones.LeftShoulder;
                    second = HumanBodyBones.LeftUpperArm;
                    third = HumanBodyBones.LeftLowerArm;
                    break;
                case 16:
                case 17:
                    first = HumanBodyBones.LeftUpperArm;
                    second = HumanBodyBones.LeftLowerArm;
                    third = HumanBodyBones.LeftHand;
                    break;
                case 22:
                    first = HumanBodyBones.RightShoulder;
                    second = HumanBodyBones.RightUpperArm;
                    third = HumanBodyBones.RightLowerArm;
                    break;
                case 23:
                case 24:
                    first = HumanBodyBones.RightUpperArm;
                    second = HumanBodyBones.RightLowerArm;
                    third = HumanBodyBones.RightHand;
                    break;
                default:
                    normal = Vector3.zero;
                    return false;
            }

            Transform a = humanoidAnimator.GetBoneTransform(first);
            Transform b = humanoidAnimator.GetBoneTransform(second);
            Transform c = humanoidAnimator.GetBoneTransform(third);
            if (a == null || b == null || c == null)
            {
                normal = Vector3.zero;
                return false;
            }
            normal = Vector3.Cross(b.position - a.position, c.position - b.position);
            return normal.sqrMagnitude > 1e-8f;
        }

        private void ApplyAutomaticScale()
        {
            Transform sourceHips = referenceRig.Skeleton.GetBone(0);
            Transform sourceLeftFoot = referenceRig.Skeleton.GetBone(3);
            Transform targetHipsTransform = humanoidAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetLeftFoot = humanoidAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (sourceHips == null || sourceLeftFoot == null ||
                targetHipsTransform == null || targetLeftFoot == null)
            {
                return;
            }

            float sourceLeg = Vector3.Distance(sourceHips.position, sourceLeftFoot.position);
            float targetLeg = Vector3.Distance(targetHipsTransform.position, targetLeftFoot.position);
            if (sourceLeg > 0.1f && targetLeg > 0.1f)
            {
                float scale = Mathf.Clamp(sourceLeg / targetLeg * scaleMultiplier, 0.5f, 1.5f);
                transform.localScale = Vector3.one * scale;
            }
        }

        private void CacheRendererVisibility()
        {
            if (visibilityCached)
            {
                return;
            }

            visualRenderers = GetComponentsInChildren<Renderer>(true);
            visualRendererStates = CaptureStates(visualRenderers);
            if (teamMember != null)
            {
                Renderer[] allRenderers = teamMember.GetComponentsInChildren<Renderer>(true);
                int count = 0;
                for (int index = 0; index < allRenderers.Length; index++)
                {
                    Renderer renderer = allRenderers[index];
                    if (IsCanonicalRenderer(renderer))
                    {
                        count++;
                    }
                }

                canonicalRenderers = new Renderer[count];
                canonicalRendererStates = new bool[count];
                int destination = 0;
                for (int index = 0; index < allRenderers.Length; index++)
                {
                    Renderer renderer = allRenderers[index];
                    if (!IsCanonicalRenderer(renderer))
                    {
                        continue;
                    }
                    canonicalRenderers[destination] = renderer;
                    canonicalRendererStates[destination] = renderer.enabled;
                    destination++;
                }
            }
            visibilityCached = true;
        }

        private bool IsCanonicalRenderer(Renderer renderer)
        {
            return renderer != null &&
                   renderer is not LineRenderer &&
                   renderer.transform != transform &&
                   !renderer.transform.IsChildOf(transform);
        }

        private static bool[] CaptureStates(Renderer[] renderers)
        {
            bool[] states = new bool[renderers.Length];
            for (int index = 0; index < renderers.Length; index++)
            {
                states[index] = renderers[index] != null && renderers[index].enabled;
            }
            return states;
        }

        private void SetPresentationVisibility(bool visualAvailable)
        {
            if (!visibilityCached)
            {
                return;
            }

            for (int index = 0; index < visualRenderers.Length; index++)
            {
                if (visualRenderers[index] != null)
                {
                    visualRenderers[index].enabled = visualAvailable &&
                                                     visualRendererStates[index];
                }
            }
            for (int index = 0; index < canonicalRenderers.Length; index++)
            {
                if (canonicalRenderers[index] != null)
                {
                    canonicalRenderers[index].enabled = visualAvailable
                        ? false
                        : canonicalRendererStates[index];
                }
            }
        }

#if UNITY_EDITOR
        public void Configure(Animator animator)
        {
            humanoidAnimator = animator;
            scaleMultiplier = 1f;
            hipsWorldOffset = Vector3.zero;
            alignLimbsFromJointPositions = true;
            showCanonicalRigWhenVisualUnavailable = true;
            initialized = false;
            visibilityCached = false;
        }
#endif
    }
}
