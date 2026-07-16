using System.Collections.Generic;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Presentation-only hand-to-ball correction for optional Humanoid skins.
    /// The neural model and canonical contact IK remain authoritative; this solver
    /// restores the BallAux palm-offset semantics that a plain wrist mapping loses.
    /// </summary>
    [DefaultExecutionOrder(550)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BasketballHumanoidVisualRetargeter))]
    public sealed class BasketballHumanoidHandContactSolver : MonoBehaviour
    {
        [SerializeField] private BasketballHumanoidVisualRetargeter retargeter;
        [SerializeField] private bool enableHandBallContact = true;
        [SerializeField, Range(0f, 1f)] private float contactStart = 0.12f;
        [SerializeField, Range(0f, 1f)] private float contactFull = 0.72f;
        [SerializeField, Min(0.01f)] private float maximumBallDistance = 0.52f;
        [SerializeField, Min(0f)] private float palmClearance = 0.008f;
        [SerializeField, Range(0.2f, 0.8f)] private float palmCenterAlongMiddleFinger = 0.48f;
        [SerializeField, Min(0.01f)] private float maximumWristCorrection = 0.24f;
        [SerializeField, Range(0f, 90f)] private float maximumPalmRotationCorrection = 28f;
        [SerializeField, Min(0.1f)] private float contactBlendSpeed = 9f;
        [SerializeField] private bool applyRelaxedFingerPose = true;
        [SerializeField, Range(0f, 35f)] private float relaxedFingerCurl = 6f;
        [SerializeField, Range(0f, 65f)] private float contactFingerCurl = 12f;

        private BasketballReferenceRig referenceRig;
        private BasketballNeuralController controller;
        private SphereCollider ballCollider;
        private HandRig leftHand;
        private HandRig rightHand;
        private float leftWeight;
        private float rightWeight;
        private bool initialized;

        public bool IsReady => initialized;
        public float LeftWeight => leftWeight;
        public float RightWeight => rightWeight;

        private sealed class HandRig
        {
            public Transform UpperArm;
            public Transform LowerArm;
            public Transform Hand;
            public Vector3 PalmCenterLocal;
            public Vector3 PalmNormalLocal;
            public Vector3 FingerDirectionLocal;
            public FingerJoint[] Fingers;
        }

        private sealed class FingerJoint
        {
            public Transform Transform;
            public Quaternion BindLocalRotation;
            public Vector3 BendAxisLocal;
            public float CurlMultiplier;
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
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }
            if (!enableHandBallContact || controller == null || controller.State == null ||
                referenceRig == null || referenceRig.Ball == null)
            {
                FadeOutWeights();
                ApplyFingerPose(leftHand, leftWeight);
                ApplyFingerPose(rightHand, rightWeight);
                return;
            }

            BasketballAgentState state = controller.State;
            int pivot = BasketballAgentState.Pivot;
            Vector3 ballPosition = referenceRig.Ball.transform.position;
            float radius = GetWorldBallRadius(referenceRig.Ball);
            float leftTarget = CalculateContactWeight(
                leftHand,
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 2)],
                ballPosition);
            float rightTarget = CalculateContactWeight(
                rightHand,
                state.Contacts[BasketballAgentState.ContactIndex(pivot, 3)],
                ballPosition);
            float step = contactBlendSpeed * Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            leftWeight = Mathf.MoveTowards(leftWeight, leftTarget, step);
            rightWeight = Mathf.MoveTowards(rightWeight, rightTarget, step);

            SolveHand(leftHand, ballPosition, radius, leftWeight);
            SolveHand(rightHand, ballPosition, radius, rightWeight);
            ApplyFingerPose(leftHand, leftWeight);
            ApplyFingerPose(rightHand, rightWeight);
        }

        private void Initialize()
        {
            retargeter = retargeter != null
                ? retargeter
                : GetComponent<BasketballHumanoidVisualRetargeter>();
            referenceRig = GetComponentInParent<BasketballReferenceRig>();
            controller = GetComponentInParent<BasketballNeuralController>();
            Animator animator = retargeter != null
                ? retargeter.HumanoidAnimator
                : GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman ||
                referenceRig == null || controller == null)
            {
                initialized = false;
                enabled = false;
                return;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            leftHand = BuildHandRig(animator, hips, true);
            rightHand = BuildHandRig(animator, hips, false);
            initialized = leftHand != null && rightHand != null;
            if (!initialized)
            {
                Debug.LogWarning(
                    $"Humanoid hand contact correction is disabled on '{name}' because " +
                    "the Humanoid arm or proximal finger mapping is incomplete.",
                    this);
                enabled = false;
            }
        }

        private HandRig BuildHandRig(Animator animator, Transform hips, bool left)
        {
            Transform upper = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftUpperArm
                : HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftLowerArm
                : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand);
            Transform index = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftIndexProximal
                : HumanBodyBones.RightIndexProximal);
            Transform middle = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftMiddleProximal
                : HumanBodyBones.RightMiddleProximal);
            Transform little = animator.GetBoneTransform(left
                ? HumanBodyBones.LeftLittleProximal
                : HumanBodyBones.RightLittleProximal);
            if (upper == null || lower == null || hand == null || index == null ||
                middle == null || little == null)
            {
                return null;
            }

            Vector3 fingerWorld = (middle.position - hand.position).normalized;
            Vector3 acrossWorld = (little.position - index.position).normalized;
            Vector3 normalWorld = Vector3.Cross(fingerWorld, acrossWorld).normalized;
            if (hips != null && Vector3.Dot(normalWorld, hips.position - hand.position) < 0f)
            {
                normalWorld = -normalWorld;
            }

            HandRig rig = new HandRig
            {
                UpperArm = upper,
                LowerArm = lower,
                Hand = hand,
                PalmCenterLocal = hand.InverseTransformPoint(Vector3.Lerp(
                    hand.position,
                    middle.position,
                    palmCenterAlongMiddleFinger)),
                PalmNormalLocal = hand.InverseTransformDirection(normalWorld).normalized,
                FingerDirectionLocal = hand.InverseTransformDirection(fingerWorld).normalized
            };
            rig.Fingers = BuildFingerJoints(animator, rig, left);
            return rig;
        }

        private static FingerJoint[] BuildFingerJoints(
            Animator animator,
            HandRig rig,
            bool left)
        {
            HumanBodyBones[] bones = left
                ? new[]
                {
                    HumanBodyBones.LeftThumbProximal,
                    HumanBodyBones.LeftThumbIntermediate,
                    HumanBodyBones.LeftThumbDistal,
                    HumanBodyBones.LeftIndexProximal,
                    HumanBodyBones.LeftIndexIntermediate,
                    HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleProximal,
                    HumanBodyBones.LeftMiddleIntermediate,
                    HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingProximal,
                    HumanBodyBones.LeftRingIntermediate,
                    HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleProximal,
                    HumanBodyBones.LeftLittleIntermediate,
                    HumanBodyBones.LeftLittleDistal
                }
                : new[]
                {
                    HumanBodyBones.RightThumbProximal,
                    HumanBodyBones.RightThumbIntermediate,
                    HumanBodyBones.RightThumbDistal,
                    HumanBodyBones.RightIndexProximal,
                    HumanBodyBones.RightIndexIntermediate,
                    HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal,
                    HumanBodyBones.RightMiddleIntermediate,
                    HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal,
                    HumanBodyBones.RightRingIntermediate,
                    HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal,
                    HumanBodyBones.RightLittleIntermediate,
                    HumanBodyBones.RightLittleDistal
                };

            var joints = new List<FingerJoint>(bones.Length);
            Vector3 palmNormalWorld = rig.Hand.TransformDirection(rig.PalmNormalLocal);
            for (int finger = 0; finger < 5; finger++)
            {
                Transform proximal = animator.GetBoneTransform(bones[finger * 3]);
                Transform intermediate = animator.GetBoneTransform(bones[finger * 3 + 1]);
                Transform distal = animator.GetBoneTransform(bones[finger * 3 + 2]);
                if (proximal == null || intermediate == null || distal == null)
                {
                    continue;
                }

                bool thumb = finger == 0;
                Vector3 proximalAxis = CalculateBendAxis(
                    proximal,
                    intermediate,
                    palmNormalWorld);
                Vector3 intermediateAxis = CalculateBendAxis(
                    intermediate,
                    distal,
                    palmNormalWorld);
                joints.Add(CreateFingerJoint(
                    proximal,
                    proximalAxis,
                    thumb ? 0.4f : 0.65f));
                joints.Add(CreateFingerJoint(
                    intermediate,
                    intermediateAxis,
                    thumb ? 0.55f : 0.9f));
                joints.Add(CreateFingerJoint(
                    distal,
                    intermediate.TransformDirection(intermediateAxis),
                    thumb ? 0.35f : 0.55f,
                    true));
            }
            return joints.ToArray();
        }

        private static Vector3 CalculateBendAxis(
            Transform joint,
            Transform child,
            Vector3 palmNormalWorld)
        {
            Vector3 segment = (child.position - joint.position).normalized;
            Vector3 axis = Vector3.Cross(segment, palmNormalWorld);
            if (axis.sqrMagnitude < 1e-8f)
            {
                axis = joint.right;
            }
            return joint.InverseTransformDirection(axis.normalized);
        }

        private static FingerJoint CreateFingerJoint(
            Transform transform,
            Vector3 axis,
            float multiplier,
            bool axisIsWorld = false)
        {
            return new FingerJoint
            {
                Transform = transform,
                BindLocalRotation = transform.localRotation,
                BendAxisLocal = axisIsWorld
                    ? transform.InverseTransformDirection(axis.normalized)
                    : axis.normalized,
                CurlMultiplier = multiplier
            };
        }

        private float CalculateContactWeight(
            HandRig rig,
            float neuralContact,
            Vector3 ballPosition)
        {
            if (rig == null)
            {
                return 0f;
            }

            float contact = Mathf.InverseLerp(
                contactStart,
                Mathf.Max(contactStart + 0.001f, contactFull),
                neuralContact);
            float distance = Vector3.Distance(
                rig.Hand.TransformPoint(rig.PalmCenterLocal),
                ballPosition);
            float distanceWeight = 1f - Mathf.InverseLerp(
                maximumBallDistance * 0.72f,
                maximumBallDistance,
                distance);
            return Mathf.SmoothStep(0f, 1f, contact * distanceWeight);
        }

        private void SolveHand(
            HandRig rig,
            Vector3 ballPosition,
            float ballRadius,
            float weight)
        {
            if (rig == null || weight <= 0.0001f)
            {
                return;
            }

            Vector3 palmPosition = rig.Hand.TransformPoint(rig.PalmCenterLocal);
            Vector3 toBall = ballPosition - palmPosition;
            if (toBall.sqrMagnitude < 1e-8f)
            {
                toBall = ballPosition - rig.Hand.position;
            }
            if (toBall.sqrMagnitude < 1e-8f)
            {
                return;
            }
            toBall.Normalize();

            Quaternion originalHandRotation = rig.Hand.rotation;
            Vector3 originalPalmNormal = originalHandRotation * rig.PalmNormalLocal;
            Quaternion desiredHandRotation = Quaternion.FromToRotation(
                originalPalmNormal,
                toBall) * originalHandRotation;

            Vector3 desiredFinger = Vector3.ProjectOnPlane(
                originalHandRotation * rig.FingerDirectionLocal,
                toBall);
            Vector3 correctedFinger = Vector3.ProjectOnPlane(
                desiredHandRotation * rig.FingerDirectionLocal,
                toBall);
            if (desiredFinger.sqrMagnitude > 1e-8f && correctedFinger.sqrMagnitude > 1e-8f)
            {
                float twist = Vector3.SignedAngle(
                    correctedFinger,
                    desiredFinger,
                    toBall);
                desiredHandRotation = Quaternion.AngleAxis(twist, toBall) *
                                      desiredHandRotation;
            }
            desiredHandRotation = Quaternion.RotateTowards(
                originalHandRotation,
                desiredHandRotation,
                maximumPalmRotationCorrection);

            Vector3 desiredPalmPosition = ballPosition -
                                          toBall * (ballRadius + palmClearance);
            Vector3 desiredWristPosition = desiredPalmPosition -
                                           desiredHandRotation * rig.PalmCenterLocal;
            Vector3 wristCorrection = Vector3.ClampMagnitude(
                desiredWristPosition - rig.Hand.position,
                maximumWristCorrection);
            Vector3 blendedWristPosition = rig.Hand.position + wristCorrection * weight;
            SolveTwoBoneArm(rig, blendedWristPosition);
            rig.Hand.rotation = Quaternion.Slerp(
                rig.Hand.rotation,
                desiredHandRotation,
                weight);
        }

        private static void SolveTwoBoneArm(HandRig rig, Vector3 wristTarget)
        {
            Vector3 shoulder = rig.UpperArm.position;
            Vector3 elbow = rig.LowerArm.position;
            Vector3 wrist = rig.Hand.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, wrist);
            Vector3 shoulderToTarget = wristTarget - shoulder;
            float rawDistance = shoulderToTarget.magnitude;
            if (upperLength < 0.001f || lowerLength < 0.001f || rawDistance < 0.001f)
            {
                return;
            }

            Vector3 direction = shoulderToTarget / rawDistance;
            float distance = Mathf.Clamp(
                rawDistance,
                Mathf.Abs(upperLength - lowerLength) + 0.001f,
                upperLength + lowerLength - 0.001f);
            float along = (upperLength * upperLength - lowerLength * lowerLength +
                           distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(
                0f,
                upperLength * upperLength - along * along));

            Vector3 bendDirection = Vector3.ProjectOnPlane(elbow - shoulder, direction);
            if (bendDirection.sqrMagnitude < 1e-8f)
            {
                bendDirection = Vector3.ProjectOnPlane(Vector3.down, direction);
            }
            if (bendDirection.sqrMagnitude < 1e-8f)
            {
                bendDirection = Vector3.ProjectOnPlane(Vector3.forward, direction);
            }
            bendDirection.Normalize();
            Vector3 desiredElbow = shoulder + direction * along + bendDirection * height;

            Vector3 currentUpperDirection = rig.LowerArm.position - shoulder;
            Vector3 desiredUpperDirection = desiredElbow - shoulder;
            if (currentUpperDirection.sqrMagnitude > 1e-8f &&
                desiredUpperDirection.sqrMagnitude > 1e-8f)
            {
                rig.UpperArm.rotation = Quaternion.FromToRotation(
                    currentUpperDirection,
                    desiredUpperDirection) * rig.UpperArm.rotation;
            }

            Vector3 currentLowerDirection = rig.Hand.position - rig.LowerArm.position;
            Vector3 desiredLowerDirection = wristTarget - rig.LowerArm.position;
            if (currentLowerDirection.sqrMagnitude > 1e-8f &&
                desiredLowerDirection.sqrMagnitude > 1e-8f)
            {
                rig.LowerArm.rotation = Quaternion.FromToRotation(
                    currentLowerDirection,
                    desiredLowerDirection) * rig.LowerArm.rotation;
            }
        }

        private void ApplyFingerPose(HandRig rig, float contactWeight)
        {
            if (!applyRelaxedFingerPose || rig == null || rig.Fingers == null)
            {
                return;
            }
            float curl = Mathf.Lerp(
                relaxedFingerCurl,
                contactFingerCurl,
                Mathf.Clamp01(contactWeight));
            for (int index = 0; index < rig.Fingers.Length; index++)
            {
                FingerJoint joint = rig.Fingers[index];
                if (joint.Transform == null)
                {
                    continue;
                }
                joint.Transform.localRotation = joint.BindLocalRotation;
                Vector3 worldAxis = joint.Transform.TransformDirection(joint.BendAxisLocal);
                joint.Transform.rotation = Quaternion.AngleAxis(
                    curl * joint.CurlMultiplier,
                    worldAxis) * joint.Transform.rotation;
            }
        }

        private float GetWorldBallRadius(BasketballBallController ball)
        {
            ballCollider = ballCollider != null
                ? ballCollider
                : ball.GetComponent<SphereCollider>();
            if (ballCollider != null)
            {
                Vector3 extents = ballCollider.bounds.extents;
                return Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
            }
            Vector3 scale = ball.transform.lossyScale;
            return ball.Radius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        private void FadeOutWeights()
        {
            float step = contactBlendSpeed * Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            leftWeight = Mathf.MoveTowards(leftWeight, 0f, step);
            rightWeight = Mathf.MoveTowards(rightWeight, 0f, step);
        }

#if UNITY_EDITOR
        public void Configure(BasketballHumanoidVisualRetargeter visualRetargeter)
        {
            retargeter = visualRetargeter;
            enableHandBallContact = true;
            contactStart = 0.12f;
            contactFull = 0.72f;
            maximumBallDistance = 0.52f;
            palmClearance = 0.008f;
            palmCenterAlongMiddleFinger = 0.48f;
            maximumWristCorrection = 0.24f;
            maximumPalmRotationCorrection = 28f;
            contactBlendSpeed = 9f;
            applyRelaxedFingerPose = true;
            relaxedFingerCurl = 6f;
            contactFingerCurl = 12f;
            initialized = false;
        }
#endif
    }
}
