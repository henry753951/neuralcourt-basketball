using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballTeamMember : MonoBehaviour
    {
        [SerializeField, Min(0)] private int playerIndex;
        [SerializeField, Min(0)] private int teamId;
        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private BasketballDebugVisualizer visualizer;
        [SerializeField] private BasketballTargetIndicator indicator;

        public int PlayerIndex => playerIndex;
        public int TeamId => teamId;
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
                    ? state.BonePositions[14] + Vector3.up * 0.2f
                    : transform.position + Vector3.up * 1.25f;
            }
        }

#if UNITY_EDITOR
        public void Configure(
            int index,
            int team,
            BasketballNeuralController neuralController,
            BasketballKeyboardMouseInputProvider provider,
            BasketballDebugVisualizer debugVisualizer,
            BasketballTargetIndicator targetIndicator)
        {
            playerIndex = index;
            teamId = team;
            controller = neuralController;
            inputProvider = provider;
            visualizer = debugVisualizer;
            indicator = targetIndicator;
        }
#endif
    }
}
