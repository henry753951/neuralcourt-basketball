namespace CrowdEyes.AI4Animation.Basketball
{
    public interface IBasketballPossessionAuthority
    {
        bool HasBall(BasketballNeuralController player);
        bool CanWriteBall(BasketballNeuralController player);
        void ReportNeuralTick(
            BasketballNeuralController player,
            in BasketballBallObservation observation);
    }
}
