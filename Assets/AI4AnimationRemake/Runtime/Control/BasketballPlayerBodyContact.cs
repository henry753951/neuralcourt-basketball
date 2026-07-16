using System.Collections.Generic;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Deterministic player body contact for neural roots. The neural controller
    /// owns transforms, so Rigidbody impulses cannot safely resolve player-player
    /// overlap; this component applies a bounded planar correction to the complete
    /// recurrent pose and also exposes torso occlusion for steal validation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
    public sealed class BasketballPlayerBodyContact : MonoBehaviour
    {
        private static readonly List<BasketballPlayerBodyContact> Active = new(16);

        [SerializeField, Min(0.15f)] private float bodyRadius = 0.34f;
        [SerializeField, Min(0.8f)] private float bodyHeight = 1.72f;
        [SerializeField, Min(0.01f)] private float contactPadding = 0.04f;
        [SerializeField, Min(0.01f)] private float maximumCorrectionPerTick = 0.09f;
        [SerializeField, Min(0.1f)] private float torsoOcclusionRadius = 0.3f;

        private CapsuleCollider capsule;
        private Rigidbody body;
        private BasketballNeuralController controller;
        private BasketballTeamMember teamMember;

        public float BodyRadius => bodyRadius;

        private void Awake()
        {
            CacheAndConfigure();
        }

        private void OnEnable()
        {
            CacheAndConfigure();
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void OnValidate()
        {
            bodyRadius = Mathf.Max(0.15f, bodyRadius);
            bodyHeight = Mathf.Max(2f * bodyRadius, bodyHeight);
            contactPadding = Mathf.Max(0.01f, contactPadding);
            maximumCorrectionPerTick = Mathf.Max(0.01f, maximumCorrectionPerTick);
            torsoOcclusionRadius = Mathf.Max(0.1f, torsoOcclusionRadius);
            CacheAndConfigure();
        }

        internal void ResolveAfterDecode(BasketballAgentState state)
        {
            if (!isActiveAndEnabled || state == null)
            {
                return;
            }

            Vector3 up = Vector3.up;
            Vector3 correction = Vector3.zero;
            Vector3 position = state.ActorRootPosition;
            for (int index = 0; index < Active.Count; index++)
            {
                BasketballPlayerBodyContact other = Active[index];
                if (other == null || other == this || !other.isActiveAndEnabled ||
                    other.controller == null || other.controller.State == null)
                {
                    continue;
                }

                Vector3 away = Vector3.ProjectOnPlane(
                    position - other.controller.State.ActorRootPosition,
                    up);
                float minimumDistance = bodyRadius + other.bodyRadius + contactPadding;
                float distance = away.magnitude;
                if (distance >= minimumDistance)
                {
                    continue;
                }

                Vector3 direction = distance > 1e-4f
                    ? away / distance
                    : StableFallbackDirection(other);
                correction += direction * (minimumDistance - distance);
            }

            correction = Vector3.ClampMagnitude(correction, maximumCorrectionPerTick);
            if (correction.sqrMagnitude <= 1e-10f)
            {
                return;
            }

            TranslateState(state, correction);
        }

        public bool OccludesHandPath(
            BasketballAgentState ownerState,
            Vector3 handPosition,
            Vector3 ballPosition)
        {
            if (ownerState == null)
            {
                return false;
            }

            Vector3 lower = ownerState.ActorRootPosition + Vector3.up * 0.42f;
            Vector3 upper = ownerState.ActorRootPosition + Vector3.up * 1.42f;
            Vector3 handToBall = ballPosition - handPosition;
            // Exclude the final part near the ball so a legitimate touch on a
            // ball held outside the torso is not rejected.
            for (int sample = 1; sample <= 5; sample++)
            {
                float t = sample / 7f;
                Vector3 point = handPosition + handToBall * t;
                if (DistanceToSegment(point, lower, upper) < torsoOcclusionRadius)
                {
                    return true;
                }
            }
            return false;
        }

        private void CacheAndConfigure()
        {
            capsule = capsule != null ? capsule : GetComponent<CapsuleCollider>();
            body = body != null ? body : GetComponent<Rigidbody>();
            controller = controller != null
                ? controller
                : GetComponent<BasketballNeuralController>();
            teamMember = teamMember != null
                ? teamMember
                : GetComponent<BasketballTeamMember>();
            if (capsule != null)
            {
                capsule.direction = 1;
                capsule.center = Vector3.up * (bodyHeight * 0.5f);
                capsule.radius = bodyRadius;
                capsule.height = bodyHeight;
                capsule.isTrigger = false;
            }
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        private Vector3 StableFallbackDirection(BasketballPlayerBodyContact other)
        {
            int selfId = teamMember != null ? teamMember.PlayerIndex : transform.GetSiblingIndex();
            int otherId = other != null && other.teamMember != null
                ? other.teamMember.PlayerIndex
                : other != null ? other.transform.GetSiblingIndex() : 0;
            return selfId < otherId ? Vector3.right : Vector3.left;
        }

        private static void TranslateState(BasketballAgentState state, Vector3 correction)
        {
            state.ActorRootPosition += correction;
            for (int sample = BasketballAgentState.Pivot;
                 sample < BasketballAgentState.SampleCount;
                 sample++)
            {
                state.RootPositions[sample] += correction;
            }
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                state.BonePositions[bone] += correction;
            }
            if (!state.Carrier)
            {
                return;
            }
            for (int sample = BasketballAgentState.Pivot;
                 sample < BasketballAgentState.SampleCount;
                 sample++)
            {
                state.BallPositions[sample] += correction;
            }
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float denominator = segment.sqrMagnitude;
            float t = denominator > 1e-8f
                ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / denominator)
                : 0f;
            return Vector3.Distance(point, start + segment * t);
        }
    }
}
