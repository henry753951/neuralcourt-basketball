using UnityEngine;
using UnityEngine.UI;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballDemoUI : MonoBehaviour
    {
        private static readonly Color InactiveColor = new(150f / 255f, 150f / 255f, 150f / 255f);
        private static readonly Color ActiveColor = new(250f / 255f, 180f / 255f, 0f);
        private static readonly Color InactiveTextColor = new(0.8f, 0.8f, 0.8f);

        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private BasketballDebugHUD hud;
        [SerializeField] private BasketballDebugVisualizer visualizer;

        private Button uiElements;
        private Button debugLines;
        private Button useKeyboard;
        private Button useGamepad;
        private GameObject keyboardInfo;
        private GameObject gamepadInfo;

        private void Start()
        {
            uiElements = FindButton("UIElements");
            debugLines = FindButton("DebugLines");
            useKeyboard = FindButton("UseKeyboard");
            useGamepad = FindButton("UseGamepad");
            keyboardInfo = transform.Find("Info/Keyboard")?.gameObject;
            gamepadInfo = transform.Find("Info/Gamepad")?.gameObject;
            SetText("Info/Keyboard/Move", "WASD move + face camera | Q/E extra turn | Mouse orbit");
            SetText("Info/Keyboard/Actions", "Space shoot | Ctrl+LMB pass | LMB steal | R recenter");
            SetText("Info/Keyboard/Ball", "Hold RMB + move Mouse to control the ball.");
            SetText("Info/Esc", "ESC toggles cursor lock.");

            Bind(uiElements, () => SetUIVisible(!hud.ShowUIElements));
            Bind(debugLines, () => SetDebugVisible(!visualizer.ShowDebugLines));
            Bind(useKeyboard, () => SetInputMode(BasketballKeyboardMouseInputProvider.InputMode.Keyboard));
            Bind(useGamepad, () => SetInputMode(BasketballKeyboardMouseInputProvider.InputMode.Gamepad));

            SetUIVisible(true);
            SetDebugVisible(true);
            SetInputMode(inputProvider.Mode);
        }

        private Button FindButton(string path) => transform.Find(path)?.GetComponent<Button>();

        private void SetText(string path, string value)
        {
            Text label = transform.Find(path)?.GetComponent<Text>();
            if (label != null)
            {
                label.text = value;
            }
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction callback)
        {
            if (button != null)
            {
                button.onClick.AddListener(callback);
            }
        }

        private void SetUIVisible(bool value)
        {
            hud.SetVisible(value);
            SetButtonState(uiElements, value);
        }

        private void SetDebugVisible(bool value)
        {
            visualizer.SetVisible(value);
            SetButtonState(debugLines, value);
        }

        private void SetInputMode(BasketballKeyboardMouseInputProvider.InputMode mode)
        {
            inputProvider.SetMode(mode);
            bool keyboard = mode == BasketballKeyboardMouseInputProvider.InputMode.Keyboard;
            SetButtonState(useKeyboard, keyboard);
            SetButtonState(useGamepad, !keyboard);
            if (keyboardInfo != null)
            {
                keyboardInfo.SetActive(keyboard);
            }
            if (gamepadInfo != null)
            {
                gamepadInfo.SetActive(!keyboard);
            }
        }

        private static void SetButtonState(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = active ? ActiveColor : InactiveColor;
            }
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.color = active ? Color.white : InactiveTextColor;
            }
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballKeyboardMouseInputProvider provider,
            BasketballDebugHUD debugHUD,
            BasketballDebugVisualizer debugVisualizer)
        {
            inputProvider = provider;
            hud = debugHUD;
            visualizer = debugVisualizer;
        }
#endif
    }
}
