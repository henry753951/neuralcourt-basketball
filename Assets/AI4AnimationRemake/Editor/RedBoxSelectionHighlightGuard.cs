using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CrowdEyes.AI4Animation.Editor
{
    /// <summary>
    /// Unity's Scene View selection outline can bloom into a large white patch on the
    /// thin backboard target mesh. Suppress only that editor overlay while a RedBox is
    /// selected; this never changes the material or Game view rendering.
    /// </summary>
    [InitializeOnLoad]
    internal static class RedBoxSelectionHighlightGuard
    {
        private static readonly System.Type AnnotationUtilityType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
        private static readonly PropertyInfo ShowSelectionOutline =
            AnnotationUtilityType?.GetProperty(
                "showSelectionOutline",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ShowSelectionWire =
            AnnotationUtilityType?.GetProperty(
                "showSelectionWire",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool suppressing;
        private static bool previousOutline;
        private static bool previousWire;

        static RedBoxSelectionHighlightGuard()
        {
            Selection.selectionChanged += HandleSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            EditorApplication.quitting += Restore;
            EditorApplication.delayCall += HandleSelectionChanged;
        }

        private static void HandleSelectionChanged()
        {
            bool redBoxSelected = false;
            Object[] selected = Selection.objects;
            for (int index = 0; index < selected.Length; index++)
            {
                GameObject gameObject = selected[index] as GameObject;
                if (gameObject != null && gameObject.name == "RedBox")
                {
                    redBoxSelected = true;
                    break;
                }
            }

            if (redBoxSelected)
            {
                Suppress();
            }
            else
            {
                Restore();
            }
        }

        private static void Suppress()
        {
            if (suppressing || ShowSelectionOutline == null || ShowSelectionWire == null)
            {
                return;
            }

            previousOutline = (bool)ShowSelectionOutline.GetValue(null);
            previousWire = (bool)ShowSelectionWire.GetValue(null);
            ShowSelectionOutline.SetValue(null, false);
            ShowSelectionWire.SetValue(null, false);
            suppressing = true;
            SceneView.RepaintAll();
        }

        private static void Restore()
        {
            if (!suppressing || ShowSelectionOutline == null || ShowSelectionWire == null)
            {
                return;
            }

            ShowSelectionOutline.SetValue(null, previousOutline);
            ShowSelectionWire.SetValue(null, previousWire);
            suppressing = false;
            SceneView.RepaintAll();
        }
    }
}
