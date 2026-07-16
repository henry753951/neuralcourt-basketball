using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballHoop : MonoBehaviour
    {
        [SerializeField, Min(0)] private int attackedByTeamId;
        [SerializeField] private Transform rimCenter;
        [SerializeField] private Transform backboard;
        [SerializeField, Min(0.05f)] private float rimRadius = 0.225f;
        [SerializeField] private Vector3 aimOffset = new(0f, 0.025f, 0f);

        public int AttackedByTeamId => attackedByTeamId;
        public Transform RimCenterTransform => rimCenter;
        public Transform BackboardTransform => backboard;
        public float RimRadius => rimRadius;
        public Vector3 Center => rimCenter != null ? rimCenter.position : transform.position;
        public Vector3 AimPoint => Center + transform.TransformVector(aimOffset);

        public Vector3 CourtFacing
        {
            get
            {
                Vector3 towardCenter = Vector3.ProjectOnPlane(
                    -transform.position,
                    Vector3.up);
                return towardCenter.sqrMagnitude > 1e-8f
                    ? towardCenter.normalized
                    : transform.forward;
            }
        }

#if UNITY_EDITOR
        public void Configure(
            int teamId,
            Transform rim,
            Transform board,
            float radius)
        {
            attackedByTeamId = Mathf.Max(0, teamId);
            rimCenter = rim;
            backboard = board;
            rimRadius = Mathf.Max(0.05f, radius);
            aimOffset = new Vector3(0f, 0.025f, 0f);
        }
#endif
    }
}
