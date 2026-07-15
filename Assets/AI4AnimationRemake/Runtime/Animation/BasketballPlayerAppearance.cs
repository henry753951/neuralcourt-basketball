using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Presentation-only roster appearance. Team/number metadata comes from
    /// BasketballTeamMember while materials remain shared through property blocks.
    /// </summary>
    [DefaultExecutionOrder(450)]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BasketballPlayerAppearance : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("Roster Source")]
        [SerializeField] private BasketballTeamMember teamMember;
        [SerializeField] private BasketballTeamAppearanceProfile[] teamProfiles =
            Array.Empty<BasketballTeamAppearanceProfile>();

        [Header("Visual Renderers")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Renderer jerseyRenderer;
        [SerializeField] private Renderer shortsRenderer;
        [SerializeField] private BasketballJerseyNumberDisplay numberDisplay;
        [SerializeField] private Material[] skinMaterials = Array.Empty<Material>();

        [Header("Optional Overrides")]
        [Tooltip("-1 uses PlayerIndex to select a stable skin variant.")]
        [SerializeField, Min(-1)] private int skinVariant = -1;
        [SerializeField] private bool useRosterJerseyNumber = true;
        [SerializeField, Range(0, 99)] private int jerseyNumberOverride;

        private MaterialPropertyBlock propertyBlock;

        public BasketballTeamMember TeamMember => teamMember;
        public BasketballJerseyNumberDisplay NumberDisplay => numberDisplay;
        public int EffectiveJerseyNumber => useRosterJerseyNumber && teamMember != null
            ? teamMember.JerseyNumber
            : jerseyNumberOverride;

        private void Awake()
        {
            ResolveTeamMember();
            Apply();
        }

        private void OnEnable()
        {
            ResolveTeamMember();
            Apply();
        }

        private void OnValidate()
        {
            ResolveTeamMember();
            Apply();
        }

        public void Apply()
        {
            ResolveTeamMember();
            int playerIndex = teamMember != null ? teamMember.PlayerIndex : 0;
            int teamId = teamMember != null ? teamMember.TeamId : 0;

            if (bodyRenderer != null && skinMaterials != null && skinMaterials.Length > 0)
            {
                int requested = skinVariant >= 0 ? skinVariant : playerIndex;
                Material skin = skinMaterials[Mathf.Abs(requested) % skinMaterials.Length];
                if (skin != null)
                {
                    bodyRenderer.sharedMaterial = skin;
                }
            }

            BasketballTeamAppearanceProfile profile = FindProfile(teamId);
            if (profile != null)
            {
                ApplyColor(jerseyRenderer, profile.JerseyColor);
                ApplyColor(shortsRenderer, profile.ShortsColor);
                numberDisplay?.ApplyNumber(EffectiveJerseyNumber, profile.NumberColor);
            }
            else
            {
                numberDisplay?.ApplyNumber(EffectiveJerseyNumber, Color.white);
            }
        }

        private void ResolveTeamMember()
        {
            if (teamMember == null)
            {
                teamMember = GetComponentInParent<BasketballTeamMember>();
            }
        }

        private BasketballTeamAppearanceProfile FindProfile(int teamId)
        {
            BasketballTeamAppearanceProfile fallback = null;
            if (teamProfiles == null)
            {
                return null;
            }
            for (int index = 0; index < teamProfiles.Length; index++)
            {
                BasketballTeamAppearanceProfile candidate = teamProfiles[index];
                if (candidate == null)
                {
                    continue;
                }
                fallback ??= candidate;
                if (candidate.TeamId == teamId)
                {
                    return candidate;
                }
            }
            return fallback;
        }

        private void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }
            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(propertyBlock);
            propertyBlock.Clear();
        }

#if UNITY_EDITOR
        public void Configure(
            Renderer body,
            Renderer jersey,
            Renderer shorts,
            BasketballJerseyNumberDisplay jerseyNumber,
            Material[] skins,
            BasketballTeamAppearanceProfile[] profiles)
        {
            bodyRenderer = body;
            jerseyRenderer = jersey;
            shortsRenderer = shorts;
            numberDisplay = jerseyNumber;
            skinMaterials = skins ?? Array.Empty<Material>();
            teamProfiles = profiles ?? Array.Empty<BasketballTeamAppearanceProfile>();
            skinVariant = -1;
            useRosterJerseyNumber = true;
            jerseyNumberOverride = 0;
            Apply();
        }
#endif
    }
}
