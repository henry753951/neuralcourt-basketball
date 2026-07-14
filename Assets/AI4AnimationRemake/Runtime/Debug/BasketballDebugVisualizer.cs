using UnityEngine;
using UnityEngine.Rendering;

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
            // The scene stores this renderer without a material or trajectory data.
            // Keep it hidden until the runtime state below is fully configured.
            trajectory.enabled = false;
            // The URP particle shader consumes LineRenderer vertex colors, unlike the
            // regular URP Unlit shader which can flatten the whole trail to white.
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[BasketballDebugVisualizer] No suitable shader found. Line renderer will be invisible.");
                return;
            }
            runtimeMaterial = new Material(shader);
            ConfigureRuntimeMaterial(runtimeMaterial, shader);
            trajectory.sharedMaterial = runtimeMaterial;
            trajectory.useWorldSpace = true;
            trajectory.loop = false;
            trajectory.alignment = LineAlignment.View;
            trajectory.textureMode = LineTextureMode.Stretch;
            trajectory.numCornerVertices = 5;
            trajectory.numCapVertices = 5;
            trajectory.widthMultiplier = 0.045f;
            trajectory.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.08f),
                new Keyframe(0.12f, 0.72f),
                new Keyframe(0.48f, 1f),
                new Keyframe(0.82f, 0.62f),
                new Keyframe(1f, 0.06f));
            Gradient gradient = new();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.08f, 0.38f, 1f), 0f),
                    new GradientColorKey(new Color(0.04f, 0.92f, 0.92f), 0.48f),
                    new GradientColorKey(new Color(1f, 0.48f, 0.06f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.08f, 0f),
                    new GradientAlphaKey(0.92f, 0.42f),
                    new GradientAlphaKey(0.72f, 0.78f),
                    new GradientAlphaKey(0.12f, 1f)
                });
            trajectory.colorGradient = gradient;
            trajectory.positionCount = BasketballAgentState.KeyCount;
        }

        private static void ConfigureRuntimeMaterial(Material material, Shader shader)
        {
            bool usesVertexColors = shader.name == "Universal Render Pipeline/Particles/Unlit" ||
                shader.name == "Sprites/Default";
            Color tint = usesVertexColors ? Color.white : new Color(0.04f, 0.92f, 0.92f, 1f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", tint);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", tint);
            }

            if (shader.name != "Universal Render Pipeline/Particles/Unlit")
            {
                return;
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void LateUpdate()
        {
            if (trajectory == null || runtimeMaterial == null)
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
            if (!value && trajectory != null)
            {
                trajectory.enabled = false;
            }
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
