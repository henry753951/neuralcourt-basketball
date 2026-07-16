using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Optional JSONL sink for the bounded world-event stream. It is disabled by
    /// default and never participates in control, inference, physics, or rewards.
    /// The output is suitable for offline skill-surrogate and RL dataset tooling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasketballSkillTelemetryRecorder : MonoBehaviour
    {
        private const string SchemaName = "basketball-simulation-v3";

        [SerializeField] private BasketballWorldEventStream eventStream;
        [SerializeField] private BasketballRewardTracker rewardTracker;
        [SerializeField] private bool recordOnStart;
        [SerializeField] private bool recordObservationSteps = true;
        [SerializeField, Range(1f, 60f)] private float observationRate = 10f;
        [SerializeField] private string filePrefix = "basketball-skills";
        [SerializeField, Range(1, 256)] private int flushEveryEvents = 16;

        private readonly StringBuilder lineBuilder = new(4096);
        private StreamWriter writer;
        private int unflushedEventCount;
        private float nextObservationAt;

        public bool IsRecording => writer != null;
        public string OutputPath { get; private set; }
        public int RecordedEventCount { get; private set; }
        public int RecordedRewardCount { get; private set; }
        public int RecordedStepCount { get; private set; }

        private void Awake()
        {
            BasketballWorldEventStream resolved = eventStream != null
                ? eventStream
                : GetComponent<BasketballWorldEventStream>();
            eventStream = null;
            SetEventStream(resolved);
            BasketballRewardTracker resolvedRewards = rewardTracker != null
                ? rewardTracker
                : GetComponent<BasketballRewardTracker>();
            rewardTracker = null;
            SetRewardTracker(resolvedRewards);
        }

        private void Start()
        {
            if (recordOnStart)
            {
                StartRecording();
            }
        }

        private void OnDisable()
        {
            StopRecording();
        }

        private void OnDestroy()
        {
            SetEventStream(null);
            SetRewardTracker(null);
            StopRecording();
        }

        public void SetEventStream(BasketballWorldEventStream value)
        {
            if (eventStream == value)
            {
                return;
            }
            if (eventStream != null)
            {
                eventStream.Published -= HandlePublished;
            }
            eventStream = value;
            if (eventStream != null)
            {
                eventStream.Published += HandlePublished;
            }
        }

        public void SetRewardTracker(BasketballRewardTracker value)
        {
            if (rewardTracker == value)
            {
                return;
            }
            if (rewardTracker != null)
            {
                rewardTracker.Published -= HandleRewardPublished;
            }
            rewardTracker = value;
            if (rewardTracker != null)
            {
                rewardTracker.Published += HandleRewardPublished;
            }
        }

        public bool StartRecording()
        {
            if (writer != null)
            {
                return true;
            }
            try
            {
                string directory = Path.Combine(
                    Application.persistentDataPath,
                    "BasketballTelemetry");
                Directory.CreateDirectory(directory);
                string safePrefix = string.IsNullOrWhiteSpace(filePrefix)
                    ? "basketball-skills"
                    : filePrefix.Trim();
                OutputPath = Path.Combine(
                    directory,
                    $"{safePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.jsonl");
                writer = new StreamWriter(
                    OutputPath,
                    append: false,
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 65536);
                writer.WriteLine(
                    $"{{\"schema\":\"{SchemaName}\",\"unityVersion\":" +
                    $"\"{Application.unityVersion}\",\"records\":" +
                    "[\"event\",\"reward\",\"step\"]}");
                RecordedEventCount = 0;
                RecordedRewardCount = 0;
                RecordedStepCount = 0;
                unflushedEventCount = 0;
                nextObservationAt = Time.time;
                return true;
            }
            catch (Exception exception)
            {
                writer?.Dispose();
                writer = null;
                Debug.LogError(
                    $"Could not start basketball telemetry recording: " +
                    exception.Message,
                    this);
                return false;
            }
        }

        public void StopRecording()
        {
            if (writer == null)
            {
                return;
            }
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Basketball telemetry writer could not close cleanly: " +
                    exception.Message,
                    this);
            }
            finally
            {
                writer = null;
                unflushedEventCount = 0;
            }
        }

        private void HandlePublished(BasketballWorldEvent worldEvent)
        {
            if (writer == null)
            {
                return;
            }
            try
            {
                BuildEventJsonLine(worldEvent);
                WriteCurrentLine();
                RecordedEventCount++;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Basketball telemetry recording stopped after an I/O error: " +
                    exception.Message,
                    this);
                StopRecording();
            }
        }

        private void HandleRewardPublished(BasketballRewardSignal signal)
        {
            if (writer == null)
            {
                return;
            }
            try
            {
                BuildRewardJsonLine(signal);
                WriteCurrentLine();
                RecordedRewardCount++;
            }
            catch (Exception exception)
            {
                StopAfterWriteError(exception);
            }
        }

        public void CaptureObservation(BasketballWorldObservation observation)
        {
            if (writer == null || !recordObservationSteps || observation == null ||
                Time.time < nextObservationAt)
            {
                return;
            }
            nextObservationAt = Time.time + 1f / Mathf.Max(1f, observationRate);
            try
            {
                BuildObservationJsonLine(observation);
                WriteCurrentLine();
                RecordedStepCount++;
            }
            catch (Exception exception)
            {
                StopAfterWriteError(exception);
            }
        }

        private void BuildEventJsonLine(BasketballWorldEvent value)
        {
            lineBuilder.Clear();
            lineBuilder.Append('{');
            AppendString("record", "event");
            AppendNumber("sequence", value.Sequence);
            AppendNumber("time", value.TimeSeconds);
            AppendString("type", value.Type.ToString());
            AppendNumber("possessionVersion", value.PossessionVersion);
            AppendNumber("actor", value.ActorPlayerIndex);
            AppendNumber("target", value.TargetPlayerIndex);
            AppendNumber("team", value.TeamId);
            AppendVector("position", value.Position);
            AppendVector("velocity", value.Velocity);
            AppendVector("targetPosition", value.TargetPosition);
            AppendNumber("skillVariant", value.SkillVariant);
            AppendNumber("value", value.Value);
            AppendNumber("points", value.Points, trailingComma: false);
            lineBuilder.Append('}');
        }

        private void BuildRewardJsonLine(BasketballRewardSignal value)
        {
            lineBuilder.Clear();
            lineBuilder.Append('{');
            AppendString("record", "reward");
            AppendNumber("sequence", value.Sequence);
            AppendNumber("episode", value.EpisodeId);
            AppendNumber("worldEventSequence", value.WorldEventSequence);
            AppendString("source", value.SourceType.ToString());
            AppendNumber("actor", value.ActorPlayerIndex);
            AppendNumber("target", value.TargetPlayerIndex);
            AppendNumber("team", value.TeamId);
            AppendNumber("teamZeroDelta", value.TeamZeroDelta);
            AppendNumber("teamOneDelta", value.TeamOneDelta);
            AppendNumber("actorDelta", value.ActorDelta);
            AppendNumber("targetDelta", value.TargetDelta);
            AppendBoolean("endsPossession", value.EndsPossession, trailingComma: false);
            lineBuilder.Append('}');
        }

        private void BuildObservationJsonLine(BasketballWorldObservation value)
        {
            lineBuilder.Clear();
            lineBuilder.Append('{');
            AppendString("record", "step");
            AppendNumber("time", value.TimeSeconds);
            AppendString("matchMode", value.MatchMode.ToString());
            AppendString("ballState", value.BallState.ToString());
            AppendString("ballControlMode", value.BallControlMode.ToString());
            AppendNumber("possessionVersion", value.PossessionVersion);
            AppendNumber("owner", value.OwnerPlayerIndex);
            AppendNumber("previousOwner", value.PreviousOwnerPlayerIndex);
            AppendNumber("intendedReceiver", value.IntendedReceiverPlayerIndex);
            AppendNumber("activePlayers", value.ActivePlayerCount);
            AppendNumber("activeTeamZero", value.ActiveTeamZeroCount);
            AppendNumber("activeTeamOne", value.ActiveTeamOneCount);
            AppendVector("ballPosition", value.BallPosition);
            AppendVector("ballVelocity", value.BallVelocity);
            AppendVector("teamZeroAttackTarget", value.TeamZeroAttackTarget);
            AppendVector("teamOneAttackTarget", value.TeamOneAttackTarget);
            AppendNumber("teamZeroScore", value.TeamZeroScore);
            AppendNumber("teamOneScore", value.TeamOneScore);
            AppendNumber("rewardEpisode", rewardTracker != null
                ? rewardTracker.EpisodeId
                : 0);
            AppendNumber("latestRewardSequence", rewardTracker != null
                ? rewardTracker.LatestSequence
                : 0);
            AppendNumber("teamZeroReturn", rewardTracker != null
                ? rewardTracker.TeamZeroReturn
                : 0f);
            AppendNumber("teamOneReturn", rewardTracker != null
                ? rewardTracker.TeamOneReturn
                : 0f);
            lineBuilder.Append("\"players\":[");
            for (int index = 0; index < value.PlayerCapacity; index++)
            {
                if (index > 0)
                {
                    lineBuilder.Append(',');
                }
                BasketballPlayerObservation player = value.GetPlayer(index);
                lineBuilder.Append('{');
                AppendNumber("slot", player.RosterIndex);
                AppendNumber("player", player.PlayerIndex);
                AppendNumber("team", player.TeamId);
                AppendBoolean("active", player.Active);
                AppendVector("position", player.Position);
                AppendVector("velocity", player.Velocity);
                AppendVector("forward", player.Forward);
                AppendBoolean("hasBall", player.HasBall);
                AppendBoolean("intendedReceiver", player.IsIntendedReceiver);
                AppendNumber("actionMask", (int)player.ActionMask, trailingComma: false);
                lineBuilder.Append('}');
            }
            lineBuilder.Append("]}");
        }

        private void AppendString(string name, string value)
        {
            lineBuilder.Append('"').Append(name).Append("\":\"")
                .Append(value).Append("\",");
        }

        private void AppendNumber(string name, int value, bool trailingComma = true)
        {
            lineBuilder.Append('"').Append(name).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));
            if (trailingComma)
            {
                lineBuilder.Append(',');
            }
        }

        private void AppendNumber(string name, float value, bool trailingComma = true)
        {
            lineBuilder.Append('"').Append(name).Append("\":")
                .Append(value.ToString("R", CultureInfo.InvariantCulture));
            if (trailingComma)
            {
                lineBuilder.Append(',');
            }
        }

        private void AppendBoolean(string name, bool value, bool trailingComma = true)
        {
            lineBuilder.Append('\"').Append(name).Append("\":")
                .Append(value ? "true" : "false");
            if (trailingComma)
            {
                lineBuilder.Append(',');
            }
        }

        private void AppendVector(string name, Vector3 value)
        {
            lineBuilder.Append('"').Append(name).Append("\":[")
                .Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append("],");
        }

        private void WriteCurrentLine()
        {
            writer.WriteLine(lineBuilder.ToString());
            unflushedEventCount++;
            if (unflushedEventCount >= Mathf.Max(1, flushEveryEvents))
            {
                writer.Flush();
                unflushedEventCount = 0;
            }
        }

        private void StopAfterWriteError(Exception exception)
        {
            Debug.LogError(
                "Basketball telemetry recording stopped after an I/O error: " +
                exception.Message,
                this);
            StopRecording();
        }

#if UNITY_EDITOR
        public void Configure(
            BasketballWorldEventStream stream,
            bool beginOnStart = false,
            BasketballRewardTracker rewards = null)
        {
            eventStream = stream;
            recordOnStart = beginOnStart;
            rewardTracker = rewards;
        }
#endif
    }
}
