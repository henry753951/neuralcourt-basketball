using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public struct BasketballIntent
    {
        public Vector2 Move;
        public Vector2 BallControl;
        public float Turn;
        public float Spin;
        public float BallHeight;
        public bool Sprint;
        public bool Hold;
        public bool Shoot;
        // Hold-style preparation without enabling Pivot/Momentum ball targeting.
        // Used by an intended receiver before the real ball enters catch range.
        public bool CatchReady;
        // Runtime interaction intent only. The 2020 model has no Steal style
        // channel, so this must never be presented as a learned steal label.
        public bool Steal;
        // Match-layer metadata. The pretrained model still sees the original Shoot style;
        // this pair only decides whether a targeted fake may hand the shared ball to physics.
        public bool PassTargeting;
        public bool CommitBallRelease;
        // Pass preparation may reuse the original Hold style and root-facing
        // control, but it must not repurpose Ball Target/Pivot/Momentum.
        public bool PassControl;
        public float PassHoldStyle;
        public float PassShootStyle;
        public bool UseWorldMove;
        public Vector3 WorldMove;
        public bool UseWorldFacing;
        public Vector3 WorldFacing;
        public bool IsGamepad;
    }
}
