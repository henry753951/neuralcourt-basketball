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
                "降低速度會讓遠距離傳球必須提高弧線；提高速度可讓球路更平，但接球窗口會更短。建議先維持 8。" );
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
            Property(
                settings,
                "forcedPassReleaseSeconds",
                "強制離手截止時間 (s)",
                "預訓練模型未產生理想離手姿勢時，經過多久仍交給物理。太短容易從低運球點出手，太長會讓操作延遲。" );
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
        private bool showPassControls = true;
        private bool showInference = true;
        private bool showPassingPhysics;

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
                BasketballInspectorGUI.Property(serializedObject, "players", "球員清單", "必須依 Player Index 排列。目前 GPU Batch 固定需要十名球員（5v5）。" );
                BasketballInspectorGUI.Property(serializedObject, "ball", "共用比賽球", "所有球權與物理狀態共用的唯一實體球。" );
                BasketballInspectorGUI.Property(serializedObject, "possessionManager", "球權管理器", "唯一球權、傳球、接球、攔截與搶球仲裁來源。" );
                BasketballInspectorGUI.Property(serializedObject, "viewCamera", "遊戲攝影機", "用於畫面方向、傳球目標選擇與 UI。" );
                BasketballInspectorGUI.Property(serializedObject, "orbitCamera", "第三人稱環繞相機", "滑鼠控制的自由視角元件。" );
                BasketballInspectorGUI.Property(serializedObject, "hud", "UI Toolkit HUD", "遊戲內狀態、控制圓盤與效能資訊。" );
                BasketballInspectorGUI.Property(serializedObject, "sentisBatchScheduler", "GPU 推論排程器", "可留空；執行時會在 BasketballMatch 上自動建立並設定三人 Batch。" );
                BasketballInspectorGUI.Property(serializedObject, "runtimeSettings", "共用 Runtime 設定檔", "十名球員、相機、GPU 排程與 HUD 共用的設定資產。" );
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showPassControls,
                    "滑鼠傳球操作",
                    "Ctrl + 滑鼠左鍵按住達確認時間後傳球；短點為假動作。"))
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
                    "passCommitSeconds",
                    "按住多久確認傳球 (s)",
                    "小於此時間放開會視為假動作。降低可讓傳球更靈敏，但較容易誤觸。" );
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "passTimeoutSeconds",
                    "傳球準備逾時 (s)",
                    "等待 ML 傳球姿勢與離手的最長時間。超時會取消，不會把球卡在準備狀態。" );
                BasketballInspectorGUI.Property(
                    serializedObject,
                    "passFakeDuration",
                    "假動作維持時間 (s)",
                    "短點左鍵後，讓模型保持傳球準備姿勢多少時間。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();

            SerializedProperty settingsProperty = serializedObject.FindProperty("runtimeSettings");
            BasketballRuntimeSettings settingsAsset = settingsProperty != null
                ? settingsProperty.objectReferenceValue as BasketballRuntimeSettings
                : null;
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
                BasketballInspectorGUI.Property(serializedObject, "players", "參與球權仲裁的球員", "所有可能持球、接球、攔截與搶球的球員。" );
                BasketballInspectorGUI.Property(serializedObject, "ball", "唯一實體球", "由 NeuralPossession 或 Rigidbody Physics 擇一控制。" );
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
            }
            BasketballInspectorGUI.EndSection();

            if (BasketballInspectorGUI.Section(
                    ref showSteal,
                    "搶球與拍球",
                    "先形成 Touch，之後才可能 Secure 球權；碰到球不會立刻把 Owner 瞬移給防守者。"))
            {
                BasketballInspectorGUI.Property(serializedObject, "stealHandDistance", "有效手球接觸距離 (m)", "防守者手掌必須進入此距離才可能拍掉球。" );
                BasketballInspectorGUI.Property(serializedObject, "stealInteractionRadius", "搶球互動半徑 (m)", "防守者 Root 必須在持球者附近，才啟用搶球品質判定。" );
                BasketballInspectorGUI.Property(serializedObject, "stealQualityThreshold", "拍球成功品質門檻", "距離、暴露程度與手部接近速度的綜合最低分數。越低越容易拍掉。" );
                BasketballInspectorGUI.Property(serializedObject, "cleanStealQuality", "乾淨抄球品質門檻", "達到較高品質時，球會更偏向防守者並進入可 Secure 的 contested 狀態。" );
                BasketballInspectorGUI.Property(serializedObject, "cleanStealHandDistance", "乾淨抄球手部距離 (m)", "除了品質外，手掌還必須更靠近球。" );
                BasketballInspectorGUI.Property(serializedObject, "stealSecureDelay", "碰球後最短控制延遲 (s)", "防止第一次 Touch 的同一幀就直接取得球權。" );
                BasketballInspectorGUI.Property(serializedObject, "stealSecureWindow", "搶球控制窗口 (s)", "拍球後防守者可在這段時間內形成穩定控制。" );
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
            }
        }
    }

    [CustomEditor(typeof(BasketballRuntimeSettings))]
    [CanEditMultipleObjects]
    public sealed class BasketballRuntimeSettingsEditor : UnityEditor.Editor
    {
        private bool showNeural = true;
        private bool showPassing = true;
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
                "players",
                "Batch 球員（固定 10 人）",
                "順序必須與 BasketballMatch 的球員清單一致。少於或多於十人都不會啟動。" );
            BasketballInspectorGUI.Property(
                serializedObject,
                "modelAsset",
                "Batch ONNX 模型",
                "留空時會載入 Resources/Models/BasketballMoEBatch10。模型必須符合固定 input／batch_output contract。" );
            serializedObject.ApplyModifiedProperties();

            SerializedProperty players = serializedObject.FindProperty("players");
            if (players != null && players.arraySize != BasketballSentisBatchScheduler.BatchSize)
            {
                EditorGUILayout.HelpBox(
                    $"GPU Batch 必須剛好有 {BasketballSentisBatchScheduler.BatchSize} 名球員；目前是 {players.arraySize}。",
                    MessageType.Error);
            }

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

            if (BasketballInspectorGUI.Section(ref showReferences, "系統連接（通常由 Builder 自動設定）"))
            {
                BasketballInspectorGUI.Property(serializedObject, "controller", "神經控制器", "canonical 26-bone 閉迴路角色。" );
                BasketballInspectorGUI.Property(serializedObject, "inputProvider", "輸入來源", "目前 Tab 選中時讀取鍵盤滑鼠。" );
                BasketballInspectorGUI.Property(serializedObject, "visualizer", "Debug 視覺化", "顯示 trajectory、series 與模型狀態。" );
                BasketballInspectorGUI.Property(serializedObject, "indicator", "選取指示器", "目前控制角色與傳球鎖定標記。" );
            }
            BasketballInspectorGUI.EndSection();
            serializedObject.ApplyModifiedProperties();
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
            BasketballInspectorGUI.Property(serializedObject, "wristRotationLimit", "手腕最大旋轉 (°)", "限制不同骨軸造成的極端 wrist twist；需要更多控球手腕動作時可小幅提高。" );
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
