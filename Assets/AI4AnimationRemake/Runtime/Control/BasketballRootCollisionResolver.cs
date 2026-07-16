using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Allocation-free equivalent of RootSeries.ResolveCollisions used by the
    /// SIGGRAPH 2020 Basketball demo.
    /// </summary>
    public sealed class BasketballRootCollisionResolver
    {
        private static readonly ProfilerMarker Marker = new("Basketball.Contact");
        private readonly Collider[] overlaps = new Collider[64];
        private readonly float safety;
        private readonly int collisionMask;

        public BasketballRootCollisionResolver(float radius, LayerMask mask)
        {
            safety = Mathf.Max(radius, 0.01f);
            collisionMask = mask.value;
        }

        public void Resolve(BasketballAgentState state)
        {
            using (Marker.Auto())
            {
                for (int sample = BasketballAgentState.Pivot;
                     sample < BasketballAgentState.SampleCount;
                     sample++)
                {
                    Vector3 previous = state.RootPositions[sample - 1];
                    Vector3 current = state.RootPositions[sample];
                    Vector3 delta = current - previous;
                    float distance = delta.magnitude;
                    if (distance > 1e-6f && Physics.Raycast(
                            previous,
                            delta / distance,
                            out _,
                            distance,
                            collisionMask,
                            QueryTriggerInteraction.Ignore))
                    {
                        for (int future = sample;
                             future < BasketballAgentState.SampleCount;
                             future++)
                        {
                            state.RootPositions[future] = state.RootPositions[future - 1];
                        }
                    }

                    current = state.RootPositions[sample];
                    Vector3 corrected = SafetyProjection(current);
                    state.RootPositions[sample] = corrected;
                    state.RootVelocities[sample] +=
                        BasketballAgentState.Framerate * (corrected - current);
                }
            }
        }

        public void ResolveAfterDecode(BasketballAgentState state)
        {
            Vector3 decodedActorPosition = state.ActorRootPosition;
            Resolve(state);

            // Decode writes ActorRootPosition, the trajectory pivot and every
            // bone in world space before collision handling. Resolve() used to
            // correct only RootSeries, allowing the rendered actor to cross an
            // obstacle even though its future trajectory stopped at the wall.
            // Translate the decoded pose by the same pivot correction so the
            // skeleton remains rigid and the recurrent state stays coherent.
            Vector3 correction =
                state.RootPositions[BasketballAgentState.Pivot] -
                decodedActorPosition;
            if (correction.sqrMagnitude <= 1e-12f)
            {
                return;
            }

            state.ActorRootPosition += correction;
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

        private Vector3 SafetyProjection(Vector3 pivot)
        {
            int count = Physics.OverlapSphereNonAlloc(
                pivot,
                safety,
                overlaps,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            if (count == 0)
            {
                return pivot;
            }

            Vector3 closest = pivot;
            float minimum = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider candidateCollider = overlaps[index];
                if (candidateCollider == null || candidateCollider.isTrigger)
                {
                    continue;
                }

                Vector3 candidate = candidateCollider.ClosestPoint(pivot);
                Vector3 delta = candidate - pivot;
                float legacyDistance =
                    delta.x * delta.x * delta.x * delta.x +
                    delta.y * delta.y * delta.y * delta.y +
                    delta.z * delta.z * delta.z * delta.z;
                if (legacyDistance < minimum)
                {
                    minimum = legacyDistance;
                    closest = candidate;
                }
            }

            Vector3 direction = pivot - closest;
            return direction.sqrMagnitude > 1e-10f
                ? closest + safety * direction.normalized
                : pivot;
        }
    }
}
