using System;
using Unity.Profiling;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    /// <summary>
    /// Allocation-free runtime sampling for the lightweight in-game performance HUD.
    /// The Unity Profiler remains the source of truth for detailed captures.
    /// </summary>
    internal sealed class BasketballPerformanceMonitor : IDisposable
    {
        private const int SampleCapacity = 30;
        private const double NanosecondsToMilliseconds = 1e-6;
        private const float TickRateWindow = 0.5f;

        private readonly FrameTiming[] frameTimings = new FrameTiming[1];

        private ProfilerRecorder mainThreadRecorder;
        private ProfilerRecorder gcAllocatedRecorder;
        private ProfilerRecorder controlRecorder;
        private ProfilerRecorder featureRecorder;
        private ProfilerRecorder inferenceRecorder;
        private ProfilerRecorder decodeRecorder;
        private ProfilerRecorder interpolateRecorder;
        private ProfilerRecorder applyPoseRecorder;
        private ProfilerRecorder ballRecorder;
        private ProfilerRecorder contactRecorder;
        private ProfilerRecorder ikRecorder;
        private ProfilerRecorder cameraRecorder;

        private float smoothedFrameSeconds;
        private float tickWindowSeconds;
        private int tickWindowStart = -1;
        private int frameTimingCaptureCountdown;
        private bool active;

        public float FramesPerSecond => smoothedFrameSeconds > 1e-6f
            ? 1f / smoothedFrameSeconds
            : 0f;
        public float FrameMilliseconds => smoothedFrameSeconds * 1000f;
        public float MainThreadMilliseconds { get; private set; } = -1f;
        public float GpuFrameMilliseconds { get; private set; } = -1f;
        public long GcAllocatedBytesPerFrame { get; private set; } = -1L;
        public float InferenceMilliseconds { get; private set; } = -1f;
        public float NeuralPipelineMilliseconds { get; private set; } = -1f;
        public float AnimationMilliseconds { get; private set; } = -1f;
        public float CameraMilliseconds { get; private set; } = -1f;
        public float NeuralTicksPerSecond { get; private set; }

        public void Start()
        {
            if (active)
            {
                return;
            }

            mainThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "Main Thread", SampleCapacity);
            gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame", SampleCapacity);
            controlRecorder = StartScriptRecorder("Basketball.Control");
            featureRecorder = StartScriptRecorder("Basketball.BuildFeatures");
            inferenceRecorder = StartScriptRecorder("Basketball.Inference");
            decodeRecorder = StartScriptRecorder("Basketball.Decode");
            interpolateRecorder = StartScriptRecorder("Basketball.Interpolate");
            applyPoseRecorder = StartScriptRecorder("Basketball.ApplyPose");
            ballRecorder = StartScriptRecorder("Basketball.Ball");
            contactRecorder = StartScriptRecorder("Basketball.Contact");
            ikRecorder = StartScriptRecorder("Basketball.IK");
            cameraRecorder = StartScriptRecorder("Basketball.Camera");
            frameTimingCaptureCountdown = 0;
            active = true;
        }

        public void Stop()
        {
            if (!active)
            {
                return;
            }

            DisposeRecorder(ref mainThreadRecorder);
            DisposeRecorder(ref gcAllocatedRecorder);
            DisposeRecorder(ref controlRecorder);
            DisposeRecorder(ref featureRecorder);
            DisposeRecorder(ref inferenceRecorder);
            DisposeRecorder(ref decodeRecorder);
            DisposeRecorder(ref interpolateRecorder);
            DisposeRecorder(ref applyPoseRecorder);
            DisposeRecorder(ref ballRecorder);
            DisposeRecorder(ref contactRecorder);
            DisposeRecorder(ref ikRecorder);
            DisposeRecorder(ref cameraRecorder);
            active = false;
        }

        public void Tick(
            float unscaledDeltaTime,
            int neuralTickCount,
            int frameTimingCaptureInterval)
        {
            if (!active || unscaledDeltaTime <= 0f)
            {
                return;
            }

            float frameSeconds = Mathf.Min(unscaledDeltaTime, 0.25f);
            if (smoothedFrameSeconds <= 0f)
            {
                smoothedFrameSeconds = frameSeconds;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-6f * frameSeconds);
                smoothedFrameSeconds = Mathf.Lerp(smoothedFrameSeconds, frameSeconds, blend);
            }

            if (frameTimingCaptureCountdown <= 0)
            {
                FrameTimingManager.CaptureFrameTimings();
                frameTimingCaptureCountdown = Mathf.Max(1, frameTimingCaptureInterval) - 1;
            }
            else
            {
                frameTimingCaptureCountdown--;
            }
            UpdateTickRate(frameSeconds, neuralTickCount);
        }

        public void Sample()
        {
            if (!active)
            {
                return;
            }

            MainThreadMilliseconds = AverageFrameMilliseconds(mainThreadRecorder);
            GcAllocatedBytesPerFrame = AverageBytes(gcAllocatedRecorder);
            InferenceMilliseconds = AverageInvocationMilliseconds(inferenceRecorder);

            long tickCount = SumCounts(controlRecorder);
            long neuralNanoseconds =
                SumValues(controlRecorder) +
                SumValues(featureRecorder) +
                SumValues(inferenceRecorder) +
                SumValues(decodeRecorder) +
                SumValues(ballRecorder) +
                SumValues(contactRecorder) +
                SumValues(ikRecorder);
            NeuralPipelineMilliseconds = tickCount > 0
                ? (float)(neuralNanoseconds * NanosecondsToMilliseconds / tickCount)
                : -1f;

            long animationCount = SumCounts(applyPoseRecorder);
            long animationNanoseconds =
                SumValues(interpolateRecorder) + SumValues(applyPoseRecorder);
            AnimationMilliseconds = animationCount > 0
                ? (float)(animationNanoseconds * NanosecondsToMilliseconds / animationCount)
                : -1f;
            CameraMilliseconds = AverageInvocationMilliseconds(cameraRecorder);

            uint timingCount = FrameTimingManager.GetLatestTimings(1, frameTimings);
            GpuFrameMilliseconds = timingCount > 0 && frameTimings[0].gpuFrameTime > 0.0
                ? (float)frameTimings[0].gpuFrameTime
                : -1f;
        }

        public void ResetTickRate(int neuralTickCount)
        {
            tickWindowSeconds = 0f;
            tickWindowStart = neuralTickCount;
            NeuralTicksPerSecond = 0f;
        }

        public void Dispose() => Stop();

        private void UpdateTickRate(float deltaTime, int neuralTickCount)
        {
            if (neuralTickCount < 0)
            {
                ResetTickRate(-1);
                return;
            }

            if (tickWindowStart < 0 || neuralTickCount < tickWindowStart)
            {
                ResetTickRate(neuralTickCount);
                return;
            }

            tickWindowSeconds += deltaTime;
            if (tickWindowSeconds < TickRateWindow)
            {
                return;
            }

            NeuralTicksPerSecond = (neuralTickCount - tickWindowStart) / tickWindowSeconds;
            tickWindowStart = neuralTickCount;
            tickWindowSeconds = 0f;
        }

        private static ProfilerRecorder StartScriptRecorder(string markerName) =>
            ProfilerRecorder.StartNew(ProfilerCategory.Scripts, markerName, SampleCapacity);

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid)
            {
                recorder.Dispose();
            }
            recorder = default;
        }

        private static float AverageInvocationMilliseconds(ProfilerRecorder recorder)
        {
            long count = SumCounts(recorder);
            return count > 0
                ? (float)(SumValues(recorder) * NanosecondsToMilliseconds / count)
                : -1f;
        }

        private static float AverageFrameMilliseconds(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0)
            {
                return -1f;
            }

            long total = 0L;
            int frames = 0;
            for (int index = 0; index < recorder.Count; index++)
            {
                ProfilerRecorderSample sample = recorder.GetSample(index);
                if (sample.Count <= 0)
                {
                    continue;
                }
                total += sample.Value;
                frames++;
            }
            return frames > 0
                ? (float)(total * NanosecondsToMilliseconds / frames)
                : -1f;
        }

        private static long AverageBytes(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0)
            {
                return -1L;
            }

            long total = 0L;
            int frames = 0;
            for (int index = 0; index < recorder.Count; index++)
            {
                ProfilerRecorderSample sample = recorder.GetSample(index);
                total += sample.Value;
                frames++;
            }
            return frames > 0 ? total / frames : -1L;
        }

        private static long SumValues(ProfilerRecorder recorder)
        {
            if (!recorder.Valid)
            {
                return 0L;
            }

            long total = 0L;
            for (int index = 0; index < recorder.Count; index++)
            {
                total += recorder.GetSample(index).Value;
            }
            return total;
        }

        private static long SumCounts(ProfilerRecorder recorder)
        {
            if (!recorder.Valid)
            {
                return 0L;
            }

            long total = 0L;
            for (int index = 0; index < recorder.Count; index++)
            {
                total += recorder.GetSample(index).Count;
            }
            return total;
        }
    }
}
