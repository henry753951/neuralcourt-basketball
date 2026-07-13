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
        public bool IsGamepad;
    }
}
