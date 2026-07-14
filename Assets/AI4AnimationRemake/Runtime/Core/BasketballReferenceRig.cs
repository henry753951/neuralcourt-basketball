using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballReferenceRig : MonoBehaviour
    {
        [SerializeField]
        private BasketballSkeleton skeleton;

        [SerializeField]
        private BasketballBallController ball;

        private BasketballRuntimeSettings runtimeSettings;

        public BasketballSkeleton Skeleton => skeleton;
        public BasketballBallController Ball => ball;
        public BasketballRuntimeSettings RuntimeSettings => runtimeSettings != null
            ? runtimeSettings
            : BasketballRuntimeSettings.LoadDefault();
        public int NeuralTickRate => RuntimeSettings != null
            ? RuntimeSettings.NeuralTickRate
            : BasketballRuntimeSettings.CanonicalNeuralTickRate;
        public bool RenderInterpolation => RuntimeSettings == null ||
                                           RuntimeSettings.RenderInterpolation;
        public int MaximumCatchUpTicks => RuntimeSettings != null
            ? RuntimeSettings.MaximumCatchUpTicks
            : 4;
        public bool EnableContactIK => RuntimeSettings == null ||
                                       RuntimeSettings.EnableContactIK;
        public bool EnableDebugDraw => RuntimeSettings != null &&
                                       RuntimeSettings.EnableDebugDraw;
        public bool DeterministicMode => RuntimeSettings == null ||
                                         RuntimeSettings.DeterministicMode;
        public bool Validate(out string reason)
        {
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

            if (RuntimeSettings == null)
            {
                reason = $"Basketball runtime settings are missing from Resources/" +
                         $"{BasketballRuntimeSettings.DefaultResourcePath}.asset.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballSkeleton skeletonComponent,
            BasketballBallController ballComponent)
        {
            skeleton = skeletonComponent;
            ball = ballComponent;
        }
#endif

        internal void SetRuntimeSettings(BasketballRuntimeSettings value) =>
            runtimeSettings = value;
    }
}
