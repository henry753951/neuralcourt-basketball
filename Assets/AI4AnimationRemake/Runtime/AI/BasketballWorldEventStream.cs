using System;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballWorldEventType
    {
        None,
        MatchInitialized,
        MatchConfigurationChanged,
        MatchRestarted,
        CommandRejected,
        PossessionChanged,
        PassRequested,
        PassReleased,
        PassPreparationFailed,
        PassFailed,
        PassCaught,
        PassIntercepted,
        ShotReleased,
        ShotMade,
        ShotMissed,
        StealTouch,
        BallContested,
        BallLoose,
        BallSecured,
        BallOutOfBounds,
        ShotClockViolation,
        BackcourtViolation,
        ThreeSecondViolation,
        TravelViolation,
        DoubleDribbleViolation
    }

    [Serializable]
    public readonly struct BasketballWorldEvent
    {
        public BasketballWorldEvent(
            int sequence,
            float timeSeconds,
            BasketballWorldEventType type,
            int possessionVersion,
            int actorPlayerIndex,
            int targetPlayerIndex,
            int teamId,
            Vector3 position,
            Vector3 velocity,
            float value,
            int points,
            Vector3 targetPosition,
            int skillVariant)
        {
            Sequence = sequence;
            TimeSeconds = timeSeconds;
            Type = type;
            PossessionVersion = possessionVersion;
            ActorPlayerIndex = actorPlayerIndex;
            TargetPlayerIndex = targetPlayerIndex;
            TeamId = teamId;
            Position = position;
            Velocity = velocity;
            Value = value;
            Points = points;
            TargetPosition = targetPosition;
            SkillVariant = skillVariant;
        }

        public int Sequence { get; }
        public float TimeSeconds { get; }
        public BasketballWorldEventType Type { get; }
        public int PossessionVersion { get; }
        public int ActorPlayerIndex { get; }
        public int TargetPlayerIndex { get; }
        public int TeamId { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Value { get; }
        public int Points { get; }
        public Vector3 TargetPosition { get; }
        public int SkillVariant { get; }
    }

    /// <summary>
    /// Fixed-size event history for debugging, skill-result capture, and future
    /// external simulation bridges. Publishing overwrites the oldest entry and
    /// does not allocate after initialization.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasketballWorldEventStream : MonoBehaviour
    {
        [SerializeField, Range(32, 2048)] private int capacity = 256;

        private BasketballWorldEvent[] events;
        private int nextWriteIndex;
        private int count;
        private int sequence;

        public int Count => count;
        public int Capacity => events != null ? events.Length : Mathf.Max(32, capacity);
        public int LatestSequence => sequence;
        public int OldestSequence => count > 0 ? sequence - count + 1 : sequence + 1;
        public event Action<BasketballWorldEvent> Published;

        private void Awake()
        {
            EnsureBuffer();
        }

        public BasketballWorldEvent Publish(
            BasketballWorldEventType type,
            int possessionVersion,
            BasketballTeamMember actor = null,
            BasketballTeamMember target = null,
            Vector3 position = default,
            Vector3 velocity = default,
            float value = 0f,
            int points = 0,
            Vector3 targetPosition = default,
            int skillVariant = 0)
        {
            EnsureBuffer();
            BasketballWorldEvent worldEvent = new(
                ++sequence,
                Time.time,
                type,
                possessionVersion,
                actor != null ? actor.PlayerIndex : -1,
                target != null ? target.PlayerIndex : -1,
                actor != null ? actor.TeamId : -1,
                position,
                velocity,
                value,
                points,
                targetPosition,
                skillVariant);
            events[nextWriteIndex] = worldEvent;
            nextWriteIndex = (nextWriteIndex + 1) % events.Length;
            count = Mathf.Min(count + 1, events.Length);
            Published?.Invoke(worldEvent);
            return worldEvent;
        }

        public BasketballWorldEvent GetNewest(int newestOffset = 0)
        {
            if (events == null || newestOffset < 0 || newestOffset >= count)
            {
                return default;
            }
            int index = nextWriteIndex - 1 - newestOffset;
            while (index < 0)
            {
                index += events.Length;
            }
            return events[index];
        }

        public bool TryGetBySequence(
            int requestedSequence,
            out BasketballWorldEvent worldEvent)
        {
            if (events == null || count == 0 ||
                requestedSequence < OldestSequence ||
                requestedSequence > LatestSequence)
            {
                worldEvent = default;
                return false;
            }

            int newestOffset = LatestSequence - requestedSequence;
            worldEvent = GetNewest(newestOffset);
            return worldEvent.Sequence == requestedSequence;
        }

        public void Clear()
        {
            EnsureBuffer();
            Array.Clear(events, 0, events.Length);
            nextWriteIndex = 0;
            count = 0;
        }

        private void EnsureBuffer()
        {
            int desired = Mathf.Max(32, capacity);
            if (events != null && events.Length == desired)
            {
                return;
            }
            events = new BasketballWorldEvent[desired];
            nextWriteIndex = 0;
            count = 0;
        }

#if UNITY_EDITOR
        public void Configure(int eventCapacity = 256)
        {
            capacity = Mathf.Clamp(eventCapacity, 32, 2048);
        }
#endif
    }
}
