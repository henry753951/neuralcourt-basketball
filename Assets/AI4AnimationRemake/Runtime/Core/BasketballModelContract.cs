namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Immutable input/output dimensions of the deployed Basketball GPU model.
    /// Runtime model weights live only in the imported Sentis ModelAsset.
    /// </summary>
    public static class BasketballModelContract
    {
        public const int InputFeatureCount = 864;
        public const int MainFeatureCount = 734;
        public const int GatingFeatureCount = 130;
        public const int OutputFeatureCount = 588;
        public const int ExpertCount = 8;
        public const int PackedOutputFeatureCount = OutputFeatureCount + ExpertCount;
    }
}
