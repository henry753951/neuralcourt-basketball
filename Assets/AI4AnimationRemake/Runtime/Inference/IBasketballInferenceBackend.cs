using System;

namespace CrowdEyes.AI4Animation.Basketball
{
    public interface IBasketballInferenceBackend
    {
        int InputSize { get; }
        int OutputSize { get; }

        void Initialize(BasketballModelAsset model);

        void Evaluate(
            ReadOnlySpan<float> input,
            Span<float> output);
    }
}
