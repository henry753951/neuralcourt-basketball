using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballReferenceRig : MonoBehaviour
    {
        [SerializeField]
        private BasketballModelAsset model;

        [SerializeField]
        private BasketballSkeleton skeleton;

        [SerializeField]
        private BasketballBallController ball;

        [SerializeField, Min(1)]
        private int neuralTickRate = 30;

        [SerializeField]
        private bool renderInterpolation = true;

        [SerializeField]
        private bool enableContactIK = true;

        [SerializeField]
        private bool enableDebugDraw;

        [SerializeField]
        private bool deterministicMode = true;

        public BasketballModelAsset Model => model;
        public BasketballSkeleton Skeleton => skeleton;
        public BasketballBallController Ball => ball;
        public int NeuralTickRate => neuralTickRate;
        public bool RenderInterpolation => renderInterpolation;
        public bool EnableContactIK => enableContactIK;
        public bool EnableDebugDraw => enableDebugDraw;
        public bool DeterministicMode => deterministicMode;

        public bool Validate(out string reason)
        {
            if (model == null)
            {
                reason = "Basketball model is not assigned.";
                return false;
            }

            if (!model.Validate(out reason))
            {
                reason = $"Model: {reason}";
                return false;
            }

            if (skeleton == null)
            {
                reason = "Basketball skeleton is not assigned.";
                return false;
            }

            if (!skeleton.Validate(out reason))
            {
                reason = $"Skeleton: {reason}";
                return false;
            }

            if (ball == null)
            {
                reason = "Basketball ball controller is not assigned.";
                return false;
            }

            if (neuralTickRate != 30)
            {
                reason = $"Reference mode requires 30 Hz but is set to {neuralTickRate} Hz.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballModelAsset modelAsset,
            BasketballSkeleton skeletonComponent,
            BasketballBallController ballComponent)
        {
            model = modelAsset;
            skeleton = skeletonComponent;
            ball = ballComponent;
            neuralTickRate = 30;
            renderInterpolation = true;
            enableContactIK = true;
            enableDebugDraw = false;
            deterministicMode = true;
        }
#endif
    }
}
