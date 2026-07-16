using CrowdEyes.AI4Animation.Basketball;
using UnityEditor;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Editor
{
    internal static class BasketballInspectorGUI
    {
        private const float BilingualRowHeight = 34f;

        private static readonly string[] InterpolationNames =
        {
            "關閉（直接顯示神經幀）",
            "線性插值",
            "平滑曲線插值"
        };

        public static void Header(string title, string description)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        public static bool Section(ref bool expanded, string title, string description = null)
        {
            EditorGUILayout.Space(3f);
            // A Component Inspector is already hosted inside Unity's own foldout
            // header. BeginFoldoutHeaderGroup cannot be nested, so use the plain
            // foldout control here and avoid leaking a header group to the next
            // component on the same GameObject.
            expanded = EditorGUILayout.Foldout(
                expanded,
                title,
                true,
                EditorStyles.foldout);
            if (expanded && !string.IsNullOrEmpty(description))
            {
                EditorGUILayout.HelpBox(description, MessageType.None);
            }
            return expanded;
        }

        public static void EndSection()
        {
            // Intentionally empty. See Section: normal foldouts do not own a
            // begin/end GUI group and are safe inside a Component Inspector.
        }

        public static void Property(
            SerializedObject serialized,
            string propertyName,
            string label,
            string tooltip,
            bool includeChildren = true)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            float propertyHeight = EditorGUI.GetPropertyHeight(
                property,
                GUIContent.none,
                includeChildren);
            bool fullWidthField = property.isArray ||
                                  property.propertyType ==
                                  SerializedPropertyType.Generic;
            Rect row = EditorGUILayout.GetControlRect(
                false,
                fullWidthField
                    ? BilingualRowHeight + propertyHeight
                    : Mathf.Max(BilingualRowHeight, propertyHeight));
            if (fullWidthField)
            {
                DrawBilingualLabel(
                    new Rect(row.x, row.y, row.width, BilingualRowHeight),
                    label,
                    property.displayName,
                    tooltip);
                EditorGUI.PropertyField(
                    new Rect(
                        row.x,
                        row.y + BilingualRowHeight,
                        row.width,
                        propertyHeight),
                    property,
                    GUIContent.none,
                    includeChildren);
                return;
            }

            float labelWidth = Mathf.Clamp(
                EditorGUIUtility.labelWidth + 36f,
                168f,
                row.width * 0.58f);
            Rect labelRect = new Rect(
                row.x,
                row.y,
                labelWidth - 5f,
                BilingualRowHeight);
            Rect fieldRect = new Rect(
                row.x + labelWidth,
                row.y,
                row.width - labelWidth,
                propertyHeight);

            DrawBilingualLabel(
                labelRect,
                label,
                property.displayName,
                tooltip);
            EditorGUI.PropertyField(
                fieldRect,
                property,
                GUIContent.none,
                includeChildren);
        }

        public static void InterpolationProperty(SerializedObject serialized)
        {
            SerializedProperty property = serialized.FindProperty("interpolationMode");
            if (property == null)
            {
                return;
            }

            Rect row = EditorGUILayout.GetControlRect(false, BilingualRowHeight);
            float labelWidth = Mathf.Clamp(
                EditorGUIUtility.labelWidth + 36f,
                168f,
                row.width * 0.58f);
            Rect labelRect = new Rect(
                row.x,
                row.y,
                labelWidth - 5f,
                BilingualRowHeight);
            Rect popupRect = new Rect(
                row.x + labelWidth,
                row.y,
                row.width - labelWidth,
                EditorGUIUtility.singleLineHeight);
            const string tooltip =
                "只影響畫面呈現，不會把插值結果寫回閉迴路神經狀態。";
            DrawBilingualLabel(
                labelRect,
                "畫面姿勢插值",
                property.displayName,
                tooltip);

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUI.Popup(
                popupRect,
                property.enumValueIndex,
                InterpolationNames);
            if (EditorGUI.EndChangeCheck())
            {
                property.enumValueIndex = value;
            }
            EditorGUI.showMixedValue = false;
        }

        private static void DrawBilingualLabel(
            Rect rect,
            string traditionalChinese,
            string english,
            string tooltip)
        {
            string secondaryColor = EditorGUIUtility.isProSkin
                ? "#A8A8A8"
                : "#626262";
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 2, 0, 0)
            };
            GUI.Label(
                rect,
                new GUIContent(
                    $"{traditionalChinese}\n<size=10><color={secondaryColor}>{english}</color></size>",
                    tooltip),
                style);
        }

        public static void ReadOnly(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, value ?? "—");
            }
        }

        public static void SelectObjectButton(Object value, string label)
        {
            using (new EditorGUI.DisabledScope(value == null))
            {
                if (GUILayout.Button(label))
                {
                    Selection.activeObject = value;
                    EditorGUIUtility.PingObject(value);
                }
            }
        }

        public static void DrawNeuralSettings(
            SerializedObject settings,
            bool showPresets)
        {
            if (showPresets)
            {
                EditorGUILayout.LabelField("快速模式", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("30 Hz 參考"))
                {
                    ApplyNeuralPreset(settings, 30, BasketballPoseInterpolationMode.Linear);
                }
                if (GUILayout.Button("20 Hz 實驗"))
                {
                    ApplyNeuralPreset(settings, 20, BasketballPoseInterpolationMode.SmoothStep);
                }
                if (GUILayout.Button("15 Hz 實驗"))
                {
                    ApplyNeuralPreset(settings, 15, BasketballPoseInterpolationMode.SmoothStep);
                }
                if (GUILayout.Button("10 Hz 實驗"))
                {
                    ApplyNeuralPreset(settings, 10, BasketballPoseInterpolationMode.SmoothStep);
                }
                EditorGUILayout.EndHorizontal();
            }

            Property(
                settings,
                "neuralTickRate",
                "神經推論頻率 (Hz)",
                "30 Hz 是 Basketball 2020 模型的正式參考頻率；20、15、10 Hz 都是實驗模式。降低頻率不會改寫模型內部的 30 Hz 數學定義。");
            InterpolationProperty(settings);
            Property(
                settings,
                "maximumCatchUpTicks",
                "單幀最多追趕 Tick",
                "畫面卡頓後，一個 Render Frame 最多補算多少個 neural tick。太高可能造成單幀尖峰，太低會讓模擬落後。建議 3～4。");
            Property(
                settings,
                "enableContactIK",
                "啟用接觸 IK",
                "開啟手、腳與球的接觸後處理。關閉可用來量測純神經動畫成本。",
                false);
            Property(
                settings,
                "enableDebugDraw",
                "啟用 Debug 視覺化",
                "顯示軌跡、球控制與模型資訊。量測效能時建議關閉。",
                false);
            Property(
                settings,
                "deterministicMode",
                "確定性模式",
                "維持固定排程與可重現的模擬順序。正式比較與回歸測試建議開啟。",
                false);

            SerializedProperty rate = settings.FindProperty("neuralTickRate");
            if (rate != null && !rate.hasMultipleDifferentValues && rate.intValue != 30)
            {
                EditorGUILayout.HelpBox(
                    "目前不是 30 Hz 參考模式。請特別檢查運球節奏、腳滑、接球、投籃離手與快速轉向。",
                    MessageType.Warning);
            }
        }

        public static void DrawPassingSettings(SerializedObject settings)
        {
            Property(
                settings,
                "directPassApexClearance",
                "一般傳球最低弧高 (m)",
                "胸前傳球／提前量傳球的軌跡最高點，至少高於離手點與接球點多少公尺。越低越平；遠距離仍會為了速度上限自動增加弧度。只有一般傳球使用。" );
            Property(
                settings,
                "lobPassApexClearance",
                "高拋傳球最低弧高 (m)",
                "只有明確指定 Lob Pass 時使用。數值越大，球飛得越高、時間越長。" );
            Property(
                settings,
                "maximumDirectPassSpeed",
                "一般傳球水平速度上限 (m/s)",
                "降低速度會讓遠距離傳球必須提高弧線；提高速度可讓球路更平，但接球窗口會更短。預設 11.5，最終仍受每位球員 Maximum Power 限制。" );
            Property(
                settings,
                "maximumLobPassSpeed",
                "高拋傳球水平速度上限 (m/s)",
                "高拋傳球的水平速度限制。" );
            Property(
                settings,
                "maximumReceiverLeadDistance",
                "最大提前量距離 (m)",
                "跑動接球時，預測接球點最多能在接球者前方多遠。太大容易傳過頭，太小會傳到身後。" );
            Property(
                settings,
                "receiverLeadScale",
                "接球者速度預測倍率",
                "接球者目前速度對提前量的影響。0 代表傳向目前位置，1 代表完整速度預測。" );
            Property(
                settings,
                "passReleaseBelowChestTolerance",
                "允許低於胸口離手距離 (m)",
                "一般傳球會等待模型把球抬到胸口附近。數值越小，要求的離手位置越高；太小可能等不到合格姿勢。" );
            Property(settings, "passGatherSeconds", "Gather 結束時間 (s)", "先穩定持球的短暫準備段；提高會讓傳球反應變慢。" );
            Property(settings, "passAlignSeconds", "Align 結束時間 (s)", "面向傳球方向的結束時間。" );
            Property(settings, "passPushSeconds", "Push 結束時間 (s)", "水平前推姿勢的結束時間；之後進入離手等待。" );
            Property(settings, "minimumPassReleaseSeconds", "最早傳球離手 (s)", "避免同一幀瞬移離手，同時保持單次點擊後快速完成。" );
            Property(
                settings,
                "forcedPassReleaseSeconds",
                "強制離手截止時間 (s)",
                "預訓練模型未產生理想離手姿勢時，經過多久仍交給物理。太短容易從低運球點出手，太長會讓操作延遲。" );
        }

        public static void DrawShootingSettings(SerializedObject settings)
        {
            Property(settings, "shotApexClearance", "投籃基本弧高 (m)", "最高點至少高於離手點或籃框的距離。降低會更平；提高會形成較高拋物線。" );
            Property(settings, "shotDistanceApexScale", "距離弧高倍率", "每增加一公尺水平距離所追加的弧高，直到最大弧高上限。" );
            Property(settings, "maximumShotApexClearance", "投籃最大弧高 (m)", "限制遠距離投籃的額外最高點，避免球路過度高拋。" );
            Property(settings, "maximumShotLaunchSpeed", "最大投籃離手速度 (m/s)", "一次性物理離手速度的安全上限；超過代表該距離超出目前支援範圍。" );
            Property(settings, "minimumShotReleaseSeconds", "最早投籃離手 (s)", "保留模型形成投籃姿勢的最短時間。" );
            Property(settings, "forcedShotReleaseSeconds", "投籃強制離手截止 (s)", "接觸訊號不理想時仍會在此時間一次性交給物理，避免球卡在手中。" );
            Property(settings, "minimumShotReleaseHeight", "自然離手最低高度 (m)", "自然離手候選需達到的球高；強制截止仍可避免永久卡住。" );
            Property(settings, "naturalShotReleaseHandContact", "自然離手手部接觸上限", "低於此接觸權重才視為模型自然放球。" );
            Property(settings, "shotFacingDot", "出手面向門檻", "角色面向籃框的 Dot 門檻。0.9 約等於容許 26°；越高要求越正。" );
            Property(settings, "maximumShotAlignSeconds", "最長瞄準轉向時間 (s)", "Space 後最多花多久轉向籃框，再提交原模型 Shoot style。" );
            Property(settings, "shotCommandTimeoutSeconds", "投籃命令逾時 (s)", "模型未在期限內離手時取消鎖定，避免 Shoot 狀態永久卡住。" );
        }

        private static void ApplyNeuralPreset(
            SerializedObject settings,
            int rate,
            BasketballPoseInterpolationMode interpolation)
        {
            settings.FindProperty("neuralTickRate").intValue = rate;
            settings.FindProperty("interpolationMode").enumValueIndex = (int)interpolation;
            settings.FindProperty("maximumCatchUpTicks").intValue = 4;
            settings.FindProperty("enableContactIK").boolValue = true;
            settings.FindProperty("deterministicMode").boolValue = true;
            settings.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings.targetObject);
        }
    }

    [CustomEditor(typeof(BasketballMatchController))]
    [CanEditMultipleObjects]
    public sealed class BasketballMatchControllerEditor : UnityEditor.Editor
    {
        private bool showConnections = true;
        private bool showMatchMode = true;
        private bool showPassControls = true;
        private bool showInference = true;
        private bool showPassingPhysics;
        private bool showShootingPhysics = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Match／比賽控制台",
                "集中管理玩家切換、傳球鎖定、共用球權與 GPU 神經推論設定。將滑鼠停在欄位名稱上可查看繁體中文說明。");

            if (BasketballInspectorGUI.Section(
                    ref showConnections,
                    "場景連接",
                    "這些是場景物件引用。正常調參時通常不需要更動。"))
            {
                BasketballInspectorGUI.Property(serializedObject, "teamGroups", "參賽隊伍", "比賽中的所有隊伍（如 Home 與 Away）。系統會在遊戲開始時自動抓取旗下的所有球員。" );
                BasketballInspectorGUI.Property(serializedObject, "ball", "共用比賽球", "所有球權與物理狀態共用的唯一實體球。" );
                BasketballInspectorGUI.Property(serializedObject, "possessionManager", "球權管理器", "唯一球權、傳球、接球、攔截與搶球仲裁來源。" );
                BasketballInspectorGUI.Property(serializedObject, "court", "比賽球場／Court", "提供雙籃框、進攻方向、三分線與投籃目標。" );
                BasketballInspectorGUI.Property(serializedObject, "viewCamera", "遊戲攝影機", "用於畫面方向、傳球目標選擇與 UI。" );
                BasketballInspectorGUI.Property(serializedObject, "orbitCamera", "第三人稱環繞相機", "滑鼠控制的自由視角元件。" );
                BasketballInspectorGUI.Property(serializedObject, "hud", "UI Toolkit HUD", "遊戲內狀態、控制圓盤與效能資訊。" );
                BasketballInspectorGUI.Property(serializedObject, "sentisBatchScheduler", "GPU 推論排程器", "可留空；執行時會在 BasketballMatch 上自動建立並設定十人 Batch。" );
                BasketballInspectorGUI.Property(serializedObject, "runtimeSettings", "共用 Runtime 設定檔", "十名球員、相機、GPU 排程與 HUD 共用的設定資產。" );
                BasketballInspectorGUI.Property(serializedObject, "worldEventStream", "世界事件紀錄", "固定容量 Ring Buffer；記錄球權、傳球、投籃、搶球與重開球結果，供 Debug 與未來外部 Policy 使用。可留空由 Runtime 自動建立。" );
                BasketballInspectorGUI.Property(serializedObject, "rewardTracker", "Reward Tracker／獎勵追蹤", "將同一份 World Event 轉成 1v1、3v3、5v5 共用的 Team／Player Reward；不會控制動作、球權或 Rigidbody。" );
                BasketballInspectorGUI.Property(serializedObject, "telemetryRecorder", "Telemetry Recorder／技能資料匯出", "可選 JSONL 世界事件紀錄器。預設不寫檔；可留空由 Runtime 自動建立，再於 Play Mode 手動開始。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showMatchMode,
                    "上場人數與比賽模式",
                    "GPU 模型仍保留固定十個 Batch slot；未上場 slot 會安全 Idle，並排除傳球、Rival、接球、搶球、碰撞與 Tab 選擇。"))
            {
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "matchMode",
                    "比賽模式",
                    "選擇 1v1、3v3、5v5 或 Custom。所有模式共用同一套球權、傳球、投籃與神經動畫。" );
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "controlMode",
                    "球員控制模式",
                    "Selected Player Only 保留舊待命行為；Selected Player With Rule AI 由鍵鼠控制目前球員、其餘交給 AI；Full Rule AI 僅保留 Tab 切換觀察鏡頭。" );
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "ruleBasedTeamAI",
                    "Rule-based 團隊 AI",
                    "可留空並由 Runtime 自動建立；負責高階移動、對位、傳球、投籃、搶球與 Loose Ball 決策，不直接寫骨骼或球 Transform。" );
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "decisionPolicyBehaviour",
                    "決策 Policy 元件",
                    "可指定任何實作 IBasketballDecisionPolicy 的 MonoBehaviour。留空時使用上方內建 Rule AI；外部 Policy 仍只能輸出 Command，不能直接寫球權或 Transform。" );
                SerializedProperty mode = serializedObject.FindProperty("matchMode");
                if (mode != null && mode.enumValueIndex ==
                    (int)BasketballMatchMode.Custom)
                {
                    BasketballInspectorGUI.Property(serializedObject, "customHomePlayers", "TEAM A 上場人數", "Custom 模式下 Team A 的有效球員數。" );
                    BasketballInspectorGUI.Property(serializedObject, "customAwayPlayers", "TEAM B 上場人數", "Custom 模式下 Team B 的有效球員數。" );
                }
                BasketballInspectorGUI.ReadOnly(
                    "GPU Batch 容量",
                    $"固定 {BasketballSentisBatchScheduler.BatchSize} slots");
                if (Application.isPlaying && targets.Length == 1)
                {
                    BasketballMatchController runtimeMatch =
                        (BasketballMatchController)target;
                    BasketballInspectorGUI.ReadOnly(
                        "配置套用狀態／Apply State",
                        runtimeMatch.IsMatchConfigurationPending
                            ? "等待 GPU readback 後安全重置"
                            : "已同步／Ready");
                }
                if (Application.isPlaying && targets.Length == 1 &&
                    GUILayout.Button("套用目前模式／Apply Match Configuration"))
                {
                    serializedObject.ApplyModifiedProperties();
                    BasketballMatchController match = (BasketballMatchController)target;
                    SerializedProperty selectedMode = serializedObject.FindProperty("matchMode");
                    SerializedProperty selectedControl = serializedObject.FindProperty("controlMode");
                    SerializedProperty home = serializedObject.FindProperty("customHomePlayers");
                    SerializedProperty away = serializedObject.FindProperty("customAwayPlayers");
                    match.ApplyMatchConfiguration(
                        (BasketballMatchMode)selectedMode.intValue,
                        (BasketballMatchControlMode)selectedControl.intValue,
                        home.intValue,
                        away.intValue);
                }
            }
            BasketballInspectorGUI.EndSection();

            SerializedProperty settingsProperty = serializedObject.FindProperty("runtimeSettings");
            BasketballRuntimeSettings settingsAsset = settingsProperty != null
                ? settingsProperty.objectReferenceValue as BasketballRuntimeSettings
                : null;

            if (BasketballInspectorGUI.Section(
                    ref showShootingPhysics,
                    "投籃瞄準與物理",
                    "Space 立即鎖存一次 Shoot，人物同步轉向進攻籃框；離手截止前由模型形成姿勢，之後只設定一次拋體速度。"))
            {
                if (settingsAsset != null && targets.Length == 1)
                {
                    SerializedObject settings = new SerializedObject(settingsAsset);
                    settings.Update();
                    BasketballInspectorGUI.DrawShootingSettings(settings);
                    settings.ApplyModifiedProperties();
                }
                else
                {
                    EditorGUILayout.HelpBox("請先指定 Runtime 設定檔。", MessageType.Info);
                }
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "scoreRestartDelay",
                    "進球後重新發球延遲 (s)",
                    "顯示得分後等待多久，再把球權交給失分隊最接近該籃框的球員。重置會同步清除舊 Shot／Pass／Catch／Steal 狀態。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showPassControls,
                    "滑鼠傳球操作",
                    "Ctrl + 滑鼠左鍵會立即提交鎖定隊友的傳球；不再等待長按，也不產生假動作。"))
            {
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "passLockDot",
                    "目標鎖定精準度",
                    "越接近 1，必須看得越準；越低，畫面中央附近更容易鎖定。建議 0.78～0.88。" );
                SerializedProperty lockDot = serializedObject.FindProperty("passLockDot");
                if (lockDot != null && !lockDot.hasMultipleDifferentValues)
                {
                    float halfAngle = Mathf.Acos(Mathf.Clamp(lockDot.floatValue, -1f, 1f)) *
                                      Mathf.Rad2Deg;
                    BasketballInspectorGUI.ReadOnly("目前鎖定半角", $"{halfAngle:F1}°");
                }
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "passTimeoutSeconds",
                    "傳球準備逾時 (s)",
                    "等待 ML 傳球姿勢與離手的最長時間。超時會取消，不會把球卡在準備狀態。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();

            if (BasketballInspectorGUI.Section(
                    ref showInference,
                    "神經推論與插值",
                    "GPUCompute／DirectML 是固定後端；這裡調整 neural tick、畫面插值與追幀策略。十名球員共用同一設定檔。"))
            {
                if (settingsAsset == null)
                {
                    EditorGUILayout.HelpBox(
                        "尚未指定 Runtime 設定檔。執行時會嘗試載入 Resources/Settings/BasketballRuntimeSettings。",
                        MessageType.Warning);
                }
                else if (targets.Length == 1)
                {
                    SerializedObject settings = new SerializedObject(settingsAsset);
                    settings.Update();
                    BasketballInspectorGUI.DrawNeuralSettings(settings, true);
                    settings.ApplyModifiedProperties();
                    BasketballInspectorGUI.SelectObjectButton(settingsAsset, "在 Project 視窗選取完整設定檔");
                }
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showPassingPhysics,
                    "傳球物理與提前量",
                    "這些欄位直接寫入共用 Runtime 設定檔。遠距離傳球的弧高與球速存在物理上的取捨。"))
            {
                if (settingsAsset != null && targets.Length == 1)
                {
                    SerializedObject settings = new SerializedObject(settingsAsset);
                    settings.Update();
                    BasketballInspectorGUI.DrawPassingSettings(settings);
                    settings.ApplyModifiedProperties();
                }
                else
                {
                    EditorGUILayout.HelpBox("請先指定 Runtime 設定檔。", MessageType.Info);
                }
            }
            BasketballInspectorGUI.EndSection();

            if (Application.isPlaying && targets.Length == 1)
            {
                BasketballMatchController match = (BasketballMatchController)target;
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("執行中狀態", EditorStyles.boldLabel);
                BasketballInspectorGUI.ReadOnly(
                    "目前操控球員",
                    match.ActivePlayer != null ? $"P{match.ActivePlayer.PlayerIndex + 1}" : "—");
                BasketballInspectorGUI.ReadOnly(
                    "目前持球者",
                    match.Owner != null ? $"P{match.Owner.PlayerIndex + 1}" : "自由球");
                BasketballInspectorGUI.ReadOnly(
                    "推論後端",
                    match.SentisBatchScheduler != null
                        ? match.SentisBatchScheduler.BackendName
                        : "尚未建立");
                BasketballInspectorGUI.ReadOnly(
                    "場上有效球員",
                    match.CurrentObservation != null
                        ? $"{match.CurrentObservation.ActivePlayerCount} / {match.CurrentObservation.PlayerCapacity}"
                        : "尚未建立");
                BasketballInspectorGUI.ReadOnly(
                    "世界事件",
                    match.WorldEvents != null
                        ? $"{match.WorldEvents.Count} records / seq {match.WorldEvents.LatestSequence}"
                        : "尚未建立");
                BasketballRuleBasedTeamAI teamAI =
                    match.GetComponent<BasketballRuleBasedTeamAI>();
                BasketballInspectorGUI.ReadOnly(
                    "目前球員 AI 角色",
                    teamAI != null && match.ActivePlayer != null
                        ? teamAI.GetRole(match.ActivePlayer.PlayerIndex).ToString()
                        : "—");
            }
        }
    }

    [CustomEditor(typeof(BasketballPossessionManager))]
    [CanEditMultipleObjects]
    public sealed class BasketballPossessionManagerEditor : UnityEditor.Editor
    {
        private bool showReferences = true;
        private bool showCatch = true;
        private bool showSteal = true;
        private bool showTiming = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Possession／球權與接觸",
                "調整傳球接住、Loose Ball、Contested Ball 與搶球判定。判定半徑不會讓飛行中的球自動導引或遠距離吸附。");

            if (BasketballInspectorGUI.Section(ref showReferences, "場景連接"))
            {
                BasketballInspectorGUI.Property(serializedObject, "teamGroups", "參賽隊伍", "系統會在遊戲開始時自動抓取旗下的所有球員。" );
                BasketballInspectorGUI.Property(serializedObject, "ball", "唯一實體球", "由 NeuralPossession 或 Rigidbody Physics 擇一控制。" );
                BasketballInspectorGUI.Property(serializedObject, "court", "球場與籃框", "Shoot 離手時用正確進攻籃框解算一次性物理速度。" );
                BasketballInspectorGUI.Property(serializedObject, "worldEventStream", "世界事件紀錄", "由 Match 注入的固定容量事件流；球權系統只發布結果，不自行分配策略。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showCatch,
                    "接球判定",
                    "一般撿球較嚴格；指定接球者有較大的手部／胸口接球窗口，但球仍必須實際穿過該範圍。"))
            {
                BasketballInspectorGUI.Property(serializedObject, "catchHandDistance", "一般手部接球距離 (m)", "非指定接球者或 Loose Ball 撿球時，手掌距離球的上限。" );
                BasketballInspectorGUI.Property(serializedObject, "catchControlRadius", "一般控制半徑 (m)", "球員 Root 到球的最大距離，避免只靠伸出的骨骼遠距離取球。" );
                BasketballInspectorGUI.Property(serializedObject, "intendedReceiverCatchHandDistance", "指定接球者手部距離 (m)", "傳球目標的手掌接球容錯。" );
                BasketballInspectorGUI.Property(serializedObject, "intendedReceiverControlRadius", "指定接球者控制半徑 (m)", "傳球目標 Root 到球的最大距離。" );
                BasketballInspectorGUI.Property(serializedObject, "intendedReceiverChestCatchRadius", "指定接球者胸前半徑 (m)", "球路穿過胸前時允許進入 CatchBlend；不是遠距離吸球半徑。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumCatchRelativeSpeed", "可接球最大相對速度 (m/s)", "球相對接球手的速度超過此值時視為太快，會漏接或進入 Loose／Contested。" );
                BasketballInspectorGUI.Property(serializedObject, "passCatchHandAssist", "Pass Hand Assist／傳球手部追加容錯 (m)", "只套用到預定接球者；球仍必須實際靠近手部或穿過胸前接球區。" );
                BasketballInspectorGUI.Property(serializedObject, "passCatchControlAssist", "Pass Root Assist／傳球 Root 追加半徑 (m)", "補償神經角色插值與接球者跑動造成的 Root 誤差，不會改變飛行中球路。" );
                BasketballInspectorGUI.Property(serializedObject, "passCatchChestAssist", "Pass Chest Assist／胸前追加容錯 (m)", "擴大傳球目標的局部胸前接觸窗；不是從遠處吸球。" );
                BasketballInspectorGUI.Property(serializedObject, "passCatchSpeedAssist", "Pass Speed Assist／傳球速度追加容錯 (m/s)", "指定接球者可承受的額外相對速度。太高會讓高速球看起來過度容易接住。" );
                BasketballInspectorGUI.Property(serializedObject, "looseBallPickupRootRadius", "Loose Pickup Radius／滾地球撿球半徑 (m)", "球已是 Loose、低速且貼地時，Root 進入此局部半徑即可完成撿球，避免手部模型碰不到腳邊球。" );
                BasketballInspectorGUI.Property(serializedObject, "looseBallPickupMaximumHeight", "Loose Pickup Height／撿球最大離地高度 (m)", "只對貼地或低彈跳 Loose Ball 啟用自動近身撿球。" );
                BasketballInspectorGUI.Property(serializedObject, "looseBallPickupMaximumSpeed", "Loose Pickup Speed／撿球最大速度 (m/s)", "球太快時仍必須先減速，不會高速穿過球員就直接換球權。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumFlightSecureSeconds", "Minimum Flight Time／最短飛行保護 (s)", "傳球或投籃剛離手時暫不允許任何人立刻吸回球。" );
                BasketballInspectorGUI.Property(serializedObject, "releasingPlayerCatchLockout", "Releaser Lockout／離手者接球鎖定 (s)", "避免傳球者或投籃者在球剛離手後立刻重新取得球權。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showSteal,
                    "搶球與拍球",
                    "先形成 Touch，之後才可能 Secure 球權；碰到球不會立刻把 Owner 瞬移給防守者。"))
            {
                BasketballInspectorGUI.Property(serializedObject, "stealHandDistance", "有效手球接觸距離 (m)", "防守者手掌必須進入此距離才可能拍掉球。" );
                BasketballInspectorGUI.Property(serializedObject, "stealInteractionRadius", "搶球互動半徑 (m)", "防守者 Root 必須在持球者附近，才啟用搶球品質判定。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumStealExposure", "最低球暴露程度", "球仍被身體與手穩定保護時不允許只靠距離搶走。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumStealApproach", "最低手掌接近速度", "手必須真的朝球移動；站著重疊不會持續觸發抄球。" );
                BasketballInspectorGUI.Property(serializedObject, "stealQualityThreshold", "拍球成功品質門檻", "距離、暴露程度與手部接近速度的綜合最低分數。越低越容易拍掉。" );
                BasketballInspectorGUI.Property(serializedObject, "cleanStealQuality", "乾淨抄球品質門檻", "達到較高品質時，球會更偏向防守者並進入可 Secure 的 contested 狀態。" );
                BasketballInspectorGUI.Property(serializedObject, "cleanStealHandDistance", "乾淨抄球手部距離 (m)", "除了品質外，手掌還必須更靠近球。" );
                BasketballInspectorGUI.Property(serializedObject, "stealSecureDelay", "碰球後最短控制延遲 (s)", "防止第一次 Touch 的同一幀就直接取得球權。" );
                BasketballInspectorGUI.Property(serializedObject, "stealSecureWindow", "搶球控制窗口 (s)", "拍球後防守者可在這段時間內形成穩定控制。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumStealSecureQuality", "Secure Quality／抄球控制品質", "拍掉球後仍須達到此品質才可進入 CatchBlend。" );
                BasketballInspectorGUI.Property(serializedObject, "stealSecureHandDistance", "Secure Hand Distance／控制手球距離 (m)", "球必須再次靠近防守者手掌，不能只因第一次 Touch 就瞬移到手上。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumStealSecureRelativeSpeed", "Secure Relative Speed／控制最大相對速度 (m/s)", "高速飛離的球不會立刻被判定為已控制。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showTiming, "球權時序"))
            {
                BasketballInspectorGUI.Property(serializedObject, "contestedTimeout", "爭球逾時 (s)", "沒有任何人形成穩定控制時，多久後改成 Loose Ball。" );
                BasketballInspectorGUI.Property(serializedObject, "possessionCooldown", "球權切換冷卻 (s)", "避免多人接觸時 Owner 在角色之間快速跳動。" );
                BasketballInspectorGUI.Property(serializedObject, "catchBlendSeconds", "接球融合時間 (s)", "接球成立後，Rigidbody 球平滑交回神經持球狀態的時間。太長會黏，太短可能跳動。" );
            }
            BasketballInspectorGUI.EndSection();

            SerializedProperty cleanQuality = serializedObject.FindProperty("cleanStealQuality");
            SerializedProperty touchQuality = serializedObject.FindProperty("stealQualityThreshold");
            if (cleanQuality != null && touchQuality != null &&
                cleanQuality.floatValue < touchQuality.floatValue)
            {
                EditorGUILayout.HelpBox(
                    "乾淨抄球門檻應高於或等於一般拍球門檻，否則判定語意會顛倒。",
                    MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && targets.Length == 1)
            {
                BasketballPossessionManager manager = (BasketballPossessionManager)target;
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("執行中球權", EditorStyles.boldLabel);
                BasketballInspectorGUI.ReadOnly("球狀態", manager.BallState.ToString());
                BasketballInspectorGUI.ReadOnly("控制模式", manager.BallControlMode.ToString());
                BasketballInspectorGUI.ReadOnly(
                    "Owner",
                    manager.Owner != null ? $"P{manager.Owner.PlayerIndex + 1}" : "無");
                BasketballInspectorGUI.ReadOnly(
                    "預定接球者",
                    manager.IntendedReceiver != null
                        ? $"P{manager.IntendedReceiver.PlayerIndex + 1}"
                        : "無");
                BasketballShotPlan shot = manager.LastShotPlan;
                BasketballInspectorGUI.ReadOnly(
                    "最近投籃",
                    shot.Shooter != null
                        ? $"P{shot.Shooter.PlayerIndex + 1} / {(shot.IsThreePointer ? 3 : 2)}PT / {shot.Distance:F1} m"
                        : "尚無");
            }
        }
    }

    [CustomEditor(typeof(BasketballRuleBasedTeamAI))]
    public sealed class BasketballRuleBasedTeamAIEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Rule-based Team AI／規則式團隊 AI",
                "實作可替換的 IBasketballDecisionPolicy，只輸出 Intent 與 Skill Command；Match 仲裁命令，AI4Animation、中央球權與物理負責實際結果。" );
            BasketballInspectorGUI.Property(serializedObject, "decisionInterval", "高階決策間隔 (s)", "只快取傳球等離散選擇；移動與接球目標仍每幀追蹤。" );
            BasketballInspectorGUI.Property(serializedObject, "preferredShotDistance", "偏好投籃距離 (m)", "持球者進入此距離後會先面向正確籃框，再提交 Shoot intent。" );
            BasketballInspectorGUI.Property(serializedObject, "shotFacingDot", "AI 出手面向門檻", "越接近 1，AI 必須更正面朝向籃框才會投籃。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumShotAlignTime", "AI 最長瞄準時間 (s)", "避免角色因模型轉向緩慢而永久卡在瞄準。" );
            BasketballInspectorGUI.Property(serializedObject, "driveStopDistance", "推進停止距離 (m)", "持球推進時保留在籃框前的目標距離。" );
            BasketballInspectorGUI.Property(serializedObject, "defenseSpacing", "防守站位距離 (m)", "防守者站在對位球員與己方防守籃框之間的距離。" );
            BasketballInspectorGUI.Property(serializedObject, "stealAttemptDistance", "搶球嘗試距離 (m)", "只有對位持球者與球進入此距離才提出 Steal intent；成功仍需真實手球接觸。" );
            BasketballInspectorGUI.Property(serializedObject, "minimumStealFacingDot", "搶球最低面向", "防守者必須大致面向球，避免背對持球者仍伸手穿過身體。" );
            BasketballInspectorGUI.Property(serializedObject, "stealAttemptDuration", "單次伸手時間 (s)", "AI 的 Steal 是短脈衝，不會貼近後永久維持搶球姿勢。" );
            BasketballInspectorGUI.Property(serializedObject, "stealAttemptCooldown", "兩次抄球冷卻 (s)", "失敗後必須重新站位與等待暴露窗口；提高可降低連續亂拍。" );
            BasketballInspectorGUI.Property(serializedObject, "protectBallPressureDistance", "開始護球距離 (m)", "最近防守者進入此距離後，持球者把運球側移到防守者相反方向。" );
            BasketballInspectorGUI.Property(serializedObject, "protectBallControlStrength", "護球側向控制強度", "只使用原模型 Ball Control 產生換手／側向運球，不直接移動實體球。" );
            BasketballInspectorGUI.Property(serializedObject, "protectedHandSwitchInterval", "護球換手間隔 (s)", "防守者換側或間隔到期時允許改變運球手，避免固定單側暴露。" );
            BasketballInspectorGUI.Property(serializedObject, "passPressureDistance", "受壓迫傳球距離 (m)", "最近防守者進入此距離時，持球 AI 會優先評估隊友。" );
            BasketballInspectorGUI.Property(serializedObject, "actionCooldown", "離散動作冷卻 (s)", "限制連續傳球／投籃決策，避免狀態反覆切換。" );
            BasketballInspectorGUI.Property(serializedObject, "minimumPossessionSecondsBeforePass", "接球後最短持球時間 (s)", "AI 接到球後至少觀察與推進這段時間才可再次傳球，避免拿到球立刻互傳。" );
            BasketballInspectorGUI.Property(serializedObject, "returnPassLockoutSeconds", "禁止立即回傳時間 (s)", "接球後暫時排除上一位持球者，避免兩名球員來回乒乓傳球。" );
            BasketballInspectorGUI.Property(serializedObject, "minimumOpenPassGain", "無壓迫傳球收益門檻", "沒有壓迫時必須有明顯更好的推進、空間與傳球線才會傳。" );
            BasketballInspectorGUI.Property(serializedObject, "minimumPressuredPassGain", "受壓迫傳球收益門檻", "被貼防時可接受較低收益，但仍不會隨便把球傳出去。" );
            BasketballInspectorGUI.Property(serializedObject, "defenseSprintDistance", "防守加速距離 (m)", "距離正確防守站位超過此值時使用 Sprint 追人。" );
            BasketballInspectorGUI.Property(serializedObject, "offBallSprintDistance", "無球跑位加速距離 (m)", "距離空切／拉開位置超過此值時使用 Sprint。" );
            BasketballInspectorGUI.Property(serializedObject, "playerAvoidanceRadius", "Player Avoidance Radius／避碰半徑 (m)", "Off-ball、切入、退防與一般防守 steering 會在此半徑內互相讓位；不影響接球、攔截或 Loose Ball 的真實接觸。" );
            BasketballInspectorGUI.Property(serializedObject, "playerAvoidanceStrength", "Player Avoidance Strength／避碰強度", "提高會更積極分開重疊路徑；過高可能使目標附近左右擺動。" );
            BasketballInspectorGUI.Property(serializedObject, "courtBoundaryMargin", "Court Boundary Margin／場內邊界 (m)", "AI 目標會限制在球場邊線內此距離，避免規則式路徑把角色帶出場。" );
            BasketballInspectorGUI.Property(serializedObject, "helpDefenseDepth", "Help Defense Depth／協防深度 (m)", "三人以上模式由一名 Off-ball Defender 移到持球者與籃框之間的協防位置。" );
            BasketballInspectorGUI.Property(serializedObject, "cutLaneOffset", "Cut Lane Offset／空切橫向偏移 (m)", "三人以上模式只分配一名 Cutter，從持球者反側切入禁區，避免多人同時空切。" );
            BasketballInspectorGUI.Property(serializedObject, "recoveryPredictionHorizon", "Recovery Prediction Horizon／落點預測上限 (s)", "預測 ShotFlight、Loose Ball 與傳球攔截的最長物理時間；球撞框或籃板後會在下一個決策幀重新預測。" );
            BasketballInspectorGUI.Property(serializedObject, "rollingBallLeadTime", "Rolling Ball Lead Time／滾球提前量 (s)", "球已接近地面時，不追舊座標，而是依目前平面速度預留短距離提前量。" );
            BasketballInspectorGUI.Property(serializedObject, "recoveryRunSpeed", "Recovery Run Speed／追球估計速度 (m/s)", "只用於比較誰能先到落點及攔截點；不直接改變 AI4Animation 的實際移動速度。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptTrajectorySamples", "Intercept Trajectory Samples／攔截取樣數", "沿 Rigidbody 拋物線檢查可達攔截點。提高會更精細，但每次團隊決策需要更多純數學取樣。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptReactionTime", "Intercept Reaction Time／攔截反應時間 (s)", "防守者開始移動前預留的反應延遲；提高會減少不合理的瞬間攔截。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptArrivalSlack", "Intercept Arrival Slack／攔截容許時間 (s)", "球員抵達時間可晚於取樣點的容許量；過大會讓不可達的防守者也嘗試攔截。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptReachRadius", "Intercept Reach Radius／攔截伸手半徑 (m)", "估算角色不用將 Root 完全移到球正下方仍可接觸球的水平範圍。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumInterceptHeight", "Maximum Intercept Height／最高攔截高度 (m)", "高於此球心高度的傳球不視為目前角色可攔截，避免追逐頭頂上方的高拋球。" );
            BasketballInspectorGUI.Property(serializedObject, "drawDecisionGizmos", "顯示團隊決策 Gizmos", "選取 BasketballMatch 時顯示角色顏色、追球目標與防守對位連線。只影響 Editor 視覺化。" );
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(BasketballRulesManager))]
    public sealed class BasketballRulesManagerEditor : UnityEditor.Editor
    {
        private bool showTiming = true;
        private bool showCourt = true;
        private bool showHandling = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Rules／籃球規則管理",
                "掛在外層 BasketballMatch。違例一律透過中央球權重開，不會直接改個別 Player 的 Carrier；1v1、3v3、5v5 共用同一套規則。" );
            BasketballInspectorGUI.Property(serializedObject, "enableRules", "Enable Rules／啟用規則", "總開關。關閉只停用違例仲裁，不改 AI4Animation 或物理設定。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceOutOfBounds", "Out of Bounds／出界", "球心離開正式 28 x 15 m 場地後，由最後觸球隊的對手發球。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceShotClock", "Shot Clock／24 秒", "同隊傳球不重設；換球權 24 秒，進攻籃板 14 秒。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceBackcourt", "Backcourt／8 秒與回場", "8 秒內需進入前場；建立前場球權後不可帶回後場。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceOffensiveThreeSeconds", "Three Seconds／進攻三秒", "進攻球員不能長時間停留在對方禁區；AI 會在判罰前主動退出。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceTraveling", "Traveling／走步", "只對明確 Hold 且持球移動過遠採保守判定，避免誤判模型正常運球。" );
            BasketballInspectorGUI.Property(serializedObject, "enforceDoubleDribble", "Double Dribble／二次運球", "明確停球後再次出現落地運球接觸才判定。" );

            if (BasketballInspectorGUI.Section(ref showTiming, "Timing／時間限制"))
            {
                BasketballInspectorGUI.Property(serializedObject, "shotClockSeconds", "Shot Clock／進攻時間 (s)", "FIBA 預設 24 秒。" );
                BasketballInspectorGUI.Property(serializedObject, "offensiveReboundShotClockSeconds", "Offensive Rebound／進攻籃板重設 (s)", "FIBA 預設 14 秒。" );
                BasketballInspectorGUI.Property(serializedObject, "backcourtSeconds", "Backcourt Advance／推進前場 (s)", "FIBA 預設 8 秒。" );
                BasketballInspectorGUI.Property(serializedObject, "offensiveLaneSeconds", "Paint Limit／禁區停留 (s)", "FIBA 預設 3 秒。" );
                BasketballInspectorGUI.Property(serializedObject, "violationRestartDelay", "Violation Pause／違例重開延遲 (s)", "先顯示違例並凍結死球，再交給對手發球，避免運球途中無提示瞬移換手。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showCourt, "Court Detection／場地判定"))
            {
                BasketballInspectorGUI.Property(serializedObject, "outOfBoundsTolerance", "Boundary Tolerance／出界容差 (m)", "避免浮點誤差在白線附近反覆觸發。" );
                BasketballInspectorGUI.Property(serializedObject, "midcourtTolerance", "Midcourt Tolerance／中線容差 (m)", "球需明確跨過此距離才建立前場或判回場。" );
                BasketballInspectorGUI.Property(serializedObject, "paintWidth", "Paint Width／禁區寬 (m)", "FIBA 預設 4.9 m。" );
                BasketballInspectorGUI.Property(serializedObject, "paintDepth", "Paint Depth／禁區深 (m)", "從底線向場內的禁區深度，預設 5.8 m。" );
                BasketballInspectorGUI.Property(serializedObject, "paintExitWarningSeconds", "AI Exit Warning／AI 提前退出 (s)", "距三秒違例剩餘此時間時，規則 AI 會改走禁區外。" );
                BasketballInspectorGUI.Property(serializedObject, "inboundPlayerInset", "Inbound Safety Inset／重開安全內縮 (m)", "選擇發球員與重開位置時保留可達空間。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showHandling, "Ball Handling／持球違例"))
            {
                BasketballInspectorGUI.Property(serializedObject, "heldBallConfirmationSeconds", "Held Confirmation／停球確認 (s)", "Hold 需持續此時間才視為已停止運球。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumHeldTravelDistance", "Held Travel／停球可移動距離 (m)", "保守替代步數辨識；降低會更嚴格，但較可能誤判。" );
                BasketballInspectorGUI.Property(serializedObject, "dribbleGroundContactThreshold", "Ground Contact／運球落地門檻", "停球後重新跨過此 Ball Contact 值才判二次運球。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && Application.isPlaying)
            {
                BasketballRulesManager rules = (BasketballRulesManager)target;
                BasketballInspectorGUI.ReadOnly("Shot Clock／進攻時間", $"{rules.ShotClockRemaining:F1} s");
                BasketballInspectorGUI.ReadOnly("Backcourt／後場時間", $"{rules.BackcourtRemaining:F1} s");
                BasketballInspectorGUI.ReadOnly("Last Violation／最近違例", rules.LastViolation.ToString());
            }
        }
    }

    [CustomEditor(typeof(BasketballCourt))]
    public sealed class BasketballCourtEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Court／比賽球場",
                "FIBA 場地尺寸、兩隊進攻籃框與兩／三分判定的唯一場地資料來源。長軸已和 BasketballDemo 的 Z 軸移動慣例對齊。");
            BasketballInspectorGUI.Property(serializedObject, "courtLength", "球場長度 (m)", "FIBA 全場長度，預設 28 公尺。" );
            BasketballInspectorGUI.Property(serializedObject, "courtWidth", "球場寬度 (m)", "FIBA 全場寬度，預設 15 公尺。" );
            BasketballInspectorGUI.Property(serializedObject, "threePointRadius", "三分弧半徑 (m)", "從籃框中心投影到地面後量測的 FIBA 三分弧半徑。" );
            BasketballInspectorGUI.Property(serializedObject, "threePointCornerDistance", "底角三分距離 (m)", "底角直線區段使用的籃框水平距離。" );
            BasketballInspectorGUI.Property(serializedObject, "positiveZHoop", "+Z 進攻籃框", "預設由 Team 0 進攻；場景座標約為 Z=+12.463。" );
            BasketballInspectorGUI.Property(serializedObject, "negativeZHoop", "-Z 進攻籃框", "預設由 Team 1 進攻；場景座標約為 Z=-12.463。" );
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(BasketballCourtBoundary))]
    public sealed class BasketballCourtBoundaryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Court Boundary／球場邊界",
                "四面不可見的實體牆同時阻擋 Rigidbody 籃球與神經角色 Root。牆面沒有 Renderer，不會遮住球場畫面。");
            BasketballInspectorGUI.Property(serializedObject, "courtLength", "Court Length／球場長度 (m)", "沿球場長軸的可活動長度；預設為 FIBA 28 公尺。" );
            BasketballInspectorGUI.Property(serializedObject, "courtWidth", "Court Width／球場寬度 (m)", "沿邊線方向的可活動寬度；預設為 FIBA 15 公尺。" );
            BasketballInspectorGUI.Property(serializedObject, "wallThickness", "Wall Thickness／牆厚 (m)", "增加厚度可降低高速球穿透；內側邊緣仍維持在正式球場線上。" );
            BasketballInspectorGUI.Property(serializedObject, "wallHeight", "Wall Height／牆高 (m)", "預設 12 公尺，可攔住一般投籃、傳球與反彈球。" );
            BasketballInspectorGUI.Property(serializedObject, "wallBottom", "Wall Bottom／牆底高度 (m)", "略低於地板，避免球從牆底縫隙穿出。" );
            BasketballInspectorGUI.Property(serializedObject, "positiveLengthWall", "Baseline Positive／正長軸底線牆", "球場正長軸端的 BoxCollider。" );
            BasketballInspectorGUI.Property(serializedObject, "negativeLengthWall", "Baseline Negative／負長軸底線牆", "球場負長軸端的 BoxCollider。" );
            BasketballInspectorGUI.Property(serializedObject, "positiveWidthWall", "Sideline Positive／正寬軸邊線牆", "球場正寬軸端的 BoxCollider。" );
            BasketballInspectorGUI.Property(serializedObject, "negativeWidthWall", "Sideline Negative／負寬軸邊線牆", "球場負寬軸端的 BoxCollider。" );
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Loose Ball Recovery／邊緣球回收", EditorStyles.boldLabel);
            BasketballInspectorGUI.Property(serializedObject, "ball", "Ball／實體籃球", "可留空；Play Mode 會自動尋找場上的 BasketballBallController。" );
            BasketballInspectorGUI.Property(serializedObject, "ballRecoveryInset", "Recovery Inset／回收內縮距離 (m)", "低速球進入邊界帶後，會以物理力平滑推回這個可達區域。" );
            BasketballInspectorGUI.Property(serializedObject, "minimumReachableInset", "Minimum Reachable Inset／最小可達內縮 (m)", "即使舊 Prefab 保留較小數值，也確保球不會停在角色碰撞體無法靠近的牆邊。" );
            BasketballInspectorGUI.Property(serializedObject, "ballRecoveryMaximumHeight", "Recovery Max Height／最高回收高度 (m)", "只處理貼地與低彈跳球，避免干擾正常投籃或空中傳球。" );
            BasketballInspectorGUI.Property(serializedObject, "ballRecoveryMaximumSpeed", "Recovery Max Speed／最高回收速度 (m/s)", "高速飛行球只由牆面碰撞處理；減速後才啟用平滑回場。" );
            BasketballInspectorGUI.Property(serializedObject, "ballReturnAcceleration", "Return Acceleration／回場加速度 (m/s²)", "邊緣低速球朝場內移動的物理加速度。" );
            BasketballInspectorGUI.Property(serializedObject, "ballReturnMaximumSpeed", "Return Max Speed／回場最高速度 (m/s)", "限制自動回場速度，避免看起來像突然被吸走。" );
            BasketballInspectorGUI.Property(serializedObject, "ballReturnPositionSpeed", "Position Recovery Speed／位置回收速度 (m/s)", "低速球卡在牆或籃架附近時，每個物理步進允許的有限內移速度。" );
            BasketballInspectorGUI.Property(serializedObject, "hardRecoveryOverflow", "Hard Overflow／逃逸容錯距離 (m)", "球穿出實體牆超過此距離才觸發最後一道位置救援。" );
            BasketballInspectorGUI.Property(serializedObject, "hardRecoveryBelowCourt", "Below Court Recovery／落板下救援距離 (m)", "球掉到地板下方超過此距離時，送回最近可玩位置。" );
            if (serializedObject.ApplyModifiedProperties())
            {
                ((BasketballCourtBoundary)target).RefreshColliders();
            }
        }
    }

    [CustomEditor(typeof(BasketballHoop))]
    [CanEditMultipleObjects]
    public sealed class BasketballHoopEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Hoop／進攻籃框",
                "保存攻框隊伍、3.05 m 籃框中心、籃板與投籃瞄準偏移。球離手後不再追蹤或導引。");
            BasketballInspectorGUI.Property(serializedObject, "attackedByTeamId", "進攻此框的隊伍 ID", "只有相同 Team ID 的出手才會記為該隊得分。" );
            BasketballInspectorGUI.Property(serializedObject, "rimCenter", "籃框中心", "計算投籃軌跡與由上往下穿框判定的中心點。" );
            BasketballInspectorGUI.Property(serializedObject, "backboard", "籃板", "保留給後續擦板投籃目標與 Debug 顯示。" );
            BasketballInspectorGUI.Property(serializedObject, "rimRadius", "籃框半徑 (m)", "FIBA 內徑約 0.45 m，因此預設半徑 0.225 m。" );
            BasketballInspectorGUI.Property(serializedObject, "aimOffset", "投籃瞄準偏移", "相對籃框中心的一次性目標偏移；不會在飛行中持續修正球。" );
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1)
            {
                BasketballHoop hoop = (BasketballHoop)target;
                BasketballInspectorGUI.ReadOnly("世界籃框中心", hoop.Center.ToString("F3"));
                BasketballInspectorGUI.ReadOnly("世界投籃目標", hoop.AimPoint.ToString("F3"));
            }
        }
    }

    [CustomEditor(typeof(BasketballScoreTracker))]
    public sealed class BasketballScoreTrackerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Score／進球與得分",
                "只在 ShotFlight 中偵測球由籃框上方向下穿越；兩／三分使用離手位置，不使用落地位置。");
            BasketballInspectorGUI.Property(serializedObject, "court", "球場資料", "提供兩個籃框與三分線判定。" );
            BasketballInspectorGUI.Property(serializedObject, "ball", "唯一比賽球", "取樣物理球的前一個與目前 FixedUpdate 位置。" );
            BasketballInspectorGUI.Property(serializedObject, "possessionManager", "球權管理器", "驗證 ShotFlight、出手球員、隊伍與離手位置。" );
            BasketballInspectorGUI.Property(serializedObject, "scoringCenterRadius", "有效穿框中心半徑 (m)", "必須小於籃框半徑，避免只擦框就計分。" );
            BasketballInspectorGUI.Property(serializedObject, "scoreCooldown", "重複計分冷卻 (s)", "同一球在籃框附近彈跳時避免重複加分。" );
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && Application.isPlaying)
            {
                BasketballScoreTracker tracker = (BasketballScoreTracker)target;
                BasketballInspectorGUI.ReadOnly(
                    "目前比分",
                    $"TEAM A {tracker.TeamZeroScore} : {tracker.TeamOneScore} TEAM B");
            }
        }
    }

    [CustomEditor(typeof(BasketballWorldEventStream))]
    public sealed class BasketballWorldEventStreamEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball World Events／世界事件流",
                "固定容量、覆寫最舊資料的 Ring Buffer。供技能結果 Debug、資料匯出與未來外部 RL Policy 使用，不會每幀建立集合。" );
            BasketballInspectorGUI.Property(
                serializedObject,
                "capacity",
                "事件紀錄容量",
                "超過容量後覆寫最舊事件；只在建立或容量變更時配置陣列。" );
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && targets.Length == 1)
            {
                BasketballWorldEventStream stream =
                    (BasketballWorldEventStream)target;
                BasketballInspectorGUI.ReadOnly("目前筆數", stream.Count.ToString());
                BasketballInspectorGUI.ReadOnly("最新序號", stream.LatestSequence.ToString());
                BasketballWorldEvent latest = stream.GetNewest();
                BasketballInspectorGUI.ReadOnly(
                    "最新事件",
                    latest.Sequence > 0
                        ? $"#{latest.Sequence} {latest.Type} / P{latest.ActorPlayerIndex + 1}"
                        : "尚無");
            }
        }
    }

    [CustomEditor(typeof(BasketballRewardTracker))]
    public sealed class BasketballRewardTrackerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Reward／籃球獎勵",
                "只將已完成的 World Event 轉成 Team 與 Player Reward。所有模式共用，不修改 Intent、AI4Animation、球權或球的 Rigidbody。" );
            BasketballInspectorGUI.Property(serializedObject, "eventStream", "World Event Stream／世界事件流", "Reward 的唯一結果來源；不從姿勢或畫面猜測技能是否成功。" );
            BasketballInspectorGUI.Property(serializedObject, "capacity", "Reward Capacity／獎勵容量", "固定容量 Ring Buffer；超出後覆寫最舊 Reward Signal。" );
            BasketballInspectorGUI.Property(serializedObject, "scorePerPointTeamReward", "Score Team Reward／每分團隊獎勵", "每得到 1 分給得分隊的共享獎勵。" );
            BasketballInspectorGUI.Property(serializedObject, "scorePerPointOpponentPenalty", "Score Opponent Penalty／每分對手懲罰", "每失去 1 分給對手隊的共享懲罰，預設與得分獎勵零和。" );
            BasketballInspectorGUI.Property(serializedObject, "scorePerPointActorReward", "Scorer Reward／得分者獎勵", "額外記在實際投籃球員的個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "completedPassTeamReward", "Completed Pass Team／成功傳球團隊", "PassCaught 後給接球方球隊的共享 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "completedPassReceiverReward", "Receiver Reward／接球者獎勵", "成功接球者的個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "completedPassPasserReward", "Passer Reward／傳球者獎勵", "成功傳球者的個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "failedPassTeamPenalty", "Failed Pass Team／傳球失敗團隊", "PassFailed 或準備失敗時給傳球方的共享懲罰。" );
            BasketballInspectorGUI.Property(serializedObject, "failedPassActorPenalty", "Failed Pass Actor／傳球失敗球員", "記在事件 Actor 的個人懲罰。" );
            BasketballInspectorGUI.Property(serializedObject, "missedShotActorPenalty", "Missed Shot Actor／未進球懲罰", "只做很小的個人 shaping，避免 AI 因害怕投失而完全不投。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptionTeamReward", "Interception Team／攔截團隊", "PassIntercepted 給攔截隊的共享獎勵。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptionOpponentPenalty", "Intercepted Team Penalty／被攔截團隊", "給原進攻隊的共享懲罰。" );
            BasketballInspectorGUI.Property(serializedObject, "interceptionActorReward", "Interceptor Reward／攔截者獎勵", "實際完成攔截者的個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "stealTouchActorReward", "Steal Touch Reward／碰球獎勵", "有效 StealTouch 的小額 shaping；不代表已取得球權。" );
            BasketballInspectorGUI.Property(serializedObject, "stealSecureTeamReward", "Secure Steal Team／抄球團隊", "防守者後續真正 Secure 球權才給的團隊獎勵。" );
            BasketballInspectorGUI.Property(serializedObject, "stealSecureOpponentPenalty", "Secure Steal Opponent／失去球權團隊", "原持球隊在抄球完成時得到的共享懲罰。" );
            BasketballInspectorGUI.Property(serializedObject, "stealSecureActorReward", "Secure Steal Actor／抄球者獎勵", "真正 Secure 的球員個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "looseBallPickupTeamReward", "Loose Pickup Team／撿球團隊", "沒有舊持球者目標時完成 Loose Ball Pickup 的共享獎勵。" );
            BasketballInspectorGUI.Property(serializedObject, "looseBallPickupActorReward", "Loose Pickup Actor／撿球者獎勵", "完成 Loose Ball Pickup 的個人 shaping reward。" );
            BasketballInspectorGUI.Property(serializedObject, "rejectedCommandActorPenalty", "Rejected Command／非法命令懲罰", "Policy 提交過期版本、非法目標或不允許技能時的小額個人懲罰。" );
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && targets.Length == 1)
            {
                BasketballRewardTracker tracker = (BasketballRewardTracker)target;
                BasketballInspectorGUI.ReadOnly("Episode／回合", tracker.EpisodeId.ToString());
                BasketballInspectorGUI.ReadOnly("Signals／獎勵訊號", tracker.Count.ToString());
                BasketballInspectorGUI.ReadOnly("Team A Return", tracker.TeamZeroReturn.ToString("0.###"));
                BasketballInspectorGUI.ReadOnly("Team B Return", tracker.TeamOneReturn.ToString("0.###"));
                if (GUILayout.Button("Reset Reward Episode／重設獎勵回合"))
                {
                    tracker.ResetEpisode();
                }
            }
        }
    }

    [CustomEditor(typeof(BasketballSkillTelemetryRecorder))]
    public sealed class BasketballSkillTelemetryRecorderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Skill Telemetry／技能資料匯出",
                "將 event、reward 與固定容量 observation step 寫成 JSON Lines，供外部 Skill Surrogate 或 RL 工具使用。預設關閉，不參與控制、Reward 計算、推論或球物理。" );
            BasketballInspectorGUI.Property(serializedObject, "eventStream", "World Event Stream／世界事件流", "要記錄的固定容量事件來源。與 BasketballMatch 使用同一份事件流。" );
            BasketballInspectorGUI.Property(serializedObject, "rewardTracker", "Reward Tracker／獎勵來源", "記錄事件對應的 Team／Player Reward Signal 與累積 Return。" );
            BasketballInspectorGUI.Property(serializedObject, "recordOnStart", "Record On Start／啟動時記錄", "開啟後進入 Play Mode 會自動建立新的 JSONL；效能量測時建議關閉。" );
            BasketballInspectorGUI.Property(serializedObject, "recordObservationSteps", "Record Observation Steps／記錄狀態步驟", "開啟時額外輸出固定十槽 Player Observation、Active Mask、球權、比分與攻擊籃框。" );
            BasketballInspectorGUI.Property(serializedObject, "observationRate", "Observation Rate／狀態記錄頻率 (Hz)", "只影響資料寫入頻率，不改變 Neural Tick、Render 或團隊決策。" );
            BasketballInspectorGUI.Property(serializedObject, "filePrefix", "File Prefix／檔名前綴", "輸出檔名會附加 UTC 日期與毫秒，避免覆寫舊資料。" );
            BasketballInspectorGUI.Property(serializedObject, "flushEveryEvents", "Flush Every Records／寫入批次", "累積多少 event／reward／step 後 Flush；數值較大可降低磁碟 I/O。" );
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && targets.Length == 1)
            {
                BasketballSkillTelemetryRecorder recorder =
                    (BasketballSkillTelemetryRecorder)target;
                BasketballInspectorGUI.ReadOnly(
                    "Recording State／記錄狀態",
                    recorder.IsRecording ? "RECORDING" : "STOPPED");
                BasketballInspectorGUI.ReadOnly(
                    "Recorded Events／已記錄事件",
                    recorder.RecordedEventCount.ToString());
                BasketballInspectorGUI.ReadOnly(
                    "Recorded Rewards／已記錄獎勵",
                    recorder.RecordedRewardCount.ToString());
                BasketballInspectorGUI.ReadOnly(
                    "Recorded Steps／已記錄狀態",
                    recorder.RecordedStepCount.ToString());
                BasketballInspectorGUI.ReadOnly(
                    "Output Path／輸出路徑",
                    string.IsNullOrEmpty(recorder.OutputPath)
                        ? "尚未建立"
                        : recorder.OutputPath);
                if (recorder.IsRecording)
                {
                    if (GUILayout.Button("停止記錄／Stop Recording"))
                    {
                        recorder.StopRecording();
                    }
                }
                else if (GUILayout.Button("開始新記錄／Start New Recording"))
                {
                    recorder.StartRecording();
                }
            }
        }
    }

    [CustomEditor(typeof(BasketballRuntimeSettings))]
    [CanEditMultipleObjects]
    public sealed class BasketballRuntimeSettingsEditor : UnityEditor.Editor
    {
        private bool showNeural = true;
        private bool showPassing = true;
        private bool showShooting = true;
        private bool showApplication;
        private bool showCamera;
        private bool showCameraCollision;
        private bool showRendering;
        private bool showProfiling;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Runtime Settings／共用執行設定",
                "這是十名球員、GPU Batch、第三人稱相機與 HUD 共用的設定檔。修改資產會同時影響整場比賽。");

            if (BasketballInspectorGUI.Section(
                    ref showNeural,
                    "神經推論與動畫",
                    "GPU 後端固定使用 Unity Inference Engine GPUCompute；這裡只調排程頻率與畫面呈現。"))
            {
                BasketballInspectorGUI.DrawNeuralSettings(serializedObject, true);
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showShooting, "投籃瞄準與物理"))
            {
                BasketballInspectorGUI.DrawShootingSettings(serializedObject);
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showPassing, "傳球物理與提前量"))
            {
                BasketballInspectorGUI.DrawPassingSettings(serializedObject);
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showApplication, "應用程式幀率"))
            {
                BasketballInspectorGUI.Property(serializedObject, "targetFrameRate", "目標畫面 FPS", "-1 代表由平台控制。若 VSync 不為 0，通常由 VSync 優先。" );
                BasketballInspectorGUI.Property(serializedObject, "vSyncCount", "垂直同步間隔", "0 關閉 VSync；1 每次螢幕更新同步；2 每兩次同步。效能比較時請固定設定。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showCamera, "第三人稱相機"))
            {
                BasketballInspectorGUI.Property(serializedObject, "mouseSensitivity", "滑鼠靈敏度", "自由視角旋轉速度。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumPitch", "最低俯仰角 (°)", "相機往下看的角度限制。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumPitch", "最高俯仰角 (°)", "相機往上看的角度限制。" );
                BasketballInspectorGUI.Property(serializedObject, "recenterPitch", "重新置中俯仰角 (°)", "相機重新對準角色時使用的垂直角度。" );
                BasketballInspectorGUI.Property(serializedObject, "rotationSmoothTime", "旋轉平滑時間 (s)", "越大越柔和但延遲越明顯。" );
                BasketballInspectorGUI.Property(serializedObject, "cameraDistance", "預設相機距離 (m)", "開始時與角色的距離。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumCameraDistance", "最近距離 (m)", "滾輪與碰撞收近時的下限。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumCameraDistance", "最遠距離 (m)", "滾輪拉遠時的上限。" );
                BasketballInspectorGUI.Property(serializedObject, "zoomSensitivity", "滾輪縮放靈敏度", "每次滾輪輸入改變距離的幅度。" );
                BasketballInspectorGUI.Property(serializedObject, "cameraTargetSmoothTime", "追蹤目標平滑時間 (s)", "先平滑 render-time 球員 Root，再交給相機。可降低 30 Hz／GPU 回讀節奏造成的細微抖動。" );
                BasketballInspectorGUI.Property(serializedObject, "positionSmoothTime", "跟隨位置平滑時間 (s)", "角色移動時相機跟隨的延遲。" );
                BasketballInspectorGUI.Property(serializedObject, "distanceSmoothTime", "縮放平滑時間 (s)", "滾輪與碰撞距離變化的平滑時間。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showCameraCollision, "相機碰撞與剔除"))
            {
                BasketballInspectorGUI.Property(serializedObject, "cameraCollisionEnabled", "啟用相機碰撞", "避免相機穿牆或穿地。" );
                BasketballInspectorGUI.Property(serializedObject, "cameraCollisionMask", "碰撞 Layer", "相機球形檢測會碰撞的 Layer。不要包含角色自身。" );
                BasketballInspectorGUI.Property(serializedObject, "cameraCollisionRadius", "碰撞球半徑 (m)", "相機碰撞檢測厚度。" );
                BasketballInspectorGUI.Property(serializedObject, "cameraCollisionPadding", "牆面保留距離 (m)", "碰撞後讓相機與牆面保持的額外距離。" );
                BasketballInspectorGUI.Property(serializedObject, "minimumCollisionDistance", "碰撞最近距離 (m)", "即使被遮擋也不讓相機進入角色中心。" );
                BasketballInspectorGUI.Property(serializedObject, "collisionQueryIntervalFrames", "碰撞查詢間隔幀", "1 每幀查詢；2 每兩幀。提高可降 CPU，但快速轉動時可能稍有延遲。" );
                BasketballInspectorGUI.Property(serializedObject, "useOcclusionCulling", "使用 Occlusion Culling", "只有 Scene 已烘焙 Occlusion Data 時才建議開啟。" );
                BasketballInspectorGUI.Property(serializedObject, "allowDynamicResolution", "允許動態解析度", "由 URP／平台依負載調整畫面解析度。" );
                BasketballInspectorGUI.Property(serializedObject, "lockCursorOnPlay", "開始時鎖定滑鼠", "進入 Play Mode 後立即鎖定並隱藏游標。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showRendering, "渲染效能預算"))
            {
                BasketballInspectorGUI.Property(serializedObject, "applyRealtimeShadowBudget", "套用即時陰影預算", "場景開始時關閉超出預算的重複即時陰影燈。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumShadowedDirectionalLights", "方向光陰影數量", "通常只保留一盞主要太陽光陰影。" );
                BasketballInspectorGUI.Property(serializedObject, "maximumShadowedAdditionalLights", "額外燈光陰影數量", "點光源／聚光燈可產生即時陰影的最大數量。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(ref showProfiling, "HUD 與效能取樣"))
            {
                BasketballInspectorGUI.Property(serializedObject, "telemetryRefreshRate", "狀態 HUD 更新率 (Hz)", "神經狀態與接觸顯示每秒更新次數。" );
                BasketballInspectorGUI.Property(serializedObject, "performanceRefreshRate", "效能 HUD 更新率 (Hz)", "FPS、CPU、GPU 與推論時間顯示每秒更新次數。" );
                BasketballInspectorGUI.Property(serializedObject, "controlDiskRefreshRate", "控球圓盤更新率 (Hz)", "右下角 Ball Target 圓盤每秒更新次數。" );
                BasketballInspectorGUI.Property(serializedObject, "frameTimingCaptureInterval", "FrameTiming 取樣間隔幀", "越小更新越即時但 Editor／GPU 取樣成本較高。建議 10～20。" );
            }
            BasketballInspectorGUI.EndSection();

            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(BasketballSentisBatchScheduler))]
    [CanEditMultipleObjects]
    public sealed class BasketballSentisBatchSchedulerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball GPU Inference／GPU 批次推論",
                "固定將十名球員（5v5）打包成一個 Unity Inference Engine GPUCompute Batch。每名球員仍保有獨立 recurrent state。");
            BasketballInspectorGUI.Property(
                serializedObject,
                "modelAsset",
                "Batch ONNX 模型",
                "留空時會載入 Resources/Models/BasketballMoEBatch10。模型必須符合固定 input／batch_output contract。" );
            serializedObject.ApplyModifiedProperties();

            BasketballInspectorGUI.ReadOnly(
                "目前圖形 API",
                SystemInfo.graphicsDeviceType.ToString());
            BasketballInspectorGUI.ReadOnly(
                "Compute Shader",
                SystemInfo.supportsComputeShaders ? "支援" : "不支援");
            EditorGUILayout.HelpBox(
                "推論頻率、姿勢插值與追幀上限不在此元件重複設定；請到 BasketballMatch 的「神經推論與插值」或共用 BasketballRuntimeSettings 調整。",
                MessageType.Info);

            if (targets.Length == 1)
            {
                BasketballSentisBatchScheduler scheduler =
                    (BasketballSentisBatchScheduler)target;
                if (Application.isPlaying)
                {
                    BasketballInspectorGUI.ReadOnly("後端狀態", scheduler.BackendName);
                    BasketballInspectorGUI.ReadOnly(
                        "最近 GPU 往返時間",
                        scheduler.LastRoundTripMilliseconds >= 0f
                            ? $"{scheduler.LastRoundTripMilliseconds:F2} ms"
                            : "暖機中");
                    BasketballInspectorGUI.ReadOnly(
                        "等待 GPU Readback",
                        scheduler.IsReadbackPending ? "是" : "否");
                }
            }

            if (GUILayout.Button("設定 Windows 為 DX12／DirectML（需重啟 Unity）"))
            {
                BasketballSentisProjectSetup.ConfigureDirectML();
            }
            BasketballRuntimeSettings defaults = BasketballRuntimeSettings.LoadDefault();
            BasketballInspectorGUI.SelectObjectButton(defaults, "選取共用 Runtime 設定檔");
        }
    }

    [CustomEditor(typeof(BasketballNeuralController))]
    [CanEditMultipleObjects]
    public sealed class BasketballNeuralControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Neural Player／神經球員",
                "每名球員保存自己的閉迴路狀態；模型權重與 GPU Batch 由 BasketballMatch 共用。常用推論參數請在共用 Runtime 設定檔調整。");
            BasketballInspectorGUI.Property(serializedObject, "rig", "角色 Rig", "26-bone canonical rig、實體球與 Pose applicator。" );
            BasketballInspectorGUI.Property(serializedObject, "inputProvider", "鍵盤滑鼠輸入", "只有目前 Tab 選中的球員會讀取玩家輸入。" );
            BasketballInspectorGUI.Property(serializedObject, "movementCamera", "移動參考相機", "鍵盤移動與角色朝向使用的第三人稱相機。" );
            BasketballInspectorGUI.Property(serializedObject, "collisionMask", "Root 碰撞 Layer", "角色 Root trajectory 用來避免穿牆／穿出場地的 Layer。" );
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1)
            {
                BasketballNeuralController controller = (BasketballNeuralController)target;
                BasketballRuntimeSettings settings = controller.RuntimeSettings;
                BasketballInspectorGUI.ReadOnly(
                    "有效 Neural Tick Rate",
                    $"{controller.NeuralTickRate} Hz");
                BasketballInspectorGUI.SelectObjectButton(settings, "選取共用 Runtime 設定檔");
            }
        }
    }

    [CustomEditor(typeof(BasketballTeamMember))]
    [CanEditMultipleObjects]
    public sealed class BasketballTeamMemberEditor : UnityEditor.Editor
    {
        private bool showReferences;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Roster Member／球員名冊",
                "Team ID 決定隊伍配色與傳球陣營；Jersey Number 可獨立於 Player Index 設定。單位數會自動使用置中的較小版面。" );
            BasketballInspectorGUI.Property(serializedObject, "playerIndex", "球員索引", "GPU Batch、Tab 切換與 HUD 使用的唯一順序。" );
            BasketballInspectorGUI.Property(serializedObject, "teamId", "隊伍 ID", "相同 Team ID 才能互相傳球；外觀會選擇相同 ID 的 Team Appearance Profile。" );
            BasketballInspectorGUI.Property(serializedObject, "jerseyNumber", "球衣背號", "支援 0～99；0～9 會在前胸與背部真正置中，並使用略小字級。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumPower", "球員最大力量", "以隊伍外層設定為 1.0 基準，同時限制傳球與投籃離手速度及 AI 可接受距離。建議 0.8～1.1。" );

            if (targets.Length == 1 && Application.isPlaying)
            {
                BasketballInspectorGUI.ReadOnly(
                    "目前是否上場",
                    ((BasketballTeamMember)target).IsOnCourt ? "是" : "否（GPU Idle Slot）");
            }

            if (BasketballInspectorGUI.Section(ref showReferences, "系統連接（通常由 Builder 自動設定）"))
            {
                BasketballInspectorGUI.Property(serializedObject, "controller", "神經控制器", "canonical 26-bone 閉迴路角色。" );
                BasketballInspectorGUI.Property(serializedObject, "inputProvider", "輸入來源", "目前 Tab 選中時讀取鍵盤滑鼠。" );
                BasketballInspectorGUI.Property(serializedObject, "visualizer", "Debug 視覺化", "顯示 trajectory、series 與模型狀態。" );
                BasketballInspectorGUI.Property(serializedObject, "indicator", "選取指示器", "目前控制角色與傳球鎖定標記。" );
                BasketballInspectorGUI.Property(serializedObject, "bodyContact", "身體接觸", "阻擋球與球員重疊，並拒絕穿過持球者軀幹的搶球。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(BasketballTeamGroup))]
    [CanEditMultipleObjects]
    public sealed class BasketballTeamGroupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Basketball Team Group／隊伍外層設定",
                "Home／Away 的共用參數集中在此。子 Player Prefab 只保留背號、索引與個人力量差異，方便重建與管理。" );
            
            GUILayout.Space(10);
            GUILayout.Label("快速陣容切換 (Quick Roster Toggle)", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1 vs 1"))
            {
                ApplyRosterLimit(1);
            }
            if (GUILayout.Button("3 vs 3"))
            {
                ApplyRosterLimit(3);
            }
            if (GUILayout.Button("5 vs 5"))
            {
                ApplyRosterLimit(5);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            BasketballInspectorGUI.Property(serializedObject, "teamId", "隊伍 ID", "同隊傳球與攻守方向使用的穩定識別碼。" );
            BasketballInspectorGUI.Property(serializedObject, "displayName", "隊伍名稱", "Hierarchy、UI 與未來外部 AI 可讀的隊伍名稱。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumPassReleaseSpeed", "隊伍基準最大傳球速度 (m/s)", "Power=1.0 球員的物理離手上限；較弱或較強球員由 Player 的 Maximum Power 乘上此值。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumShotReleaseSpeed", "隊伍基準最大投籃速度 (m/s)", "限制超遠距離大力出奇蹟；超出能力的球會以最大力量出手並自然投短。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumEffectivePassDistance", "AI 最大有效傳球距離 (m)", "規則 AI 不會主動選擇超出此距離的隊友。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumEffectiveShotDistance", "AI 最大有效投籃距離 (m)", "規則 AI 不會主動在能力範圍外投籃。" );
            serializedObject.ApplyModifiedProperties();
        }

        private void ApplyRosterLimit(int count)
        {
            foreach (var t in targets)
            {
                BasketballTeamGroup group = (BasketballTeamGroup)t;
                var members = group.GetComponentsInChildren<BasketballTeamMember>(true);
                System.Array.Sort(members, (a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
                for (int i = 0; i < members.Length; i++)
                {
                    members[i].gameObject.SetActive(i < count);
                }
            }

            BasketballMatchController match = FindObjectOfType<BasketballMatchController>();
            if (match != null)
            {
                Undo.RecordObject(match, "Apply Roster Limit");
                SerializedObject matchSo = new SerializedObject(match);
                var matchModeProp = matchSo.FindProperty("matchMode");
                if (matchModeProp != null)
                {
                    matchModeProp.enumValueIndex = count == 1 ? (int)BasketballMatchMode.OneOnOne :
                                                   count == 3 ? (int)BasketballMatchMode.ThreeOnThree :
                                                   count == 5 ? (int)BasketballMatchMode.FiveOnFive :
                                                   (int)BasketballMatchMode.Custom;
                    matchSo.ApplyModifiedProperties();
                }
            }
        }
    }

    [CustomEditor(typeof(BasketballHumanoidVisualRetargeter))]
    [CanEditMultipleObjects]
    public sealed class BasketballHumanoidVisualRetargeterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Humanoid Visual Retargeter／Humanoid 套皮",
                "神經模型仍控制 canonical 26-bone rig；此元件只把 render pose 套到 Humanoid。肢段方向依關節位置求解，避免不同骨軸造成手臂與手腕扭曲。" );
            BasketballInspectorGUI.Property(serializedObject, "humanoidAnimator", "Humanoid Animator", "必須使用有效 Humanoid Avatar；不需要 Animator Controller，也不使用 Root Motion。" );
            BasketballInspectorGUI.Property(serializedObject, "scaleMultiplier", "模型縮放倍率", "在自動腿長比例上追加的視覺倍率。" );
            BasketballInspectorGUI.Property(serializedObject, "hipsWorldOffset", "骨盆視覺偏移", "只調整套皮骨盆，不改 canonical Root 或神經狀態。" );
            BasketballInspectorGUI.Property(serializedObject, "alignLimbsFromJointPositions", "解剖式肢段對齊", "手臂與腿以 canonical 關節位置決定朝向，建議保持開啟。" );
            BasketballInspectorGUI.Property(serializedObject, "showCanonicalRigWhenVisualUnavailable", "套皮失效時顯示 Debug 骨架", "Animator／Avatar／必要骨骼缺失時，自動隱藏錯誤套皮並恢復原始 mannequin。" );
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && Application.isPlaying)
            {
                BasketballHumanoidVisualRetargeter retargeter =
                    (BasketballHumanoidVisualRetargeter)target;
                BasketballInspectorGUI.ReadOnly(
                    "套皮狀態",
                    retargeter.IsReady ? "Ready" : "Canonical Fallback");
            }
        }
    }

    [CustomEditor(typeof(BasketballHumanoidHandContactSolver))]
    [CanEditMultipleObjects]
    public sealed class BasketballHumanoidHandContactSolverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Humanoid Hand Contact／Humanoid 手球貼合",
                "只修正可視 Humanoid 的雙骨手臂與掌心；依模型 Left/Right Hand Contact 把掌心錨點放到實際球面，不修改 canonical pose、球權或球物理。" );
            BasketballInspectorGUI.Property(serializedObject, "retargeter", "Humanoid 套皮來源", "提供有效 Humanoid Animator 與已重定向的手臂姿勢。" );
            BasketballInspectorGUI.Property(serializedObject, "enableHandBallContact", "啟用手球接觸修正", "關閉可直接比較純神經重定向結果。" );
            BasketballInspectorGUI.Property(serializedObject, "contactStart", "開始修正 Contact", "低於此模型接觸值時完全不介入。" );
            BasketballInspectorGUI.Property(serializedObject, "contactFull", "完整修正 Contact", "達到此值時使用完整球面貼合；中間平滑混合。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumBallDistance", "最大啟用距離 (m)", "實體球離掌心太遠時禁止修正，避免遠距離吸手。" );
            BasketballInspectorGUI.Property(serializedObject, "palmClearance", "掌心離球面間隙 (m)", "正值讓掌面稍微離開 Collider，降低蒙皮厚度造成的穿模。" );
            BasketballInspectorGUI.Property(serializedObject, "palmCenterAlongMiddleFinger", "掌心錨點比例", "從 Wrist 到中指根部的比例；手掌模型不同時可微調。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumWristCorrection", "手腕最大位移 (m)", "限制 presentation IK 修正，避免模型 Contact 異常時手臂瞬移。" );
            BasketballInspectorGUI.Property(serializedObject, "maximumPalmRotationCorrection", "掌心最大旋轉修正 (deg)", "限制掌心為了朝向球面所增加的旋轉；降低可避免拍球時手腕過度拐彎。" );
            BasketballInspectorGUI.Property(serializedObject, "contactBlendSpeed", "接觸混合速度", "提高會更快貼球；降低會更柔和。" );
            BasketballInspectorGUI.Property(serializedObject, "applyRelaxedFingerPose", "啟用程序化手指姿勢", "模型只輸出手腕、不輸出 15 根手指骨；開啟後未接觸時維持自然放鬆，接觸時逐漸包覆球面。" );
            BasketballInspectorGUI.Property(serializedObject, "relaxedFingerCurl", "放鬆手指彎曲 (deg)", "沒有持球或手部 Contact 時的基本彎曲量，避免角色一直張開五指。" );
            BasketballInspectorGUI.Property(serializedObject, "contactFingerCurl", "接觸手指彎曲 (deg)", "手部 Contact 達到完整權重時的彎曲量；只影響可視手指，不修改神經狀態。" );
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && Application.isPlaying)
            {
                BasketballHumanoidHandContactSolver solver =
                    (BasketballHumanoidHandContactSolver)target;
                BasketballInspectorGUI.ReadOnly(
                    "左／右修正權重",
                    $"{solver.LeftWeight:F2} / {solver.RightWeight:F2}");
            }
        }
    }

    [CustomEditor(typeof(BasketballPlayerAppearance))]
    [CanEditMultipleObjects]
    public sealed class BasketballPlayerAppearanceEditor : UnityEditor.Editor
    {
        private bool showRenderers;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Player Appearance／球員外觀",
                "從 BasketballTeamMember 讀取隊伍與背號，以共享材質＋MaterialPropertyBlock 套用顏色，不會為每位球員複製材質。" );
            BasketballInspectorGUI.Property(serializedObject, "teamMember", "名冊來源", "留空時會自動尋找父物件的 BasketballTeamMember。" );
            BasketballInspectorGUI.Property(serializedObject, "teamProfiles", "隊伍外觀設定檔", "依 Team ID 選擇球衣、球褲與背號顏色。" );
            BasketballInspectorGUI.Property(serializedObject, "skinVariant", "膚色版本覆寫", "-1 依 Player Index 穩定分配；0 以上指定 Skin Materials 索引。" );
            BasketballInspectorGUI.Property(serializedObject, "useRosterJerseyNumber", "使用名冊背號", "開啟時使用 BasketballTeamMember 的 Jersey Number。" );
            BasketballInspectorGUI.Property(serializedObject, "jerseyNumberOverride", "背號覆寫", "只有關閉「使用名冊背號」時才生效。" );

            if (BasketballInspectorGUI.Section(ref showRenderers, "模型 Renderer 連接"))
            {
                BasketballInspectorGUI.Property(serializedObject, "bodyRenderer", "身體 Renderer", "套用共用膚色材質。" );
                BasketballInspectorGUI.Property(serializedObject, "jerseyRenderer", "球衣 Renderer", "由 Team Appearance Profile 套用主色。" );
                BasketballInspectorGUI.Property(serializedObject, "shortsRenderer", "球褲 Renderer", "由 Team Appearance Profile 套用副色。" );
                BasketballInspectorGUI.Property(serializedObject, "numberDisplay", "背號顯示", "前後各自含雙位數與置中單位數的 skinned patches。" );
                BasketballInspectorGUI.Property(serializedObject, "skinMaterials", "共用膚色材質", "所有球員共用，不會在 Runtime Instantiate Material。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && GUILayout.Button("立即套用外觀／Apply Appearance"))
            {
                ((BasketballPlayerAppearance)target).Apply();
                SceneView.RepaintAll();
            }
        }
    }

    [CustomEditor(typeof(BasketballTeamAppearanceProfile))]
    [CanEditMultipleObjects]
    public sealed class BasketballTeamAppearanceProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BasketballInspectorGUI.Header(
                "Team Appearance Profile／隊伍外觀設定",
                "可重用的隊伍配色資產。把同一 Profile 指派給所有同隊球員即可保持一致。" );
            BasketballInspectorGUI.Property(serializedObject, "teamId", "隊伍 ID", "需與 BasketballTeamMember 的 Team ID 相同。" );
            BasketballInspectorGUI.Property(serializedObject, "displayName", "隊伍名稱", "只用於編輯器與後續 HUD 顯示。" );
            BasketballInspectorGUI.Property(serializedObject, "jerseyColor", "球衣顏色", "Team primary color。" );
            BasketballInspectorGUI.Property(serializedObject, "shortsColor", "球褲顏色", "Team secondary color。" );
            BasketballInspectorGUI.Property(serializedObject, "numberColor", "背號顏色", "透過 MaterialPropertyBlock 套到共享背號材質。" );
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                BasketballPlayerAppearance[] appearances = Object.FindObjectsByType<
                    BasketballPlayerAppearance>(
                    FindObjectsInactive.Include);
                for (int index = 0; index < appearances.Length; index++)
                {
                    appearances[index].Apply();
                }
                SceneView.RepaintAll();
            }
        }
    }
}
