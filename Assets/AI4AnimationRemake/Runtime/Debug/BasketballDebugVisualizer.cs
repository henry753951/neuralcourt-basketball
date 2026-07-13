using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BasketballDebugVisualizer : MonoBehaviour
    {
        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private bool showDebugLines = true;

        private LineRenderer trajectory;
        private Material runtimeMaterial;

        public bool ShowDebugLines => showDebugLines;

        private void Awake()
        {
            trajectory = GetComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            runtimeMaterial = new Material(shader);
            trajectory.sharedMaterial = runtimeMaterial;
            trajectory.useWorldSpace = true;
            trajectory.loop = false;
            trajectory.startWidth = 0.025f;
            trajectory.endWidth = 0.025f;
            trajectory.startColor = new Color(1f, 0.7f, 0f, 0.9f);
            trajectory.endColor = new Color(1f, 0.25f, 0f, 0.35f);
            trajectory.positionCount = BasketballAgentState.KeyCount;
        }

        private void LateUpdate()
        {
            if (trajectory == null)
            {
                return;
            }
            bool visible = showDebugLines && controller != null && controller.IsInitialized &&
                controller.State != null;
            trajectory.enabled = visible;
            if (!visible)
            {
                return;
            }
            BasketballAgentState state = controller.State;
            for (int key = 0; key < BasketballAgentState.KeyCount; key++)
            {
                trajectory.SetPosition(key, state.RootPositions[BasketballAgentState.KeyIndex(key)] +
                    Vector3.up * 0.035f);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        public void SetVisible(bool value)
        {
            showDebugLines = value;
        }

#if UNITY_EDITOR
        public void Configure(BasketballNeuralController value)
        {
            controller = value;
            showDebugLines = true;
        }
#endif
    }
}
