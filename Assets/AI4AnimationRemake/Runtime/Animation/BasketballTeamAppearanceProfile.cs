using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [CreateAssetMenu(
        fileName = "BasketballTeamAppearance",
        menuName = "AI4Animation/Basketball Team Appearance")]
    public sealed class BasketballTeamAppearanceProfile : ScriptableObject
    {
        [SerializeField, Min(0)] private int teamId;
        [SerializeField] private string displayName = "Team";
        [SerializeField] private Color jerseyColor = Color.blue;
        [SerializeField] private Color shortsColor = Color.black;
        [SerializeField] private Color numberColor = Color.white;

        public int TeamId => teamId;
        public string DisplayName => displayName;
        public Color JerseyColor => jerseyColor;
        public Color ShortsColor => shortsColor;
        public Color NumberColor => numberColor;

#if UNITY_EDITOR
        public void Configure(
            int id,
            string teamName,
            Color jersey,
            Color shorts,
            Color number)
        {
            teamId = Mathf.Max(0, id);
            displayName = string.IsNullOrWhiteSpace(teamName) ? $"Team {teamId}" : teamName;
            jerseyColor = jersey;
            shortsColor = shorts;
            numberColor = number;
        }
#endif
    }
}
