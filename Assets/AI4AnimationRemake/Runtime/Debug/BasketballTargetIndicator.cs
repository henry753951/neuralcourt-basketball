using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballTargetIndicatorState
    {
        Hidden,
        ActivePlayer,
        PassLocked
    }

    [DisallowMultipleComponent]
    public sealed class BasketballTargetIndicator : MonoBehaviour
    {
        private const int SegmentCount = 48;
        private static readonly Color ActiveColor = new(0.25f, 0.78f, 1f, 0.92f);
        private static readonly Color LockedColor = new(1f, 0.68f, 0.12f, 1f);

        [SerializeField, Min(0.05f)] private float radius = 0.34f;
        [SerializeField, Min(0.1f)] private float height = 2.25f;
        [SerializeField, Min(0.001f)] private float width = 0.025f;

        private LineRenderer ring;
        private Material runtimeMaterial;
        private BasketballTargetIndicatorState state;
        private Camera viewCamera;

        public BasketballTargetIndicatorState State => state;

        private void Awake()
        {
            GameObject visual = new("Target Indicator Visual");
            visual.transform.SetParent(transform, false);
            ring = visual.AddComponent<LineRenderer>();
            // Prioritize URP shaders for this URP project
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[BasketballTargetIndicator] No suitable shader found. Ring will be invisible.");
                shader = Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Sprites/Default");
            }
            runtimeMaterial = new Material(shader);
            ring.sharedMaterial = runtimeMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = SegmentCount;
            ring.startWidth = width;
            ring.endWidth = width;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (int segment = 0; segment < SegmentCount; segment++)
            {
                float angle = 2f * Mathf.PI * segment / SegmentCount;
                ring.SetPosition(segment, new Vector3(
                    radius * Mathf.Cos(angle),
                    radius * Mathf.Sin(angle),
                    0f));
            }
            state = BasketballTargetIndicatorState.Hidden;
            ApplyState();
        }

        private void LateUpdate()
        {
            if (ring == null || state == BasketballTargetIndicatorState.Hidden)
            {
                return;
            }

            viewCamera = viewCamera != null ? viewCamera : Camera.main;
            Transform visual = ring.transform;
            if (state == BasketballTargetIndicatorState.ActivePlayer)
            {
                visual.position = transform.position + Vector3.up * 0.035f;
                visual.rotation = Quaternion.Euler(90f, 0f, 0f);
                return;
            }

            visual.position = transform.position + Vector3.up * height;
            if (viewCamera != null)
            {
                Vector3 towardCamera = viewCamera.transform.position - visual.position;
                if (towardCamera.sqrMagnitude > 1e-8f)
                {
                    visual.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);
                }
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        public void SetState(BasketballTargetIndicatorState value)
        {
            if (state == value)
            {
                return;
            }
            state = value;
            ApplyState();
        }

        private void ApplyState()
        {
            if (ring == null)
            {
                return;
            }
            ring.enabled = state != BasketballTargetIndicatorState.Hidden;
            Color color = state == BasketballTargetIndicatorState.PassLocked
                ? LockedColor
                : ActiveColor;
            ring.startColor = color;
            ring.endColor = color;
            ring.startWidth = state == BasketballTargetIndicatorState.PassLocked
                ? width * 1.6f
                : width;
            ring.endWidth = ring.startWidth;
            ring.transform.localScale = state == BasketballTargetIndicatorState.ActivePlayer
                ? Vector3.one * 1.3f
                : Vector3.one;
        }
    }
}
