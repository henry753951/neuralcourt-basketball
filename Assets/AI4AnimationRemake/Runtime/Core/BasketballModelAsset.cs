using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [CreateAssetMenu(menuName = "AI4Animation/Basketball Model", fileName = "BasketballModel")]
    public sealed class BasketballModelAsset : ScriptableObject
    {
        public const int InputFeatureCount = 864;
        public const int MainFeatureCount = 734;
        public const int GatingFeatureCount = 130;
        public const int OutputFeatureCount = 588;
        public const int ExpertCount = 8;
        public const int BufferCount = 58;

        [SerializeField]
        private global::Parameters source;

        public global::Parameters Source => source;

        public bool TryGetBuffer(string id, out float[] values)
        {
            values = null;
            if (source == null || source.Buffers == null)
            {
                return false;
            }

            for (int i = 0; i < source.Buffers.Length; i++)
            {
                global::Parameters.Buffer buffer = source.Buffers[i];
                if (buffer != null && buffer.ID == id)
                {
                    values = buffer.Values;
                    return values != null;
                }
            }

            return false;
        }

        public bool Validate(out string reason)
        {
            if (source == null)
            {
                reason = "Legacy Parameters source is not assigned.";
                return false;
            }

            if (source.Buffers == null || source.Buffers.Length != BufferCount)
            {
                int count = source.Buffers == null ? 0 : source.Buffers.Length;
                reason = $"Expected {BufferCount} buffers but found {count}.";
                return false;
            }

            if (!ValidateBuffer("Xmean", InputFeatureCount, out reason) ||
                !ValidateBuffer("Xstd", InputFeatureCount, out reason) ||
                !ValidateBuffer("Ymean", OutputFeatureCount, out reason) ||
                !ValidateBuffer("Ystd", OutputFeatureCount, out reason) ||
                !ValidateBuffer("wc000_w", 128 * GatingFeatureCount, out reason) ||
                !ValidateBuffer("wc000_b", 128, out reason) ||
                !ValidateBuffer("wc010_w", 128 * 128, out reason) ||
                !ValidateBuffer("wc010_b", 128, out reason) ||
                !ValidateBuffer("wc020_w", ExpertCount * 128, out reason) ||
                !ValidateBuffer("wc020_b", ExpertCount, out reason))
            {
                return false;
            }

            for (int expert = 0; expert < ExpertCount; expert++)
            {
                if (!ValidateBuffer($"wc10{expert}_w", 512 * MainFeatureCount, out reason) ||
                    !ValidateBuffer($"wc10{expert}_b", 512, out reason) ||
                    !ValidateBuffer($"wc11{expert}_w", 512 * 512, out reason) ||
                    !ValidateBuffer($"wc11{expert}_b", 512, out reason) ||
                    !ValidateBuffer($"wc12{expert}_w", OutputFeatureCount * 512, out reason) ||
                    !ValidateBuffer($"wc12{expert}_b", OutputFeatureCount, out reason))
                {
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private bool ValidateBuffer(string id, int expectedLength, out string reason)
        {
            if (!TryGetBuffer(id, out float[] values))
            {
                reason = $"Required buffer '{id}' is missing.";
                return false;
            }

            if (values.Length != expectedLength)
            {
                reason = $"Buffer '{id}' expected {expectedLength} floats but found {values.Length}.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public void SetSource(global::Parameters value)
        {
            source = value;
        }
#endif
    }
}
