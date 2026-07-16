using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Team-level authoring root. Values shared by a roster live here so player
    /// prefab instances only contain true per-player differences.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasketballTeamGroup : MonoBehaviour
    {
        [Header("Team Identity")]
        [SerializeField, Min(0)] private int teamId;
        [SerializeField] private string displayName = "Home";

        [Header("Shared Athletic Limits")]
        [Tooltip("Maximum physical release speed available to a player with Power = 1.0.")]
        [SerializeField, Min(1f)] private float maximumPassReleaseSpeed = 12f;
        [Tooltip("Maximum physical shot release speed available to a player with Power = 1.0.")]
        [SerializeField, Min(1f)] private float maximumShotReleaseSpeed = 11.5f;
        [Tooltip("AI will not intentionally pass farther than this distance.")]
        [SerializeField, Min(1f)] private float maximumEffectivePassDistance = 10.5f;
        [Tooltip("AI will not intentionally shoot farther than this distance.")]
        [SerializeField, Min(1f)] private float maximumEffectiveShotDistance = 8.25f;

        public int TeamId => teamId;
        public string DisplayName => displayName;
        public float MaximumPassReleaseSpeed => maximumPassReleaseSpeed;
        public float MaximumShotReleaseSpeed => maximumShotReleaseSpeed;
        public float MaximumEffectivePassDistance => maximumEffectivePassDistance;
        public float MaximumEffectiveShotDistance => maximumEffectiveShotDistance;

        private void OnValidate()
        {
            teamId = Mathf.Max(0, teamId);
            maximumPassReleaseSpeed = Mathf.Max(1f, maximumPassReleaseSpeed);
            maximumShotReleaseSpeed = Mathf.Max(1f, maximumShotReleaseSpeed);
            maximumEffectivePassDistance = Mathf.Max(1f, maximumEffectivePassDistance);
            maximumEffectiveShotDistance = Mathf.Max(1f, maximumEffectiveShotDistance);
        }

#if UNITY_EDITOR
        public void Configure(
            int id,
            string teamDisplayName,
            float passSpeed = 12f,
            float shotSpeed = 11.5f,
            float passDistance = 10.5f,
            float shotDistance = 8.25f)
        {
            teamId = Mathf.Max(0, id);
            displayName = string.IsNullOrWhiteSpace(teamDisplayName)
                ? $"Team {teamId}"
                : teamDisplayName;
            maximumPassReleaseSpeed = Mathf.Max(1f, passSpeed);
            maximumShotReleaseSpeed = Mathf.Max(1f, shotSpeed);
            maximumEffectivePassDistance = Mathf.Max(1f, passDistance);
            maximumEffectiveShotDistance = Mathf.Max(1f, shotDistance);
        }
#endif
    }
}
