using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public static class BasketballBallExposureEvaluator
    {
        public static float Evaluate(
            BasketballAgentState owner,
            in BasketballBallObservation ownerObservation,
            in BasketballBallObservation defenderObservation)
        {
            Vector3 ownerChest = owner.BonePositions[14];
            float bodySeparation = Vector3.Distance(
                ownerObservation.Position,
                ownerChest);
            float outsideBody = Mathf.InverseLerp(0.25f, 0.9f, bodySeparation);

            Vector3 ownerToDefender = defenderObservation.RootPosition -
                                      ownerObservation.RootPosition;
            Vector3 ownerToBall = ownerObservation.Position -
                                  ownerObservation.RootPosition;
            float betweenPlayers = 0f;
            if (ownerToDefender.sqrMagnitude > 1e-8f && ownerToBall.sqrMagnitude > 1e-8f)
            {
                betweenPlayers = Mathf.InverseLerp(
                    -0.25f,
                    0.8f,
                    Vector3.Dot(ownerToDefender.normalized, ownerToBall.normalized));
            }

            float protectedByHands = Mathf.Clamp01(ownerObservation.HandContact);
            float held = owner.Styles[BasketballAgentState.StyleIndex(
                BasketballAgentState.Pivot,
                3)];
            float protection = Mathf.Clamp01(0.55f * protectedByHands + 0.45f * held);
            return Mathf.Clamp01(
                0.5f * outsideBody + 0.35f * betweenPlayers + 0.15f * (1f - protection));
        }
    }
}
