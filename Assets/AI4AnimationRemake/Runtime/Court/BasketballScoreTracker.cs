using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public readonly struct BasketballScoreEvent
    {
        public BasketballScoreEvent(
            int teamId,
            int playerIndex,
            int points,
            Vector3 releasePosition,
            BasketballHoop hoop)
        {
            TeamId = teamId;
            PlayerIndex = playerIndex;
            Points = points;
            ReleasePosition = releasePosition;
            Hoop = hoop;
        }

        public int TeamId { get; }
        public int PlayerIndex { get; }
        public int Points { get; }
        public Vector3 ReleasePosition { get; }
        public BasketballHoop Hoop { get; }
    }

    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class BasketballScoreTracker : MonoBehaviour
    {
        [SerializeField] private BasketballCourt court;
        [SerializeField] private BasketballBallController ball;
        [SerializeField] private BasketballPossessionManager possessionManager;
        [SerializeField, Min(0.02f)] private float scoringCenterRadius = 0.14f;
        [SerializeField, Min(0.1f)] private float scoreCooldown = 0.75f;

        private Vector3 previousBallPosition;
        private float lastScoreTime = float.NegativeInfinity;
        private bool hasPreviousPosition;

        public int TeamZeroScore { get; private set; }
        public int TeamOneScore { get; private set; }
        public BasketballScoreEvent LastScore { get; private set; }
        public event Action<BasketballScoreEvent> Scored;

        private void FixedUpdate()
        {
            if (court == null || ball == null || possessionManager == null)
            {
                return;
            }

            Vector3 current = ball.transform.position;
            if (!hasPreviousPosition)
            {
                previousBallPosition = current;
                hasPreviousPosition = true;
                return;
            }

            if (possessionManager.BallState == BasketballPossessionState.ShotFlight &&
                Time.time - lastScoreTime >= scoreCooldown)
            {
                if (!TryScore(court.PositiveZHoop, previousBallPosition, current))
                {
                    TryScore(court.NegativeZHoop, previousBallPosition, current);
                }
            }
            previousBallPosition = current;
        }

        private bool TryScore(
            BasketballHoop hoop,
            Vector3 previous,
            Vector3 current)
        {
            if (hoop == null || current.y >= previous.y)
            {
                return false;
            }

            float hoopY = hoop.Center.y;
            if (previous.y < hoopY || current.y > hoopY)
            {
                return false;
            }

            float denominator = previous.y - current.y;
            if (denominator < 1e-5f)
            {
                return false;
            }
            float t = Mathf.Clamp01((previous.y - hoopY) / denominator);
            Vector3 crossing = Vector3.Lerp(previous, current, t);
            Vector2 crossingXZ = new(crossing.x, crossing.z);
            Vector2 hoopXZ = new(hoop.Center.x, hoop.Center.z);
            float allowedRadius = Mathf.Min(scoringCenterRadius, hoop.RimRadius);
            if (Vector2.Distance(crossingXZ, hoopXZ) > allowedRadius)
            {
                return false;
            }

            BasketballShotPlan shot = possessionManager.LastShotPlan;
            if (shot.Shooter == null || shot.Hoop != hoop ||
                shot.Shooter.TeamId != hoop.AttackedByTeamId)
            {
                return false;
            }

            int points = shot.IsThreePointer ? 3 : 2;
            if (shot.Shooter.TeamId == 0)
            {
                TeamZeroScore += points;
            }
            else if (shot.Shooter.TeamId == 1)
            {
                TeamOneScore += points;
            }
            LastScore = new BasketballScoreEvent(
                shot.Shooter.TeamId,
                shot.Shooter.PlayerIndex,
                points,
                shot.ReleasePosition,
                hoop);
            lastScoreTime = Time.time;
            Scored?.Invoke(LastScore);
            return true;
        }

        public void ResetScore()
        {
            TeamZeroScore = 0;
            TeamOneScore = 0;
            LastScore = default;
            lastScoreTime = float.NegativeInfinity;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballCourt basketballCourt,
            BasketballBallController basketball,
            BasketballPossessionManager possession)
        {
            court = basketballCourt;
            ball = basketball;
            possessionManager = possession;
            scoringCenterRadius = 0.14f;
            scoreCooldown = 0.75f;
        }
#endif
    }
}
