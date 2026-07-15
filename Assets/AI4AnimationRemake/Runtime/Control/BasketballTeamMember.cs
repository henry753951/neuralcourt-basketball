using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballTeamMember : MonoBehaviour
    {
        [SerializeField, Min(0)] private int playerIndex;
        [SerializeField, Min(0)] private int teamId;
        [SerializeField, Range(0, 99)] private int jerseyNumber = 1;
        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private BasketballDebugVisualizer visualizer;
        [SerializeField] private BasketballTargetIndicator indicator;

        public int PlayerIndex => playerIndex;
        public int TeamId => teamId;
        public int JerseyNumber => jerseyNumber;
        public BasketballNeuralController Controller => controller;
        public BasketballKeyboardMouseInputProvider InputProvider => inputProvider;
        public BasketballDebugVisualizer Visualizer => visualizer;
        public BasketballTargetIndicator Indicator => indicator;

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
            GetComponentInChildren<BasketballPlayerAppearance>(true)?.Apply();
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
