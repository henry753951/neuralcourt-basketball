using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    public sealed class BasketballCameraGroup : MonoBehaviour
    {
        [SerializeField] private bool includeInactiveChildren = true;

        private void Awake()
        {
            RegisterChildCameras();
        }

        public void RegisterChildCameras()
        {
            Camera[] cameras = GetComponentsInChildren<Camera>(includeInactiveChildren);
            for (int index = 0; index < cameras.Length; index++)
            {
                Camera camera = cameras[index];
                if (camera == null || camera.GetComponent<ThirdPersonOrbitCamera>() != null)
                {
                    continue;
                }
                if (camera.GetComponent<BasketballMonitorCamera>() == null)
                {
                    camera.gameObject.AddComponent<BasketballMonitorCamera>();
                }
            }
        }
    }
}
