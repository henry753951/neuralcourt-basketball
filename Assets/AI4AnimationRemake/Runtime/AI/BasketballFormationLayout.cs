using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Deterministic court-local starting poses for every supported roster size.
    /// Team zero starts on negative Z and faces positive Z; team one is mirrored.
    /// </summary>
    public static class BasketballFormationLayout
    {
        public const int MaximumPlayersPerTeam = 5;

        private static readonly Vector3[] OnePlayer =
        {
            new(0f, 0f, -3.8f)
        };

        private static readonly Vector3[] TwoPlayers =
        {
            new(-2.6f, 0f, -2f),
            new(2.6f, 0f, -2f)
        };

        private static readonly Vector3[] ThreePlayers =
        {
            new(-3.8f, 0f, -2.2f),
            new(0f, 0f, -4.8f),
            new(3.8f, 0f, -2.2f)
        };

        private static readonly Vector3[] FourPlayers =
        {
            new(-4.5f, 0f, -3.6f),
            new(-1.5f, 0f, -1.5f),
            new(1.5f, 0f, -1.5f),
            new(4.5f, 0f, -3.6f)
        };

        // Preserve the original 5v5 scene-builder positions exactly.
        private static readonly Vector3[] FivePlayers =
        {
            new(-5.2f, 0f, -3.8f),
            new(-2.6f, 0f, -1.5f),
            new(0f, 0f, -4.8f),
            new(2.6f, 0f, -1.5f),
            new(5.2f, 0f, -3.8f)
        };

        public static Vector3 GetLocalOnCourtPosition(
            int teamId,
            int teamSlot,
            int activePlayerCount)
        {
            Vector3[] layout = GetLayout(activePlayerCount);
            int slot = Mathf.Clamp(teamSlot, 0, layout.Length - 1);
            Vector3 position = layout[slot];
            if (teamId == 1)
            {
                position.z = -position.z;
            }
            return position;
        }

        public static Vector3 GetLocalFacingDirection(int teamId)
            => teamId == 1 ? Vector3.back : Vector3.forward;

        public static void GetWorldOnCourtPose(
            BasketballCourt court,
            int teamId,
            int teamSlot,
            int activePlayerCount,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 localPosition = GetLocalOnCourtPosition(
                teamId,
                teamSlot,
                activePlayerCount);
            Vector3 localForward = GetLocalFacingDirection(teamId);
            position = court != null
                ? court.CourtToWorldPoint(localPosition)
                : localPosition;
            Vector3 forward = court != null
                ? court.CourtToWorldDirection(localForward)
                : localForward;
            Vector3 up = court != null
                ? court.CourtUp
                : Vector3.up;
            rotation = Quaternion.LookRotation(forward, up);
        }

        public static void GetWorldBenchPose(
            BasketballCourt court,
            int teamId,
            int teamSlot,
            out Vector3 position,
            out Quaternion rotation)
        {
            float courtWidth = court != null ? court.CourtWidth : 15f;
            float side = teamId == 1 ? 1f : -1f;
            Vector3 localPosition = new(
                side * (courtWidth * 0.5f + 2f),
                0f,
                (teamSlot - 2) * 1.35f);
            Vector3 localForward = teamId == 1 ? Vector3.left : Vector3.right;
            position = court != null
                ? court.CourtToWorldPoint(localPosition)
                : localPosition;
            Vector3 forward = court != null
                ? court.CourtToWorldDirection(localForward)
                : localForward;
            Vector3 up = court != null
                ? court.CourtUp
                : Vector3.up;
            rotation = Quaternion.LookRotation(forward, up);
        }

        private static Vector3[] GetLayout(int activePlayerCount)
        {
            switch (Mathf.Clamp(activePlayerCount, 1, MaximumPlayersPerTeam))
            {
                case 1:
                    return OnePlayer;
                case 2:
                    return TwoPlayers;
                case 3:
                    return ThreePlayers;
                case 4:
                    return FourPlayers;
                default:
                    return FivePlayers;
            }
        }
    }
}
