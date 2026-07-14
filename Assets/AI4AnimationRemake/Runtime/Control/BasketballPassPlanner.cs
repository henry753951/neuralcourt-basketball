using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public static class BasketballPassPlanner
    {
        public static BasketballPassPlan Create(
            BasketballTeamMember passer,
            BasketballTeamMember receiver,
            Vector3 releasePosition,
            int possessionVersion,
            BasketballPassType passType = BasketballPassType.Lead)
        {
            BasketballAgentState receiverState = receiver.Controller.State;
            Vector3 receiverVelocity = receiverState != null
                ? receiverState.RootVelocities[BasketballAgentState.Pivot]
                : Vector3.zero;
            Vector3 catchPoint = receiver.AimPoint;
            float flightTime = 0.5f;

            for (int iteration = 0; iteration < 4; iteration++)
            {
                float leadScale = passType == BasketballPassType.Chest ? 0f : 1f;
                catchPoint = receiver.AimPoint + leadScale * receiverVelocity * flightTime;
                Vector3 horizontal = Vector3.ProjectOnPlane(
                    catchPoint - releasePosition,
                    Vector3.up);
                float nominalSpeed = passType == BasketballPassType.Lob ? 6f : 8f;
                flightTime = Mathf.Clamp(horizontal.magnitude / nominalSpeed, 0.32f, 1.05f);
            }

            if (passType == BasketballPassType.Lob)
            {
                flightTime = Mathf.Min(1.2f, flightTime * 1.35f);
            }

            Vector3 desiredVelocity = CalculateDesiredReleaseVelocity(
                catchPoint,
                releasePosition,
                flightTime);
            Vector3 desiredDirection = desiredVelocity.sqrMagnitude > 1e-8f
                ? desiredVelocity.normalized
                : passer.transform.forward;

            return new BasketballPassPlan
            {
                Passer = passer,
                Receiver = receiver,
                PassType = passType,
                PossessionVersion = possessionVersion,
                PredictedCatchPoint = catchPoint,
                ExpectedFlightTime = flightTime,
                DesiredReleasePosition = releasePosition,
                DesiredReleaseDirection = desiredDirection,
                DesiredReleaseVelocity = desiredVelocity,
                MaximumDirectionCorrection = 28f,
                MaximumSpeedScale = 1.6f,
                MaximumVerticalCorrection = 2.25f
            };
        }

        public static void RefreshRelease(
            ref BasketballPassPlan plan,
            Vector3 releasePosition)
        {
            plan.DesiredReleasePosition = releasePosition;
            plan.DesiredReleaseVelocity = CalculateDesiredReleaseVelocity(
                plan.PredictedCatchPoint,
                releasePosition,
                plan.ExpectedFlightTime);
            plan.DesiredReleaseDirection = plan.DesiredReleaseVelocity.sqrMagnitude > 1e-8f
                ? plan.DesiredReleaseVelocity.normalized
                : plan.Passer.transform.forward;
        }

        private static Vector3 CalculateDesiredReleaseVelocity(
            Vector3 catchPoint,
            Vector3 releasePosition,
            float flightTime)
        {
            float safeTime = Mathf.Max(flightTime, 0.05f);
            return (catchPoint - releasePosition -
                    0.5f * Physics.gravity * safeTime * safeTime) / safeTime;
        }
    }
}
