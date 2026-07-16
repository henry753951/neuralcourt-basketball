using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public readonly struct BasketballShotPlan
    {
        public BasketballShotPlan(
            BasketballTeamMember shooter,
            BasketballHoop hoop,
            Vector3 releasePosition,
            Vector3 targetPoint,
            Vector3 desiredVelocity,
            Vector3 modelVelocity,
            float flightTime,
            float apexHeight,
            bool threePointer,
            bool supported)
        {
            Shooter = shooter;
            Hoop = hoop;
            ReleasePosition = releasePosition;
            TargetPoint = targetPoint;
            DesiredVelocity = desiredVelocity;
            ModelVelocity = modelVelocity;
            FlightTime = flightTime;
            ApexHeight = apexHeight;
            IsThreePointer = threePointer;
            IsSupported = supported;
        }

        public BasketballTeamMember Shooter { get; }
        public BasketballHoop Hoop { get; }
        public Vector3 ReleasePosition { get; }
        public Vector3 TargetPoint { get; }
        public Vector3 DesiredVelocity { get; }
        public Vector3 ModelVelocity { get; }
        public float FlightTime { get; }
        public float ApexHeight { get; }
        public bool IsThreePointer { get; }
        public bool IsSupported { get; }
        public float Distance => Vector3.Distance(ReleasePosition, TargetPoint);
        public float DirectionCorrection => ModelVelocity.sqrMagnitude > 1e-8f
            ? Vector3.Angle(ModelVelocity, DesiredVelocity)
            : 180f;
    }

    public static class BasketballShotPlanner
    {
        public static bool TryCreate(
            BasketballTeamMember shooter,
            BasketballCourt court,
            Vector3 releasePosition,
            Vector3 modelVelocity,
            BasketballRuntimeSettings settings,
            out BasketballShotPlan plan)
        {
            plan = default;
            BasketballHoop hoop = court != null && shooter != null
                ? court.GetAttackHoop(shooter.TeamId)
                : null;
            if (hoop == null)
            {
                return false;
            }

            Vector3 target = hoop.AimPoint;
            Vector3 gravity = Physics.gravity;
            float gravityMagnitude = Mathf.Abs(gravity.y);
            if (gravityMagnitude < 0.01f)
            {
                return false;
            }

            float planarDistance = Vector2.Distance(
                new Vector2(releasePosition.x, releasePosition.z),
                new Vector2(target.x, target.z));
            float baseClearance = settings != null
                ? settings.ShotApexClearance
                : 1.05f;
            float distanceScale = settings != null
                ? settings.ShotDistanceApexScale
                : 0.035f;
            float maximumClearance = settings != null
                ? settings.MaximumShotApexClearance
                : 2.1f;
            float clearance = Mathf.Min(
                maximumClearance,
                baseClearance + planarDistance * distanceScale);
            float apexHeight = Mathf.Max(releasePosition.y, target.y) + clearance;
            float rise = Mathf.Max(0.01f, apexHeight - releasePosition.y);
            float fall = Mathf.Max(0.01f, apexHeight - target.y);
            float riseTime = Mathf.Sqrt(2f * rise / gravityMagnitude);
            float fallTime = Mathf.Sqrt(2f * fall / gravityMagnitude);
            float flightTime = Mathf.Max(0.1f, riseTime + fallTime);
            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                target - releasePosition,
                Vector3.up) / flightTime;
            Vector3 desiredVelocity = planarVelocity + Vector3.up *
                                      (gravityMagnitude * riseTime);
            float maximumSpeed = settings != null
                ? settings.MaximumShotLaunchSpeed
                : 18f;
            if (shooter != null)
            {
                maximumSpeed = Mathf.Min(
                    maximumSpeed,
                    shooter.MaximumShotReleaseSpeed);
            }
            bool supported = desiredVelocity.magnitude <= maximumSpeed;
            supported &= shooter == null ||
                         planarDistance <= shooter.MaximumEffectiveShotDistance;
            if (!supported)
            {
                desiredVelocity = Vector3.ClampMagnitude(desiredVelocity, maximumSpeed);
            }

            plan = new BasketballShotPlan(
                shooter,
                hoop,
                releasePosition,
                target,
                desiredVelocity,
                modelVelocity,
                flightTime,
                apexHeight,
                court.IsThreePoint(releasePosition, hoop),
                supported);
            return true;
        }
    }
}
