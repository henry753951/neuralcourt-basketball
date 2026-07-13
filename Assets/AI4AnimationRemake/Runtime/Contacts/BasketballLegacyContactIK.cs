using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Unity 6 port of the UltimateIK passes at the end of the SIGGRAPH 2020
    /// BasketballController.Read method. It intentionally keeps the original
    /// chain roots, objective order, activations, iteration counts, and target
    /// persistence.
    /// </summary>
    public sealed class BasketballLegacyContactIK
    {
        private static readonly ProfilerMarker Marker = new("Basketball.IK");

        private readonly BasketballSkeleton skeleton;
        private readonly LegacyModel body;
        private readonly LegacyModel leftFoot;
        private readonly LegacyModel rightFoot;
        private readonly LegacyModel leftHand;
        private readonly LegacyModel rightHand;
        private readonly LegacyModel head;
        private readonly float[] gaussianDistances = new float[BasketballAgentState.KeyCount];
        private readonly float[] gaussianAngles = new float[BasketballAgentState.KeyCount];

        public BasketballLegacyContactIK(Transform actorRoot, BasketballSkeleton skeleton)
        {
            this.skeleton = skeleton;
            Transform[] transforms = actorRoot.GetComponentsInChildren<Transform>(true);
            Transform Find(string name)
            {
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index].name == name)
                    {
                        return transforms[index];
                    }
                }
                throw new InvalidOperationException($"Legacy IK transform '{name}' was not found.");
            }

            Transform leftFootEnd = Find("Player 01:LeftFoot");
            Transform rightFootEnd = Find("Player 01:RightFoot");
            Transform leftBallAux = Find("Player 01:LeftBallAux");
            Transform rightBallAux = Find("Player 01:RightBallAux");

            body = LegacyModel.Build(
                Find("Player 01:Hips"),
                leftFootEnd,
                rightFootEnd,
                leftBallAux,
                rightBallAux);
            leftFoot = LegacyModel.Build(Find("Player 01:LeftUpLeg"), leftFootEnd);
            rightFoot = LegacyModel.Build(Find("Player 01:RightUpLeg"), rightFootEnd);
            leftHand = LegacyModel.Build(Find("Player 01:LeftArm"), leftBallAux);
            rightHand = LegacyModel.Build(Find("Player 01:RightArm"), rightBallAux);
            head = LegacyModel.Build(Find("Player 01:Neck"), Find("Player 01:Head"));
        }

        public void Apply(BasketballAgentState state)
        {
            using (Marker.Auto())
            {
                Vector3 ballPosition = state.BallPositions[BasketballAgentState.Pivot];
                ProcessBody(state, ballPosition);
                ProcessFoot(leftFoot, state, 0);
                ProcessFoot(rightFoot, state, 1);
                ProcessHand(leftHand, state, ballPosition, 2);
                ProcessHand(rightHand, state, ballPosition, 3);
                ProcessHead(state, ballPosition);

                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    Transform transform = skeleton.GetBone(bone);
                    state.BonePositions[bone] = transform.position;
                    state.BoneRotations[bone] = transform.rotation;
                }
            }
        }

        private void ProcessBody(BasketballAgentState state, Vector3 ballPosition)
        {
            bool active =
                !state.Carrier && state.HoldIntent ||
                state.Carrier && state.HoldIntent && state.BallHorizontalControl && !state.MoveIntent;
            if (!active)
            {
                return;
            }

            body.Activation = Activation.Square;
            body.Objectives[0].SetTarget(leftFoot.End);
            body.Objectives[1].SetTarget(rightFoot.End);
            body.Objectives[2].TargetPosition = Vector3.Lerp(
                leftHand.End.position,
                ballPosition,
                BallControlWeight(leftHand.End.position, ballPosition));
            body.Objectives[2].TargetRotation = leftHand.End.rotation;
            body.Objectives[3].TargetPosition = Vector3.Lerp(
                rightHand.End.position,
                ballPosition,
                BallControlWeight(rightHand.End.position, ballPosition));
            body.Objectives[3].TargetRotation = rightHand.End.rotation;
            body.AllowRootUpdateY = true;
            body.Iterations = 25;
            body.Solve();
        }

        private static void ProcessFoot(LegacyModel ik, BasketballAgentState state, int contactChannel)
        {
            float contact = state.Contacts[BasketballAgentState.ContactIndex(
                BasketballAgentState.Pivot,
                contactChannel)];
            LegacyObjective objective = ik.Objectives[0];
            ik.Activation = Activation.Constant;
            objective.TargetPosition = Vector3.Lerp(
                objective.TargetPosition,
                ik.End.position,
                1f - contact);
            objective.TargetRotation = state.Carrier && state.HoldIntent
                ? Quaternion.Slerp(objective.TargetRotation, ik.End.rotation, 1f - contact)
                : ik.End.rotation;
            ik.Iterations = 50;
            ik.Solve();
        }

        private static void ProcessHand(
            LegacyModel ik,
            BasketballAgentState state,
            Vector3 ballPosition,
            int contactChannel)
        {
            if (!state.Carrier)
            {
                return;
            }
            float contact = state.Contacts[BasketballAgentState.ContactIndex(
                BasketballAgentState.Pivot,
                contactChannel)];
            ik.Activation = Activation.Linear;
            ik.Objectives[0].TargetPosition = Vector3.Lerp(ik.End.position, ballPosition, contact);
            ik.Objectives[0].TargetRotation = ik.End.rotation;
            ik.Iterations = 50;
            ik.Solve();
        }

        private void ProcessHead(BasketballAgentState state, Vector3 ballPosition)
        {
            if (state.Carrier || !state.HoldIntent)
            {
                return;
            }

            for (int key = 0; key < BasketballAgentState.KeyCount; key++)
            {
                int sample = BasketballAgentState.KeyIndex(key);
                Vector3 rootPosition = state.RootPositions[sample];
                Vector3 direction = state.RootRotations[sample] * Vector3.forward;
                gaussianDistances[key] = 1f - Mathf.Clamp01(
                    Vector3.Distance(rootPosition, ballPosition) /
                    BasketballAgentState.InteractionRadius);
                gaussianAngles[key] = 1f - Vector3.Angle(
                    direction,
                    ballPosition - rootPosition) / 180f;
            }

            float distance = Gaussian(gaussianDistances);
            float angle = Gaussian(gaussianAngles);
            float weight = Mathf.Min(distance * distance, angle * angle);
            Quaternion rotation = Quaternion.LookRotation(head.End.position - ballPosition) *
                Quaternion.Euler(0f, 90f, -90f);
            head.Activation = Activation.Square;
            head.Objectives[0].TargetPosition = head.End.position;
            head.Objectives[0].TargetRotation = Quaternion.Slerp(
                head.End.rotation,
                rotation,
                weight);
            head.Iterations = 50;
            head.Solve();
        }

        private static float BallControlWeight(Vector3 pivot, Vector3 ballPosition)
        {
            float weight = 1f - Mathf.Clamp01(
                Vector3.Distance(pivot, ballPosition) / BasketballAgentState.ControlRadius);
            return BasketballMath.ActivateCurve(
                weight,
                Mathf.Lerp(1f / 3f, 2f / 3f, weight),
                0f,
                weight);
        }

        private static float Gaussian(float[] values)
        {
            float padding = (values.Length - 1f) * 0.5f;
            float weighted = 0f;
            float sum = 0f;
            for (int index = 0; index < values.Length; index++)
            {
                float weight = Mathf.Exp(
                    -Mathf.Pow(index - padding, 2f) /
                    Mathf.Pow(0.5f * padding, 2f));
                weighted += weight * values[index];
                sum += weight;
            }
            return sum > 0f ? weighted / sum : 0f;
        }

        private enum Activation
        {
            Constant,
            Linear,
            Root,
            Square
        }

        private sealed class LegacyModel
        {
            public readonly List<LegacyBone> Bones = new();
            public readonly List<LegacyObjective> Objectives = new();
            public int Iterations = 25;
            public float Threshold = 0.001f;
            public Activation Activation = Activation.Linear;
            public bool AllowRootUpdateY;
            public Transform End => Bones[Objectives[0].Bone].Transform;

            public static LegacyModel Build(Transform root, params Transform[] endpoints)
            {
                var model = new LegacyModel();
                for (int objectiveIndex = 0; objectiveIndex < endpoints.Length; objectiveIndex++)
                {
                    List<Transform> chain = BuildChain(root, endpoints[objectiveIndex]);
                    for (int chainIndex = 0; chainIndex < chain.Count; chainIndex++)
                    {
                        Transform transform = chain[chainIndex];
                        int boneIndex = model.FindBone(transform);
                        if (boneIndex < 0)
                        {
                            boneIndex = model.Bones.Count;
                            model.Bones.Add(new LegacyBone(transform));
                            int parent = model.FindBone(transform.parent);
                            if (parent >= 0)
                            {
                                model.Bones[parent].Children.Add(boneIndex);
                            }
                        }
                        model.Bones[boneIndex].Objectives.Add(objectiveIndex);
                    }

                    int endBone = model.FindBone(endpoints[objectiveIndex]);
                    model.Objectives.Add(new LegacyObjective(endBone, endpoints[objectiveIndex]));
                }
                return model;
            }

            public void Solve()
            {
                if (Bones.Count == 0)
                {
                    return;
                }
                SetLevels(0, 1);
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    if (IsConverged())
                    {
                        break;
                    }
                    if (AllowRootUpdateY)
                    {
                        float delta = 0f;
                        int count = 0;
                        for (int index = 0; index < Objectives.Count; index++)
                        {
                            LegacyObjective objective = Objectives[index];
                            if (!objective.Active)
                            {
                                continue;
                            }
                            delta += GetWeight(Bones[objective.Bone], objective) *
                                (objective.TargetPosition.y - Bones[objective.Bone].Transform.position.y);
                            count++;
                        }
                        if (count > 0)
                        {
                            Transform root = Bones[0].Transform;
                            root.position += Vector3.up * (delta / count);
                        }
                    }
                    Optimise(0);
                }
            }

            private void Optimise(int boneIndex)
            {
                LegacyBone bone = Bones[boneIndex];
                if (bone.Active)
                {
                    Vector3 position = bone.Transform.position;
                    Quaternion rotation = bone.Transform.rotation;
                    Vector3 forward = Vector3.zero;
                    Vector3 up = Vector3.zero;
                    int count = 0;

                    for (int index = 0; index < bone.Objectives.Count; index++)
                    {
                        LegacyObjective objective = Objectives[bone.Objectives[index]];
                        if (objective.Active && objective.SolveRotation)
                        {
                            Quaternion candidate = Quaternion.Slerp(
                                rotation,
                                objective.TargetRotation *
                                Quaternion.Inverse(Bones[objective.Bone].Transform.rotation) * rotation,
                                GetWeight(bone, objective));
                            forward += candidate * Vector3.forward;
                            up += candidate * Vector3.up;
                            count++;
                        }
                    }
                    for (int index = 0; index < bone.Objectives.Count; index++)
                    {
                        LegacyObjective objective = Objectives[bone.Objectives[index]];
                        if (objective.Active && objective.SolvePosition)
                        {
                            Quaternion candidate = Quaternion.Slerp(
                                rotation,
                                Quaternion.FromToRotation(
                                    Bones[objective.Bone].Transform.position - position,
                                    objective.TargetPosition - position) * rotation,
                                GetWeight(bone, objective));
                            forward += candidate * Vector3.forward;
                            up += candidate * Vector3.up;
                            count++;
                        }
                    }
                    if (count > 0 && forward.sqrMagnitude > 1e-10f && up.sqrMagnitude > 1e-10f)
                    {
                        bone.Transform.rotation = Quaternion.LookRotation(
                            (forward / count).normalized,
                            (up / count).normalized);
                    }
                }

                for (int index = 0; index < bone.Children.Count; index++)
                {
                    Optimise(bone.Children[index]);
                }
            }

            private bool IsConverged()
            {
                for (int index = 0; index < Objectives.Count; index++)
                {
                    if (Objectives[index].Error(this) > Threshold)
                    {
                        return false;
                    }
                }
                return true;
            }

            private float GetWeight(LegacyBone bone, LegacyObjective objective)
            {
                int endLevel = Bones[objective.Bone].Level;
                float ratio = endLevel > 0 ? (float)bone.Level / endLevel : 1f;
                return Activation switch
                {
                    Activation.Constant => 1f,
                    Activation.Linear => ratio,
                    Activation.Root => Mathf.Sqrt(ratio),
                    Activation.Square => ratio * ratio,
                    _ => 1f
                };
            }

            private void SetLevels(int boneIndex, int level)
            {
                LegacyBone bone = Bones[boneIndex];
                bone.Level = level;
                for (int index = 0; index < bone.Children.Count; index++)
                {
                    SetLevels(bone.Children[index], bone.Active ? level + 1 : level);
                }
            }

            private int FindBone(Transform transform)
            {
                for (int index = 0; index < Bones.Count; index++)
                {
                    if (Bones[index].Transform == transform)
                    {
                        return index;
                    }
                }
                return -1;
            }

            private static List<Transform> BuildChain(Transform root, Transform end)
            {
                var chain = new List<Transform>();
                Transform current = end;
                while (current != null)
                {
                    chain.Add(current);
                    if (current == root)
                    {
                        chain.Reverse();
                        return chain;
                    }
                    current = current.parent;
                }
                throw new InvalidOperationException(
                    $"IK endpoint '{end.name}' is not below root '{root.name}'.");
            }
        }

        private sealed class LegacyBone
        {
            public readonly Transform Transform;
            public readonly List<int> Children = new();
            public readonly List<int> Objectives = new();
            public bool Active = true;
            public int Level;

            public LegacyBone(Transform transform)
            {
                Transform = transform;
            }
        }

        private sealed class LegacyObjective
        {
            public readonly int Bone;
            public bool Active = true;
            public bool SolvePosition = true;
            public bool SolveRotation = true;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;

            public LegacyObjective(int bone, Transform target)
            {
                Bone = bone;
                SetTarget(target);
            }

            public void SetTarget(Transform target)
            {
                TargetPosition = target.position;
                TargetRotation = target.rotation;
            }

            public float Error(LegacyModel model)
            {
                float error = SolvePosition
                    ? Vector3.Distance(model.Bones[Bone].Transform.position, TargetPosition)
                    : 0f;
                if (SolveRotation)
                {
                    error += Mathf.Deg2Rad * Quaternion.Angle(
                        model.Bones[Bone].Transform.rotation,
                        TargetRotation);
                }
                return error;
            }
        }
    }
}
