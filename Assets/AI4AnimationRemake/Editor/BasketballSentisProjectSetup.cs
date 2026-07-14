using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CrowdEyes.AI4Animation.Editor
{
    public static class BasketballSentisProjectSetup
    {
        [MenuItem("AI4Animation/Configure Sentis DirectML (DX12)")]
        public static void ConfigureDirectML()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(
                target,
                new[]
                {
                    GraphicsDeviceType.Direct3D12,
                    GraphicsDeviceType.Direct3D11
                });
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Configured Windows graphics APIs as Direct3D12 first with Direct3D11 fallback. " +
                "Restart the Unity Editor before validating the Sentis DirectML backend.");
        }
    }
}
