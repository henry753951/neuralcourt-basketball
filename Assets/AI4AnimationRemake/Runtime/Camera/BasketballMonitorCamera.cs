using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BasketballMonitorCamera : MonoBehaviour
    {
        private static readonly List<BasketballMonitorCamera> Registry = new();

        [SerializeField] private string displayName;
        [SerializeField, Min(160)] private int previewWidth = 480;
        [SerializeField, Min(90)] private int previewHeight = 270;
        [SerializeField, Range(1f, 240f)] private float previewFps = 30f;
        [SerializeField, Range(1, 100)] private int jpegQuality = 80;

        private Vector3 resetPosition;
        private Vector3 resetEulerAngles;

        private Camera sourceCamera;
        private RenderTexture previewTexture;

        public static event Action RegistryChanged;
        public static IReadOnlyList<BasketballMonitorCamera> Registered => Registry;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? gameObject.name
            : displayName;
        public Camera SourceCamera => sourceCamera != null
            ? sourceCamera
            : sourceCamera = GetComponent<Camera>();
        public RenderTexture PreviewTexture => previewTexture;
        public int PreviewWidth => previewWidth;
        public int PreviewHeight => previewHeight;
        public float PreviewFps => previewFps;
        public int JpegQuality => jpegQuality;

        private void OnEnable()
        {
            sourceCamera = GetComponent<Camera>();
            sourceCamera.enabled = false;
            resetPosition = transform.position;
            resetEulerAngles = transform.eulerAngles;
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
                Registry.Sort((left, right) =>
                    string.CompareOrdinal(left.DisplayName, right.DisplayName));
                RegistryChanged?.Invoke();
            }
        }

        private void OnDisable()
        {
            SetPreviewActive(false);
            if (Registry.Remove(this))
            {
                RegistryChanged?.Invoke();
            }
            ReleasePreviewTexture();
        }

        private void OnDestroy()
        {
            Registry.Remove(this);
            ReleasePreviewTexture();
        }

        public void SetPreviewActive(bool active)
        {
            Camera camera = SourceCamera;
            if (camera == null)
            {
                return;
            }

            if (active)
            {
                EnsurePreviewTexture();
                camera.targetTexture = previewTexture;
                camera.enabled = true;
            }
            else
            {
                camera.enabled = false;
                camera.targetTexture = null;
            }
        }

        public void ApplySettings(
            int width,
            int height,
            float fx,
            float fy,
            float cx,
            float cy,
            float nearClip,
            float farClip,
            float fps,
            int quality)
        {
            previewWidth = Mathf.Max(160, width);
            previewHeight = Mathf.Max(90, height);
            previewFps = Mathf.Clamp(fps, 1f, 240f);
            jpegQuality = Mathf.Clamp(quality, 1, 100);
            Camera camera = SourceCamera;
            camera.nearClipPlane = Mathf.Max(0.01f, nearClip);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, farClip);
            float safeFy = Mathf.Max(0.001f, fy);
            camera.fieldOfView = Mathf.Clamp(
                2f * Mathf.Atan(previewHeight * 0.5f / safeFy) * Mathf.Rad2Deg,
                1f,
                179f);
            camera.aspect = (float)previewWidth / previewHeight;

            if (camera.enabled)
            {
                EnsurePreviewTexture();
                camera.targetTexture = previewTexture;
            }
        }

        public void GetIntrinsics(
            out float fx,
            out float fy,
            out float cx,
            out float cy)
        {
            float fov = SourceCamera.fieldOfView * Mathf.Deg2Rad;
            fy = previewHeight * 0.5f / Mathf.Tan(fov * 0.5f);
            fx = fy;
            cx = previewWidth * 0.5f;
            cy = previewHeight * 0.5f;
        }

        public void ResetTransform()
        {
            transform.SetPositionAndRotation(
                resetPosition,
                Quaternion.Euler(resetEulerAngles));
        }

        private void EnsurePreviewTexture()
        {
            int width = Mathf.Max(160, previewWidth);
            int height = Mathf.Max(90, previewHeight);
            if (previewTexture != null &&
                previewTexture.width == width && previewTexture.height == height)
            {
                return;
            }

            ReleasePreviewTexture();
            previewTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = $"{DisplayName}_Preview",
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            previewTexture.Create();
        }

        private void ReleasePreviewTexture()
        {
            if (previewTexture == null)
            {
                return;
            }
            if (previewTexture.IsCreated())
            {
                previewTexture.Release();
            }
            if (Application.isPlaying)
            {
                Destroy(previewTexture);
            }
#if UNITY_EDITOR
            else
            {
                DestroyImmediate(previewTexture);
            }
#endif
            previewTexture = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            Registry.Clear();
            RegistryChanged = null;
        }
    }
}
