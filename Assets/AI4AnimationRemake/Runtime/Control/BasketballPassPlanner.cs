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
            BasketballPassType passType = BasketballPassType.Lead,
            BasketballRuntimeSettings settings = null)
        {
            settings = settings != null
                ? settings
                : BasketballRuntimeSettings.LoadDefault();
            BasketballAgentState receiverState = receiver.Controller.State;
            Vector3 receiverVelocity = receiverState != null
                ? receiverState.RootVelocities[BasketballAgentState.Pivot]
                : Vector3.zero;
            receiverVelocity = Vector3.ProjectOnPlane(receiverVelocity, Vector3.up);
            Vector3 catchPoint = receiver.AimPoint;
            float flightTime = 0.5f;
            Vector3 desiredVelocity = Vector3.zero;

            for (int iteration = 0; iteration < 4; iteration++)
            {
                float leadScale = passType == BasketballPassType.Chest
                    ? 0f
                    : settings != null ? settings.ReceiverLeadScale : 0.9f;
                float maximumLead = settings != null
                    ? settings.MaximumReceiverLeadDistance
                    : 1.35f;
                Vector3 lead = Vector3.ClampMagnitude(
                    leadScale * receiverVelocity * flightTime,
                    maximumLead);
                catchPoint = receiver.AimPoint + lead;
                SolveTrajectory(
                    catchPoint,
                    releasePosition,
                    passType,
                    settings,
                    out desiredVelocity,
                    out flightTime);
            }
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
            Vector3 releasePosition,
            BasketballRuntimeSettings settings = null)
        {
            plan.DesiredReleasePosition = releasePosition;
            SolveTrajectory(
                plan.PredictedCatchPoint,
                releasePosition,
                plan.PassType,
                settings != null ? settings : BasketballRuntimeSettings.LoadDefault(),
                out Vector3 velocity,
                out float flightTime);
            plan.DesiredReleaseVelocity = velocity;
            plan.ExpectedFlightTime = flightTime;
            plan.DesiredReleaseDirection = plan.DesiredReleaseVelocity.sqrMagnitude > 1e-8f
                ? plan.DesiredReleaseVelocity.normalized
                : plan.Passer.transform.forward;
        }

        private static void SolveTrajectory(
            Vector3 catchPoint,
            Vector3 releasePosition,
            BasketballPassType passType,
            BasketballRuntimeSettings settings,
            out Vector3 velocity,
            out float flightTime)
        {
            float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
            float clearance = passType == BasketballPassType.Lob
                ? settings != null ? settings.LobPassApexClearance : 0.9f
                : settings != null ? settings.DirectPassApexClearance : 0.28f;
            float maximumHorizontalSpeed = passType == BasketballPassType.Lob
                ? settings != null ? settings.MaximumLobPassSpeed : 7f
                : settings != null ? settings.MaximumDirectPassSpeed : 8f;
            Vector3 horizontal = Vector3.ProjectOnPlane(
                catchPoint - releasePosition,
                Vector3.up);
            float horizontalDistance = horizontal.magnitude;
            float endpointY = Mathf.Max(releasePosition.y, catchPoint.y);
            float apexY = endpointY + clearance;
            flightTime = CalculateFlightTime(
                releasePosition.y,
                catchPoint.y,
                apexY,
                gravity);

            float requiredTime = horizontalDistance / Mathf.Max(1f, maximumHorizontalSpeed);
            if (flightTime < requiredTime)
            {
                float lowApex = apexY;
                float highApex = apexY;
                for (int iteration = 0; iteration < 8 &&
                     CalculateFlightTime(
                         releasePosition.y,
                         catchPoint.y,
                         highApex,
                         gravity) < requiredTime; iteration++)
                {
                    highApex = endpointY + 2f * (highApex - endpointY);
                }
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    float middleApex = 0.5f * (lowApex + highApex);
                    if (CalculateFlightTime(
                            releasePosition.y,
                            catchPoint.y,
                            middleApex,
                            gravity) < requiredTime)
                    {
                        lowApex = middleApex;
                    }
                    else
                    {
                        highApex = middleApex;
                    }
                }
                apexY = highApex;
                flightTime = CalculateFlightTime(
                    releasePosition.y,
                    catchPoint.y,
                    apexY,
                    gravity);
            }

            float verticalSpeed = Mathf.Sqrt(
                2f * gravity * Mathf.Max(0f, apexY - releasePosition.y));
            velocity = horizontal / Mathf.Max(0.05f, flightTime) +
                       Vector3.up * verticalSpeed;
        }

        private static float CalculateFlightTime(
            float releaseY,
            float catchY,
            float apexY,
            float gravity)
        {
            float ascent = Mathf.Sqrt(
                2f * Mathf.Max(0f, apexY - releaseY) / gravity);
            float descent = Mathf.Sqrt(
                2f * Mathf.Max(0f, apexY - catchY) / gravity);
            return Mathf.Max(0.05f, ascent + descent);
        }
    }
}
