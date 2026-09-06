using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class BasketballCameraMonitorPanel : MonoBehaviour
    {
        private VisualElement root;
        private VisualElement cameraList;
        private Image preview;
        private Label selectedLabel;
        private Label statusLabel;
        private Button closeButton;
        private IntegerField widthField;
        private IntegerField heightField;
        private FloatField fxField;
        private FloatField fyField;
        private FloatField cxField;
        private FloatField cyField;
        private FloatField nearField;
        private FloatField farField;
        private FloatField fpsField;
        private IntegerField qualityField;
        private FloatField positionXField;
        private FloatField positionYField;
        private FloatField positionZField;
        private FloatField rotationXField;
        private FloatField rotationYField;
        private FloatField rotationZField;

        private BasketballMonitorCamera selectedCamera;
        private ThirdPersonOrbitCamera playerCamera;
        private bool isVisible;
        private bool actionsBound;
        private bool bindingFields;

        private void OnEnable()
        {
            BasketballMonitorCamera.RegistryChanged += RefreshCameraList;
            TryBind();
            SetVisible(false);
        }

        private void Start()
        {
            TryBind();
            SetVisible(false);
        }

        private void OnDisable()
        {
            BasketballMonitorCamera.RegistryChanged -= RefreshCameraList;
            UnbindActions();
            SetVisible(false);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                SetVisible(!isVisible);
            }
        }

        private void TryBind()
        {
            if (root != null)
            {
                return;
            }
            UIDocument document = GetComponent<UIDocument>();
            root = document != null ? document.rootVisualElement : null;
            if (root == null)
            {
                return;
            }

            cameraList = root.Q<VisualElement>("camera-list");
            preview = root.Q<Image>("camera-preview");
            selectedLabel = root.Q<Label>("selected-camera-label");
            statusLabel = root.Q<Label>("status-label");
            closeButton = root.Q<Button>("close-main-ui");
            widthField = root.Q<IntegerField>("image-width");
            heightField = root.Q<IntegerField>("image-height");
            fxField = root.Q<FloatField>("fx");
            fyField = root.Q<FloatField>("fy");
            cxField = root.Q<FloatField>("cx");
            cyField = root.Q<FloatField>("cy");
            nearField = root.Q<FloatField>("near-clip");
            farField = root.Q<FloatField>("far-clip");
            fpsField = root.Q<FloatField>("capture-fps");
            qualityField = root.Q<IntegerField>("jpeg-quality");
            positionXField = root.Q<FloatField>("position-x");
            positionYField = root.Q<FloatField>("position-y");
            positionZField = root.Q<FloatField>("position-z");
            rotationXField = root.Q<FloatField>("rotation-x");
            rotationYField = root.Q<FloatField>("rotation-y");
            rotationZField = root.Q<FloatField>("rotation-z");

            VisualElement renderPage = root.Q<VisualElement>("page-global-render");
            VisualElement globalControls = root.Q<VisualElement>(className: "global-controls");
            Button renderTab = root.Q<Button>("header-tab-render");
            if (renderPage != null) renderPage.style.display = DisplayStyle.None;
            if (globalControls != null) globalControls.style.display = DisplayStyle.None;
            if (renderTab != null) renderTab.style.display = DisplayStyle.None;

            BindActions();
            playerCamera = FindAnyObjectByType<ThirdPersonOrbitCamera>();
            RefreshCameraList();
        }

        private void BindActions()
        {
            if (actionsBound || closeButton == null)
            {
                return;
            }
            actionsBound = true;
            closeButton.clicked += Close;
            root.Q<Button>("apply-intrinsics").clicked += ApplySettings;
            root.Q<Button>("validate-intrinsics").clicked += ValidateSettings;
            root.Q<Button>("reset-intrinsics").clicked += BindSelectedCamera;
            root.Q<Button>("look-at-center").clicked += LookAtCenter;
            root.Q<Button>("look-at-ball").clicked += LookAtBall;
            root.Q<Button>("reset-transform").clicked += ResetTransform;
            positionXField.RegisterValueChangedCallback(TransformFieldChanged);
            positionYField.RegisterValueChangedCallback(TransformFieldChanged);
            positionZField.RegisterValueChangedCallback(TransformFieldChanged);
            rotationXField.RegisterValueChangedCallback(TransformFieldChanged);
            rotationYField.RegisterValueChangedCallback(TransformFieldChanged);
            rotationZField.RegisterValueChangedCallback(TransformFieldChanged);
        }

        private void UnbindActions()
        {
            if (!actionsBound || closeButton == null)
            {
                return;
            }
            closeButton.clicked -= Close;
            actionsBound = false;
        }

        private void Close() => SetVisible(false);

        public void SetVisible(bool visible)
        {
            TryBind();
            isVisible = visible && root != null;
            if (root != null)
            {
                root.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (isVisible)
            {
                // The monitor menu never locks the mouse and never changes Free Cam state.
                playerCamera?.SetCursorLocked(false);
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                RefreshCameraList();
                ActivateSelectedPreview();
            }
            else
            {
                DeactivateAllPreviews();
                if (preview != null) preview.image = null;
                // Deliberately do not restore CursorLockMode here. The player can click
                // the Game view to resume mouse-look, so closing M never traps the cursor.
            }
        }

        private void RefreshCameraList()
        {
            if (cameraList == null)
            {
                return;
            }
            cameraList.Clear();
            IReadOnlyList<BasketballMonitorCamera> cameras = BasketballMonitorCamera.Registered;
            for (int index = 0; index < cameras.Count; index++)
            {
                BasketballMonitorCamera camera = cameras[index];
                if (camera == null || !camera.gameObject.activeInHierarchy)
                {
                    continue;
                }

                VisualElement row = new();
                row.AddToClassList("camera-row");
                Button select = new(() => SelectCamera(camera))
                {
                    text = $"{camera.DisplayName}\n{camera.PreviewWidth} x {camera.PreviewHeight}  ·  {camera.PreviewFps:0.#} FPS"
                };
                select.AddToClassList("camera-select");
                select.EnableInClassList("selected", camera == selectedCamera);
                row.Add(select);
                cameraList.Add(row);
            }

            if (selectedCamera == null || !selectedCamera.gameObject.activeInHierarchy)
            {
                selectedCamera = cameras.Count > 0 ? cameras[0] : null;
            }
            if (selectedCamera != null)
            {
                BindSelectedCamera();
            }
            else
            {
                selectedLabel.text = "Camera Group 中沒有可用攝影機";
                statusLabel.text = "請在群組下新增 Camera";
            }
        }

        private void SelectCamera(BasketballMonitorCamera camera)
        {
            if (selectedCamera != camera)
            {
                selectedCamera?.SetPreviewActive(false);
                selectedCamera = camera;
            }
            BindSelectedCamera();
            RefreshCameraList();
            ActivateSelectedPreview();
        }

        private void ActivateSelectedPreview()
        {
            if (!isVisible || selectedCamera == null)
            {
                return;
            }
            DeactivateAllPreviews();
            selectedCamera.SetPreviewActive(true);
            preview.image = selectedCamera.PreviewTexture;
        }

        private static void DeactivateAllPreviews()
        {
            IReadOnlyList<BasketballMonitorCamera> cameras = BasketballMonitorCamera.Registered;
            for (int index = 0; index < cameras.Count; index++)
            {
                cameras[index]?.SetPreviewActive(false);
            }
        }

        private void BindSelectedCamera()
        {
            if (selectedCamera == null)
            {
                return;
            }
            bindingFields = true;
            selectedCamera.GetIntrinsics(out float fx, out float fy, out float cx, out float cy);
            Camera camera = selectedCamera.SourceCamera;
            selectedLabel.text = $"{selectedCamera.DisplayName}  ·  {selectedCamera.PreviewWidth} x {selectedCamera.PreviewHeight}";
            widthField.SetValueWithoutNotify(selectedCamera.PreviewWidth);
            heightField.SetValueWithoutNotify(selectedCamera.PreviewHeight);
            fxField.SetValueWithoutNotify(fx);
            fyField.SetValueWithoutNotify(fy);
            cxField.SetValueWithoutNotify(cx);
            cyField.SetValueWithoutNotify(cy);
            nearField.SetValueWithoutNotify(camera.nearClipPlane);
            farField.SetValueWithoutNotify(camera.farClipPlane);
            fpsField.SetValueWithoutNotify(selectedCamera.PreviewFps);
            qualityField.SetValueWithoutNotify(selectedCamera.JpegQuality);
            Vector3 position = selectedCamera.transform.position;
            Vector3 rotation = selectedCamera.transform.eulerAngles;
            positionXField.SetValueWithoutNotify(position.x);
            positionYField.SetValueWithoutNotify(position.y);
            positionZField.SetValueWithoutNotify(position.z);
            rotationXField.SetValueWithoutNotify(rotation.x);
            rotationYField.SetValueWithoutNotify(rotation.y);
            rotationZField.SetValueWithoutNotify(rotation.z);
            bindingFields = false;
            statusLabel.text = "系統就緒";
        }

        private void ApplySettings()
        {
            if (selectedCamera == null)
            {
                return;
            }
            selectedCamera.ApplySettings(
                widthField.value,
                heightField.value,
                fxField.value,
                fyField.value,
                cxField.value,
                cyField.value,
                nearField.value,
                farField.value,
                fpsField.value,
                qualityField.value);
            statusLabel.text = "相機設定已套用";
            ActivateSelectedPreview();
            RefreshCameraList();
        }

        private void ValidateSettings()
        {
            bool valid = widthField.value > 0 && heightField.value > 0 &&
                         fxField.value > 0f && fyField.value > 0f &&
                         nearField.value > 0f && farField.value > nearField.value;
            statusLabel.text = valid ? "相機內參有效" : "相機內參無效";
            statusLabel.EnableInClassList("error", !valid);
        }

        private void TransformFieldChanged(ChangeEvent<float> _)
        {
            if (bindingFields || selectedCamera == null)
            {
                return;
            }
            selectedCamera.transform.SetPositionAndRotation(
                new Vector3(positionXField.value, positionYField.value, positionZField.value),
                Quaternion.Euler(rotationXField.value, rotationYField.value, rotationZField.value));
        }

        private void LookAtCenter()
        {
            LookAt(Vector3.up * 1.2f);
        }

        private void LookAtBall()
        {
            BasketballBallController ball = FindAnyObjectByType<BasketballBallController>();
            if (ball != null)
            {
                LookAt(ball.transform.position);
            }
        }

        private void LookAt(Vector3 target)
        {
            if (selectedCamera == null)
            {
                return;
            }
            Vector3 direction = target - selectedCamera.transform.position;
            if (direction.sqrMagnitude > 1e-6f)
            {
                selectedCamera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                BindSelectedCamera();
            }
        }

        private void ResetTransform()
        {
            selectedCamera?.ResetTransform();
            BindSelectedCamera();
        }
    }
}
