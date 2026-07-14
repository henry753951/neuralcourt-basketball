using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballPossessionState
    {
        Possessed,
        PassPreparing,
        PassFlight,
        ShotFlight,
        Loose,
        Contested,
        CatchBlend
    }

    public enum BasketballBallControlMode
    {
        NeuralPossession,
        NeuralPassPreparation,
        PhysicsFlight,
        PhysicsContested,
        CatchBlend
    }

    public enum BasketballPassType
    {
        Chest,
        Lead,
        Lob
    }

    public enum BasketballMLPassPhase
    {
        None,
        Gather,
        Align,
        Push,
        ReleasePending,
        Completed,
        Failed
    }

    [Serializable]
    public struct BasketballPassPlan
    {
        public BasketballTeamMember Passer;
        public BasketballTeamMember Receiver;
        public BasketballPassType PassType;
        public int PossessionVersion;
        public Vector3 PredictedCatchPoint;
        public float ExpectedFlightTime;
        public Vector3 DesiredReleasePosition;
        public Vector3 DesiredReleaseDirection;
        public Vector3 DesiredReleaseVelocity;
        public float MaximumDirectionCorrection;
        public float MaximumSpeedScale;
        public float MaximumVerticalCorrection;
    }

    public readonly struct BasketballBallObservation
    {
        public BasketballBallObservation(
            int tick,
            Vector3 position,
            Vector3 velocity,
            Vector3 previousVelocity,
            Vector3 rootPosition,
            Vector3 rootForward,
            Vector3 leftHandPosition,
            Vector3 rightHandPosition,
            Vector3 leftHandVelocity,
            Vector3 rightHandVelocity,
            float leftHandContact,
            float rightHandContact,
            float ballContact,
            bool catchIntent,
            bool shootIntent,
            bool stealIntent = false)
        {
            Tick = tick;
            Position = position;
            Velocity = velocity;
            PreviousVelocity = previousVelocity;
            RootPosition = rootPosition;
            RootForward = rootForward;
            LeftHandPosition = leftHandPosition;
            RightHandPosition = rightHandPosition;
            LeftHandVelocity = leftHandVelocity;
            RightHandVelocity = rightHandVelocity;
            LeftHandContact = leftHandContact;
            RightHandContact = rightHandContact;
            BallContact = ballContact;
            CatchIntent = catchIntent;
            ShootIntent = shootIntent;
            StealIntent = stealIntent;
        }

        public int Tick { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public Vector3 PreviousVelocity { get; }
        public Vector3 RootPosition { get; }
        public Vector3 RootForward { get; }
        public Vector3 LeftHandPosition { get; }
        public Vector3 RightHandPosition { get; }
        public Vector3 LeftHandVelocity { get; }
        public Vector3 RightHandVelocity { get; }
        public float LeftHandContact { get; }
        public float RightHandContact { get; }
        public float BallContact { get; }
        public bool CatchIntent { get; }
        public bool ShootIntent { get; }
        public bool StealIntent { get; }

        public float HandContact => Mathf.Max(LeftHandContact, RightHandContact);

        public float MinimumHandDistance => Mathf.Min(
            Vector3.Distance(LeftHandPosition, Position),
            Vector3.Distance(RightHandPosition, Position));

        public Vector3 ClosestHandVelocity =>
            Vector3.SqrMagnitude(LeftHandPosition - Position) <=
            Vector3.SqrMagnitude(RightHandPosition - Position)
                ? LeftHandVelocity
                : RightHandVelocity;
    }

    public readonly struct BasketballPassControlProfile
    {
        public BasketballPassControlProfile(
            BasketballMLPassPhase phase,
            float holdStyle,
            float shootStyle,
            Vector3 facing)
        {
            Phase = phase;
            HoldStyle = holdStyle;
            ShootStyle = shootStyle;
            Facing = facing;
        }

        public BasketballMLPassPhase Phase { get; }
        public float HoldStyle { get; }
        public float ShootStyle { get; }
        public Vector3 Facing { get; }
    }
}
