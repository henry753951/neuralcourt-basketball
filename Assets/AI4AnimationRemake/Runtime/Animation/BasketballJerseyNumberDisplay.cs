using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Displays a skinned front/back jersey number without cloning materials.
    /// Single-digit numbers use dedicated centered, slightly smaller patches.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BasketballJerseyNumberDisplay : MonoBehaviour
    {
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer frontTens;
        [SerializeField] private Renderer frontOnes;
        [SerializeField] private Renderer frontSingle;
        [SerializeField] private Renderer backTens;
        [SerializeField] private Renderer backOnes;
        [SerializeField] private Renderer backSingle;
        [SerializeField, Range(0, 99)] private int number = 1;
        [SerializeField] private Color numberColor = Color.white;

        private readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[6];

        public int Number => number;

        private void OnEnable()
        {
            ApplyNumber(number, numberColor);
        }

        private void OnValidate()
        {
            ApplyNumber(number, numberColor);
        }

        public void ApplyNumber(int jerseyNumber, Color color)
        {
            number = Mathf.Clamp(jerseyNumber, 0, 99);
            numberColor = color;
            bool singleDigit = number < 10;

            ApplyDigit(frontTens, number / 10, !singleDigit, 0);
            ApplyDigit(frontOnes, number % 10, !singleDigit, 1);
            ApplyDigit(frontSingle, number, singleDigit, 2);
            ApplyDigit(backTens, number / 10, !singleDigit, 3);
            ApplyDigit(backOnes, number % 10, !singleDigit, 4);
            ApplyDigit(backSingle, number, singleDigit, 5);
        }

        private void ApplyDigit(Renderer target, int digit, bool visible, int blockIndex)
        {
            if (target == null)
            {
                return;
            }

            target.enabled = visible;
            if (!visible)
            {
                return;
            }

            blocks[blockIndex] ??= new MaterialPropertyBlock();
            MaterialPropertyBlock block = blocks[blockIndex];
            target.GetPropertyBlock(block);
            block.SetVector(BaseMapStId, new Vector4(0.1f, 1f, digit * 0.1f, 0f));
            block.SetColor(BaseColorId, numberColor);
            block.SetColor(ColorId, numberColor);
            target.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void Configure(
            Renderer newFrontTens,
            Renderer newFrontOnes,
            Renderer newFrontSingle,
            Renderer newBackTens,
            Renderer newBackOnes,
            Renderer newBackSingle)
        {
            frontTens = newFrontTens;
            frontOnes = newFrontOnes;
            frontSingle = newFrontSingle;
            backTens = newBackTens;
            backOnes = newBackOnes;
            backSingle = newBackSingle;
            ApplyNumber(number, numberColor);
        }
#endif
    }
}
