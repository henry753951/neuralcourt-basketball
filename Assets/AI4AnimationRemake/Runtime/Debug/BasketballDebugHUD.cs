using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballDebugHUD : MonoBehaviour
    {
        private static readonly string[] StyleNames = { "Stand", "Move", "Dribble", "Hold", "Shoot" };
        private static readonly string[] ContactNames = { "L Foot", "R Foot", "L Hand", "R Hand", "Ball" };

        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private bool showUIElements = true;

        private readonly float[] gating = new float[BasketballModelAsset.ExpertCount];
        private GUIStyle labelStyle;
        private GUIStyle titleStyle;
        private Texture2D whiteTexture;
        private Font font;

        public bool ShowUIElements => showUIElements;

        private void Awake()
        {
            whiteTexture = Texture2D.whiteTexture;
            font = Resources.Load<Font>("Fonts/Coolvetica");
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 14,
                normal = { textColor = Color.white }
            };
            titleStyle = new GUIStyle(labelStyle)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };
        }

        private void OnGUI()
        {
            if (!showUIElements || controller == null || !controller.IsInitialized)
            {
                return;
            }

            EnsureStyles();

            BasketballAgentState state = controller.State;
            int pivot = BasketballAgentState.Pivot;
            DrawPanel(new Rect(18f, 18f, 250f, 255f), new Color(0f, 0f, 0f, 0.68f));
            GUI.Label(new Rect(32f, 27f, 220f, 24f), "AI4Animation Basketball", titleStyle);
            GUI.Label(new Rect(32f, 51f, 220f, 20f),
                $"Tick {state.TickCount}   Ball {controller.BallState}", labelStyle);

            for (int channel = 0; channel < BasketballAgentState.StyleCount; channel++)
            {
                float value = state.Styles[BasketballAgentState.StyleIndex(pivot, channel)];
                DrawBar(32f, 78f + 22f * channel, 205f, StyleNames[channel], value,
                    new Color(1f, 0.65f, 0f, 1f));
            }
            for (int channel = 0; channel < BasketballAgentState.ContactCount; channel++)
            {
                float value = state.Contacts[BasketballAgentState.ContactIndex(pivot, channel)];
                DrawBar(32f, 194f + 11f * channel, 205f, ContactNames[channel], value,
                    new Color(0.1f, 0.75f, 1f, 1f), 9f);
            }

            controller.CopyGatingWeights(gating);
            float expertWidth = Mathf.Min(54f, (Screen.width - 40f) / 8f);
            float baseY = Screen.height - 42f;
            for (int expert = 0; expert < gating.Length; expert++)
            {
                float height = 90f * Mathf.Clamp01(gating[expert]);
                Rect bar = new(20f + expert * expertWidth, baseY - height, expertWidth - 5f, height);
                DrawPanel(bar, Color.Lerp(new Color(0.2f, 0.3f, 0.9f),
                    new Color(1f, 0.65f, 0f), expert / 7f));
                GUI.Label(new Rect(bar.x, baseY, expertWidth, 18f), expert.ToString(), labelStyle);
            }
        }

        private void DrawBar(float x, float y, float width, string name, float value, Color color, float height = 16f)
        {
            GUI.Label(new Rect(x, y, 72f, height + 4f), name, labelStyle);
            DrawPanel(new Rect(x + 72f, y + 2f, width - 72f, height - 4f),
                new Color(0.18f, 0.18f, 0.18f, 0.9f));
            DrawPanel(new Rect(x + 72f, y + 2f,
                (width - 72f) * Mathf.Clamp01(value), height - 4f), color);
        }

        private void DrawPanel(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = previous;
        }

        public void SetVisible(bool value)
        {
            showUIElements = value;
        }

#if UNITY_EDITOR
        public void Configure(BasketballNeuralController value)
        {
            controller = value;
            showUIElements = true;
        }
#endif
    }
}
