using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballTeamMember : MonoBehaviour
    {
        [SerializeField, Min(0)] private int playerIndex;
        [SerializeField, Min(0)] private int teamId;
        [SerializeField, Range(0, 99)] private int jerseyNumber = 1;
        [Tooltip("Per-player power multiplier applied to the team pass and shot limits.")]
        [SerializeField, Range(0.5f, 1.25f)] private float maximumPower = 1f;
        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private BasketballDebugVisualizer visualizer;
        [SerializeField] private BasketballTargetIndicator indicator;
        [SerializeField] private BasketballPlayerBodyContact bodyContact;

        public int PlayerIndex => playerIndex;
        public int TeamId => teamId;
        public int JerseyNumber => jerseyNumber;
        public float MaximumPower => maximumPower;
        public BasketballNeuralController Controller => controller;
        public BasketballKeyboardMouseInputProvider InputProvider => inputProvider;
        public BasketballDebugVisualizer Visualizer => visualizer;
        public BasketballTargetIndicator Indicator => indicator;
        public BasketballPlayerBodyContact BodyContact => bodyContact != null
            ? bodyContact
            : bodyContact = GetComponent<BasketballPlayerBodyContact>();
        public bool IsOnCourt => gameObject.activeSelf;
        public BasketballTeamGroup TeamGroup => teamGroup != null
            ? teamGroup
            : teamGroup = GetComponentInParent<BasketballTeamGroup>();
        public float MaximumPassReleaseSpeed =>
            (TeamGroup != null ? TeamGroup.MaximumPassReleaseSpeed : 12f) * maximumPower;
        public float MaximumShotReleaseSpeed =>
            (TeamGroup != null ? TeamGroup.MaximumShotReleaseSpeed : 11.5f) * maximumPower;
        public float MaximumEffectivePassDistance =>
            (TeamGroup != null ? TeamGroup.MaximumEffectivePassDistance : 10.5f) *
            maximumPower;
        public float MaximumEffectiveShotDistance =>
            (TeamGroup != null ? TeamGroup.MaximumEffectiveShotDistance : 8.25f) *
            maximumPower;

        private BasketballTeamGroup teamGroup;

        public Vector3 AimPoint
        {
            get
            {
                BasketballAgentState state = controller != null ? controller.State : null;
                return state != null
                    ? state.BonePositions[14] + Vector3.up * 0.06f
                    : transform.position + Vector3.up * 1.25f;
            }
        }

        private void OnValidate()
        {
            playerIndex = Mathf.Max(0, playerIndex);
            teamId = Mathf.Max(0, teamId);
            jerseyNumber = Mathf.Clamp(jerseyNumber, 0, 99);
            maximumPower = Mathf.Clamp(maximumPower, 0.5f, 1.25f);
            teamGroup = GetComponentInParent<BasketballTeamGroup>();
            bodyContact = GetComponent<BasketballPlayerBodyContact>();
            GetComponentInChildren<BasketballPlayerAppearance>(true)?.Apply();
        }

        private void OnTransformParentChanged()
        {
            teamGroup = GetComponentInParent<BasketballTeamGroup>();
        }

        public void SetOnCourt(bool value)
        {
            if (!value && controller != null)
            {
                controller.SetIntentOverride(default);
                controller.SetRival(null);
            }
            if (gameObject.activeSelf != value)
            {
                gameObject.SetActive(value);
            }
        }

#if UNITY_EDITOR
        public void Configure(
            int index,
            int team,
            int number,
            BasketballNeuralController neuralController,
            BasketballKeyboardMouseInputProvider provider,
            BasketballDebugVisualizer debugVisualizer,
            BasketballTargetIndicator targetIndicator)
        {
            playerIndex = index;
            teamId = team;
            jerseyNumber = Mathf.Clamp(number, 0, 99);
            controller = neuralController;
            inputProvider = provider;
            visualizer = debugVisualizer;
            indicator = targetIndicator;
        }
#endif
    }
}
