using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballKeyboardMouseInputProvider : MonoBehaviour
    {
        public enum InputMode
        {
            Keyboard,
            Gamepad
        }

        [SerializeField]
        private InputMode mode = InputMode.Keyboard;

        private Vector2 previousMove;
        private Vector2 keyboardBallControl;
        private float radialTurn;

        public InputMode Mode => mode;
        public bool IsBallControlMode { get; private set; }
        public Vector2 CurrentBallControl { get; private set; }

        public void SetMode(InputMode value)
        {
            mode = value;
            previousMove = Vector2.zero;
            keyboardBallControl = Vector2.zero;
            radialTurn = 0f;
            IsBallControlMode = false;
            CurrentBallControl = Vector2.zero;
        }

        public BasketballIntent ReadIntent()
        {
            BasketballIntent intent = mode == InputMode.Gamepad ? ReadGamepad() : ReadKeyboard();
            CurrentBallControl = intent.BallControl;
            return intent;
        }

        private BasketballIntent ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null)
            {
                return default;
            }

            Vector2 move = Vector2.zero;
            move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            move = Vector2.ClampMagnitude(move, 1f);

            IsBallControlMode = mouse != null && mouse.rightButton.isPressed;
            if (mouse != null && IsBallControlMode)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude == 0f)
                {
                    keyboardBallControl = Vector2.Lerp(
                        keyboardBallControl, Vector2.zero, 0.1f);
                }
                else
                {
                    float width = Mathf.Max(Screen.width, 1);
                    keyboardBallControl += 5f * delta / width;
                    keyboardBallControl = Vector2.ClampMagnitude(keyboardBallControl, 1f);
                }
            }
            else
            {
                keyboardBallControl = Vector2.Lerp(
                    keyboardBallControl, Vector2.zero, 0.1f);
            }

            float turnTarget = 0f;
            if (keyboard.qKey.isPressed)
            {
                turnTarget = -1f;
            }
            else if (keyboard.eKey.isPressed)
            {
                turnTarget = 1f;
            }
            radialTurn = Mathf.Lerp(radialTurn, turnTarget, 0.5f);
            previousMove = move;

            return new BasketballIntent
            {
                Move = move,
                BallControl = keyboardBallControl,
                Turn = radialTurn,
                Spin = 0f,
                BallHeight = 0f,
                Sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed,
                Hold = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed,
                Shoot = keyboard.spaceKey.isPressed,
                IsGamepad = false
            };
        }

        private BasketballIntent ReadGamepad()
        {
            IsBallControlMode = false;
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return new BasketballIntent { IsGamepad = true };
            }

            Vector2 move = Vector2.ClampMagnitude(gamepad.leftStick.ReadValue(), 1f);
            const float threshold = 0.9f;
            float length = BasketballMath.SmoothStep(
                Vector2.ClampMagnitude(move, 1f).magnitude, 2f, threshold);
            float ratio = Vector2.SignedAngle(previousMove, move) / 180f;
            radialTurn += length * ratio;
            radialTurn *= threshold;
            previousMove = move;

            return new BasketballIntent
            {
                Move = move,
                BallControl = Vector2.ClampMagnitude(gamepad.rightStick.ReadValue(), 1f),
                Turn = radialTurn,
                Spin = (gamepad.rightShoulder.isPressed ? 1f : 0f) -
                       (gamepad.leftShoulder.isPressed ? 1f : 0f),
                BallHeight = gamepad.dpad.ReadValue().y,
                Sprint = gamepad.leftStickButton.isPressed,
                Hold = gamepad.buttonEast.isPressed,
                Shoot = gamepad.buttonNorth.isPressed,
                IsGamepad = true
            };
        }
    }
}
