using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballSkillCommandType
    {
        None,
        RequestPass
    }

    public enum BasketballSkillCommandResult
    {
        None,
        Accepted,
        RejectedExpired,
        RejectedActor,
        RejectedStalePossession,
        RejectedTarget,
        RejectedState
    }

    public enum BasketballTeamRole
    {
        Inactive,
        BallHandler,
        IntendedReceiver,
        Spacer,
        Cutter,
        TransitionSafety,
        OnBallDefender,
        OffBallDefender,
        HelpDefender,
        PassInterceptor,
        LooseBallChaser,
        Rebounder,
        BoxOutDefender,
        Transition
    }

    /// <summary>
    /// One high-level decision. Intent drives the existing neural controller while
    /// Skill requests are validated and executed by the match/possession layer.
    /// Policies never write possession, ball physics, or transforms directly.
    /// </summary>
    public readonly struct BasketballPlayerCommand
    {
        public BasketballPlayerCommand(
            int commandId,
            int actorPlayerIndex,
            int possessionVersion,
            in BasketballIntent intent,
            BasketballSkillCommandType skill = BasketballSkillCommandType.None,
            int targetPlayerIndex = -1,
            BasketballPassType passType = BasketballPassType.Lead,
            float expiresAt = float.PositiveInfinity)
        {
            CommandId = commandId;
            ActorPlayerIndex = actorPlayerIndex;
            PossessionVersion = possessionVersion;
            Intent = intent;
            Skill = skill;
            TargetPlayerIndex = targetPlayerIndex;
            PassType = passType;
            ExpiresAt = expiresAt;
        }

        public int CommandId { get; }
        public int ActorPlayerIndex { get; }
        public int PossessionVersion { get; }
        public BasketballIntent Intent { get; }
        public BasketballSkillCommandType Skill { get; }
        public int TargetPlayerIndex { get; }
        public BasketballPassType PassType { get; }
        public float ExpiresAt { get; }

        public bool HasSkillRequest => Skill != BasketballSkillCommandType.None;

        public static BasketballPlayerCommand IntentOnly(
            int actorPlayerIndex,
            int possessionVersion,
            in BasketballIntent intent)
            => new(0, actorPlayerIndex, possessionVersion, intent);

        public static BasketballPlayerCommand RequestPass(
            int commandId,
            int actorPlayerIndex,
            int targetPlayerIndex,
            int possessionVersion,
            BasketballPassType passType = BasketballPassType.Lead,
            float expiresAt = float.PositiveInfinity)
            => new(
                commandId,
                actorPlayerIndex,
                possessionVersion,
                default,
                BasketballSkillCommandType.RequestPass,
                targetPlayerIndex,
                passType,
                expiresAt);
    }

    /// <summary>
    /// Replaceable high-level decision boundary shared by the built-in rule AI and
    /// future external policies. Inputs are data snapshots; outputs are commands.
    /// </summary>
    public interface IBasketballDecisionPolicy
    {
        void BeginDecisionFrame(BasketballWorldObservation observation);

        BasketballPlayerCommand Decide(in BasketballPlayerObservation player);

        void ReportCommandResult(
            in BasketballPlayerCommand command,
            BasketballSkillCommandResult result);
    }

    /// <summary>
    /// Optional extension for policies that need delayed world outcomes such as
    /// pass catches, interceptions, steals, or made/missed shots. Events are
    /// delivered once in sequence order before the next decision frame.
    /// </summary>
    public interface IBasketballWorldEventObserver
    {
        void ObserveEvent(in BasketballWorldEvent worldEvent);
    }
}
