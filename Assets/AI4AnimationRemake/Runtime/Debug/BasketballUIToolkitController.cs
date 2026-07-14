using UnityEngine;
using UnityEngine.UIElements;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class BasketballUIToolkitController : MonoBehaviour
    {
        private const float BarUpdateEpsilon = 0.1f;
        private const float MomentumScale = 2f / BasketballAgentState.KeyCount;

        private static readonly Color DiskBackground = new(0.025f, 0.04f, 0.065f, 0.96f);
        private static readonly Color DiskInner = new(0.055f, 0.08f, 0.115f, 0.92f);
        private static readonly Color GridColor = new(0.50f, 0.58f, 0.68f, 0.16f);
        private static readonly Color ReferenceColor = new(0.86f, 0.65f, 0.13f, 0.85f);
        private static readonly Color TargetColor = new(1f, 0.73f, 0.18f, 1f);
        private static readonly Color MomentumColor = new(0.71f, 0.45f, 1f, 0.62f);
        private static readonly Color HeightColor = new(0.32f, 0.88f, 0.54f, 0.9f);
        private static readonly Color SpeedColor = new(0.92f, 0.34f, 0.90f, 0.9f);

        [SerializeField] private BasketballNeuralController controller;
        [SerializeField] private BasketballKeyboardMouseInputProvider inputProvider;
        [SerializeField] private BasketballDebugVisualizer visualizer;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private bool telemetryVisible = true;

        private readonly float[] gating = new float[BasketballModelAsset.ExpertCount];
        private readonly VisualElement[] styleBars = new VisualElement[BasketballAgentState.StyleCount];
        private readonly VisualElement[] contactBars = new VisualElement[BasketballAgentState.ContactCount];
        private readonly VisualElement[] expertBars = new VisualElement[BasketballModelAsset.ExpertCount];
        private readonly float[] lastStylePercent = new float[BasketballAgentState.StyleCount];
        private readonly float[] lastContactPercent = new float[BasketballAgentState.ContactCount];
        private readonly float[] lastExpertPercent = new float[BasketballModelAsset.ExpertCount];
        private readonly BasketballPerformanceMonitor performanceMonitor = new();

        private UIDocument document;
        private VisualElement root;
        private VisualElement telemetryCard;
        private VisualElement expertCard;
        private VisualElement performanceCard;
        private VisualElement controlCard;
        private VisualElement controlDisk;
        private Button hudToggle;
        private Button debugToggle;
        private Label ballState;
        private Label tickLabel;
        private Label controlState;
        private Label modeLabel;
        private Label fpsLabel;
        private Label frameTimeLabel;
        private Label mainThreadLabel;
        private Label gpuTimeLabel;
        private Label gcLabel;
        private Label inferenceLabel;
        private Label sentisLatencyLabel;
        private Label neuralPipelineLabel;
        private Label animationLabel;
        private Label cameraCpuLabel;
        private Label neuralRateLabel;
        private Label backendLabel;
        private float refreshTimer;
        private float performanceRefreshTimer;
        private float controlDiskRefreshTimer;
        private bool telemetryInitialized;
        private bool hasControlState;
        private bool lastControlling;
        private BasketballBallAuthorityState lastBallState;
        private int lastTickCount = -1;
        private bool performanceStarted;
        private string lastModeText;
        private string lastBackendText;
        private bool isBound;

        public bool IsBound => isBound;
        public VisualElement Root => root;

        private void OnEnable()
        {
            TryBind();
            if (performanceStarted)
            {
                performanceMonitor.Start();
            }
        }

        private void Start()
        {
            TryBind();
            performanceMonitor.Start();
            performanceStarted = true;
            performanceMonitor.ResetTickRate(
                controller != null && controller.IsInitialized
                    ? controller.SimulationTickCount
                    : -1);
            RefreshTelemetry();
            RefreshPerformance();
        }

        private void OnDisable()
        {
            performanceMonitor.Stop();
            Unbind();
        }

        private void Update()
        {
            if (!isBound)
            {
                TryBind();
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            BasketballRuntimeSettings settings = controller != null
                ? controller.RuntimeSettings
                : BasketballRuntimeSettings.LoadDefault();
            performanceMonitor.Tick(
                deltaTime,
                controller != null && controller.IsInitialized
                    ? controller.SimulationTickCount
                    : -1,
                settings != null ? settings.FrameTimingCaptureInterval : 4);

            bool controlling = inputProvider != null && inputProvider.IsBallControlMode;
            if (!hasControlState || controlling != lastControlling)
            {
                controlCard.EnableInClassList("is-controlling", controlling);
                controlState.text = controlling ? "ACTIVE" : "IDLE";
                lastControlling = controlling;
                hasControlState = true;
            }

            controlDiskRefreshTimer += deltaTime;
            float controlDiskInterval = settings != null
                ? settings.ControlDiskRefreshInterval
                : 1f / 30f;
            if (controlDiskRefreshTimer >= controlDiskInterval)
            {
                controlDiskRefreshTimer = 0f;
                controlDisk.MarkDirtyRepaint();
            }

            if (!telemetryVisible)
            {
                return;
            }

            refreshTimer += deltaTime;
            float telemetryInterval = settings != null
                ? settings.TelemetryRefreshInterval
                : 1f / 15f;
            if (refreshTimer >= telemetryInterval)
            {
                refreshTimer = 0f;
                RefreshTelemetry();
            }

            performanceRefreshTimer += deltaTime;
            float performanceInterval = settings != null
                ? settings.PerformanceRefreshInterval
                : 1f / 4f;
            if (performanceRefreshTimer >= performanceInterval)
            {
                performanceRefreshTimer = 0f;
                RefreshPerformance();
            }
        }

        private void TryBind()
        {
            if (isBound)
            {
                return;
            }

            document = GetComponent<UIDocument>();
            root = document != null ? document.rootVisualElement : null;
            if (root == null)
            {
                return;
            }

            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            telemetryCard = root.Q<VisualElement>("telemetry-card");
            expertCard = root.Q<VisualElement>("expert-card");
            performanceCard = root.Q<VisualElement>("performance-card");
            controlCard = root.Q<VisualElement>("control-card");
            controlDisk = root.Q<VisualElement>("control-disk");
            hudToggle = root.Q<Button>("hud-toggle");
            debugToggle = root.Q<Button>("debug-toggle");
            ballState = root.Q<Label>("ball-state");
            tickLabel = root.Q<Label>("tick-label");
            controlState = root.Q<Label>("control-state");
            modeLabel = root.Q<Label>("mode-label");
            fpsLabel = root.Q<Label>("fps-label");
            frameTimeLabel = root.Q<Label>("frame-time-label");
            mainThreadLabel = root.Q<Label>("main-thread-label");
            gpuTimeLabel = root.Q<Label>("gpu-time-label");
            gcLabel = root.Q<Label>("gc-label");
            inferenceLabel = root.Q<Label>("inference-label");
            sentisLatencyLabel = root.Q<Label>("sentis-latency-label");
            neuralPipelineLabel = root.Q<Label>("neural-pipeline-label");
            animationLabel = root.Q<Label>("animation-label");
            cameraCpuLabel = root.Q<Label>("camera-cpu-label");
            neuralRateLabel = root.Q<Label>("neural-rate-label");
            backendLabel = root.Q<Label>("backend-label");

            if (telemetryCard == null || expertCard == null || performanceCard == null ||
                controlCard == null ||
                controlDisk == null || hudToggle == null || debugToggle == null ||
                ballState == null || tickLabel == null || controlState == null ||
                fpsLabel == null || frameTimeLabel == null || mainThreadLabel == null ||
                gpuTimeLabel == null || gcLabel == null || inferenceLabel == null ||
                sentisLatencyLabel == null ||
                neuralPipelineLabel == null || animationLabel == null ||
                cameraCpuLabel == null ||
                neuralRateLabel == null || backendLabel == null)
            {
                return;
            }

            for (int index = 0; index < styleBars.Length; index++)
            {
                styleBars[index] = root.Q<VisualElement>($"style-{index}");
            }
            for (int index = 0; index < contactBars.Length; index++)
            {
                contactBars[index] = root.Q<VisualElement>($"contact-{index}");
            }
            for (int index = 0; index < expertBars.Length; index++)
            {
                expertBars[index] = root.Q<VisualElement>($"expert-{index}");
            }

            hudToggle.clicked += ToggleTelemetry;
            debugToggle.clicked += ToggleDebug;
            controlDisk.generateVisualContent += DrawControlDisk;
            isBound = true;
            ApplyVisibility();
        }

        private void Unbind()
        {
            if (!isBound)
            {
                return;
            }

            hudToggle.clicked -= ToggleTelemetry;
            debugToggle.clicked -= ToggleDebug;
            controlDisk.generateVisualContent -= DrawControlDisk;
            hasControlState = false;
            isBound = false;
        }

        private void ToggleTelemetry()
        {
            telemetryVisible = !telemetryVisible;
            ApplyVisibility();
            if (telemetryVisible)
            {
                refreshTimer = 0f;
                performanceRefreshTimer = 0f;
                RefreshTelemetry();
                RefreshPerformance();
            }
        }

        private void ToggleDebug()
        {
            if (visualizer == null)
            {
                return;
            }

            visualizer.SetVisible(!visualizer.ShowDebugLines);
            debugToggle.EnableInClassList("is-active", visualizer.ShowDebugLines);
        }

        private void ApplyVisibility()
        {
            DisplayStyle display = telemetryVisible ? DisplayStyle.Flex : DisplayStyle.None;
            telemetryCard.style.display = display;
            expertCard.style.display = display;
            performanceCard.style.display = display;
            hudToggle.EnableInClassList("is-active", telemetryVisible);
            debugToggle.EnableInClassList(
                "is-active", visualizer != null && visualizer.ShowDebugLines);
        }

        private void RefreshTelemetry()
        {
            if (!telemetryVisible || !isBound || controller == null || !controller.IsInitialized)
            {
                return;
            }

            BasketballAgentState state = controller.State;
            int pivot = BasketballAgentState.Pivot;
            BasketballBallAuthorityState currentBallState = controller.BallState;
            if (!telemetryInitialized || currentBallState != lastBallState)
            {
                ballState.text = BallStateText(currentBallState);
                lastBallState = currentBallState;
            }
            if (!telemetryInitialized || state.TickCount != lastTickCount)
            {
                tickLabel.text = $"TICK {state.TickCount:000000}";
                lastTickCount = state.TickCount;
            }

            for (int channel = 0; channel < styleBars.Length; channel++)
            {
                float value = state.Styles[BasketballAgentState.StyleIndex(pivot, channel)];
                float percent = 100f * Mathf.Clamp01(value);
                if (!telemetryInitialized ||
                    Mathf.Abs(percent - lastStylePercent[channel]) >= BarUpdateEpsilon)
                {
                    styleBars[channel].style.width = Length.Percent(percent);
                    lastStylePercent[channel] = percent;
                }
            }
            for (int channel = 0; channel < contactBars.Length; channel++)
            {
                float value = state.Contacts[BasketballAgentState.ContactIndex(pivot, channel)];
                float percent = 100f * Mathf.Clamp01(value);
                if (!telemetryInitialized ||
                    Mathf.Abs(percent - lastContactPercent[channel]) >= BarUpdateEpsilon)
                {
                    contactBars[channel].style.width = Length.Percent(percent);
                    lastContactPercent[channel] = percent;
                }
            }

            controller.CopyGatingWeights(gating);
            for (int expert = 0; expert < expertBars.Length; expert++)
            {
                float percent = 100f * Mathf.Clamp01(gating[expert]);
                if (!telemetryInitialized ||
                    Mathf.Abs(percent - lastExpertPercent[expert]) >= BarUpdateEpsilon)
                {
                    expertBars[expert].style.height = Length.Percent(percent);
                    lastExpertPercent[expert] = percent;
                }
            }
            telemetryInitialized = true;
        }

        private void RefreshPerformance()
        {
            if (!telemetryVisible || !isBound)
            {
                return;
            }

            performanceMonitor.Sample();
            string backendText = controller != null
                ? controller.InferenceBackendName
                : "UNINITIALIZED";
            if (backendText != lastBackendText)
            {
                backendLabel.text = backendText;
                lastBackendText = backendText;
            }
            fpsLabel.text = performanceMonitor.FramesPerSecond > 0f
                ? $"{performanceMonitor.FramesPerSecond:F0}"
                : "N/A";
            frameTimeLabel.text = MillisecondsText(performanceMonitor.FrameMilliseconds);
            mainThreadLabel.text = MillisecondsText(performanceMonitor.MainThreadMilliseconds);
            gpuTimeLabel.text = MillisecondsText(performanceMonitor.GpuFrameMilliseconds);
            gcLabel.text = BytesText(performanceMonitor.GcAllocatedBytesPerFrame);
            inferenceLabel.text = MillisecondsText(performanceMonitor.InferenceMilliseconds);
            float inferenceRoundTrip = controller != null
                ? controller.InferenceRoundTripMilliseconds
                : -1f;
            sentisLatencyLabel.text = inferenceRoundTrip >= 0f
                ? controller.IsInferencePending
                    ? $"{inferenceRoundTrip:F2} ms • RUN"
                    : $"{inferenceRoundTrip:F2} ms"
                : "N/A";
            neuralPipelineLabel.text =
                MillisecondsText(performanceMonitor.NeuralPipelineMilliseconds);
            animationLabel.text = MillisecondsText(performanceMonitor.AnimationMilliseconds);
            cameraCpuLabel.text = MillisecondsText(performanceMonitor.CameraMilliseconds);
            neuralRateLabel.text = performanceMonitor.NeuralTicksPerSecond > 0f
                ? $"{performanceMonitor.NeuralTicksPerSecond:F1} Hz"
                : "WARMUP";

            bool underPressure =
                performanceMonitor.FrameMilliseconds > 16.7f ||
                performanceMonitor.GcAllocatedBytesPerFrame > 0L;
            performanceCard.EnableInClassList("has-pressure", underPressure);
        }

        private void DrawControlDisk(MeshGenerationContext context)
        {
            Rect rect = controlDisk.contentRect;
            if (rect.width < 8f || rect.height < 8f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.405f;

            FillCircle(painter, center + new Vector2(0f, 3f), radius + 7f,
                new Color(0f, 0f, 0f, 0.28f));
            FillCircle(painter, center, radius + 5f, DiskBackground);
            FillCircle(painter, center, radius, DiskInner);
            StrokeCircle(painter, center, radius, GridColor, 1.5f);
            StrokeCircle(painter, center, radius * 0.52f, GridColor, 1f);

            StrokeLine(painter, center + Vector2.left * radius, center + Vector2.right * radius,
                GridColor, 1f);
            StrokeLine(painter, center + Vector2.up * radius, center + Vector2.down * radius,
                GridColor, 1f);

            const float markerRadius = 3.2f;
            FillCircle(painter, center + Vector2.up * radius, markerRadius, ReferenceColor);
            FillCircle(painter, center + Vector2.down * radius, markerRadius, ReferenceColor);
            FillCircle(painter, center + Vector2.left * radius, markerRadius, ReferenceColor);
            FillCircle(painter, center + Vector2.right * radius, markerRadius, ReferenceColor);

            DrawModelTrajectory(painter, center, radius);
            DrawVerticalMeters(painter, center, radius);

            Vector2 input = inputProvider != null
                ? Vector2.ClampMagnitude(inputProvider.CurrentBallControl, 1f)
                : Vector2.zero;
            Vector2 inputPoint = center + new Vector2(input.x, -input.y) * radius;
            float strength = input.magnitude;
            StrokeLine(painter, center, inputPoint, TargetColor,
                Mathf.Lerp(1.5f, 4f, strength));
            if (inputProvider != null && inputProvider.IsBallControlMode)
            {
                FillCircle(painter, inputPoint, 10f, new Color(1f, 0.72f, 0.18f, 0.15f));
            }
            FillCircle(painter, inputPoint, 5.5f, TargetColor);
            FillCircle(painter, inputPoint, 2.2f, Color.white);
        }

        private void DrawModelTrajectory(Painter2D painter, Vector2 center, float radius)
        {
            if (controller == null || !controller.IsInitialized)
            {
                return;
            }

            BasketballAgentState state = controller.State;
            Vector2 previous = Vector2.zero;
            bool hasPrevious = false;
            int key = 0;
            for (int sample = 0; sample < BasketballAgentState.SampleCount;
                 sample += BasketballAgentState.Resolution, key++)
            {
                Vector3 pivot = state.Pivots[sample];
                Vector2 normalized = Vector2.ClampMagnitude(new Vector2(pivot.x, -pivot.z), 1f);
                Vector2 point = center + normalized * radius;
                float weight = Mathf.Sqrt((key + 1f) / BasketballAgentState.KeyCount);
                Color pivotColor = Color.Lerp(
                    new Color(0.95f, 0.25f, 0.24f, 0.52f),
                    new Color(0.32f, 0.90f, 0.55f, 0.96f),
                    weight);

                if (hasPrevious)
                {
                    StrokeLine(painter, previous, point,
                        new Color(1f, 0.30f, 0.28f, 0.52f), 1.5f);
                }

                Vector3 momentum = state.Momentums[sample];
                Vector2 momentum2D = new(momentum.x, -momentum.z);
                Vector2 momentumEnd = point + momentum2D * (radius * MomentumScale);
                StrokeLine(painter, point, momentumEnd,
                    Color.Lerp(new Color(0.18f, 0.15f, 0.25f, 0.24f), MomentumColor, weight),
                    Mathf.Lerp(0.8f, 1.8f, weight));
                FillCircle(painter, point, Mathf.Lerp(2f, 4f, weight), pivotColor);

                previous = point;
                hasPrevious = true;
            }
        }

        private void DrawVerticalMeters(Painter2D painter, Vector2 center, float radius)
        {
            if (controller == null || !controller.IsInitialized)
            {
                return;
            }

            BasketballAgentState state = controller.State;
            float height = Mathf.InverseLerp(0.25f, 2f, state.Pivots[BasketballAgentState.Pivot].y);
            float speed = Mathf.InverseLerp(0f, 4f, state.Momentums[BasketballAgentState.Pivot].y);
            float meterHeight = radius * 1.35f;
            float top = center.y - meterHeight * 0.5f;
            float bottom = center.y + meterHeight * 0.5f;
            float left = center.x - radius - 12f;
            float right = center.x + radius + 12f;

            StrokeLine(painter, new Vector2(left, top), new Vector2(left, bottom), GridColor, 5f);
            StrokeLine(painter, new Vector2(right, top), new Vector2(right, bottom), GridColor, 5f);
            FillCircle(painter, new Vector2(left, Mathf.Lerp(bottom, top, height)), 4f, HeightColor);
            FillCircle(painter, new Vector2(right, Mathf.Lerp(bottom, top, speed)), 4f, SpeedColor);
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill();
        }

        private static void StrokeCircle(
            Painter2D painter, Vector2 center, float radius, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Stroke();
        }

        private static void StrokeLine(
            Painter2D painter, Vector2 start, Vector2 end, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }

        public void Configure(
            BasketballNeuralController neuralController,
            BasketballKeyboardMouseInputProvider provider,
            BasketballDebugVisualizer debugVisualizer,
            StyleSheet hudStyleSheet)
        {
            controller = neuralController;
            inputProvider = provider;
            visualizer = debugVisualizer;
            styleSheet = hudStyleSheet;
            telemetryVisible = true;
        }

        public void SetSources(
            BasketballNeuralController neuralController,
            BasketballKeyboardMouseInputProvider provider,
            BasketballDebugVisualizer debugVisualizer)
        {
            controller = neuralController;
            inputProvider = provider;
            visualizer = debugVisualizer;
            telemetryInitialized = false;
            BasketballRuntimeSettings settings = controller != null
                ? controller.RuntimeSettings
                : BasketballRuntimeSettings.LoadDefault();
            refreshTimer = settings != null ? settings.TelemetryRefreshInterval : 1f / 15f;
            performanceRefreshTimer = settings != null
                ? settings.PerformanceRefreshInterval
                : 0.25f;
            controlDiskRefreshTimer = settings != null
                ? settings.ControlDiskRefreshInterval
                : 1f / 30f;
            hasControlState = false;
            lastBackendText = null;
            performanceMonitor.ResetTickRate(
                controller != null && controller.IsInitialized
                    ? controller.SimulationTickCount
                    : -1);
            if (isBound)
            {
                controlDisk.MarkDirtyRepaint();
                ApplyVisibility();
            }
        }

        public void SetMatchStatus(string value)
        {
            if (modeLabel != null && value != lastModeText)
            {
                modeLabel.text = value;
                lastModeText = value;
            }
        }

        private static string BallStateText(BasketballBallAuthorityState value) => value switch
        {
            BasketballBallAuthorityState.Controlled => "CONTROLLED",
            BasketballBallAuthorityState.Held => "HELD",
            BasketballBallAuthorityState.Released => "RELEASED",
            BasketballBallAuthorityState.FreePhysics => "FREE PHYSICS",
            BasketballBallAuthorityState.Reacquiring => "REACQUIRING",
            _ => "UNKNOWN"
        };

        private static string MillisecondsText(float value) =>
            value >= 0f ? $"{value:F2} ms" : "N/A";

        private static string BytesText(long value)
        {
            if (value < 0L)
            {
                return "N/A";
            }
            return value < 1024L ? $"{value} B" : $"{value / 1024f:F1} KB";
        }
    }
}
