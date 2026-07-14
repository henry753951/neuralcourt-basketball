using System;
using System.Collections.Generic;
using System.IO;
using CrowdEyes.AI4Animation.Basketball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CrowdEyes.AI4Animation.Editor
{
    public static class BasketballReferenceSceneBuilder
    {
        private const string BuildRequestFile = "Library/BuildBasketballDemo.request";
        private const string OpenRequestFile = "Library/OpenBasketballDemo.request";
        private const string LegacyScenePath =
            "Assets/AI4AnimationRemake/LegacySource/BasketballDemo.Legacy.unity";

        private const string OutputScenePath =
            "Assets/Scenes/BasketballDemo.unity";

        private const string TemporaryScenePath =
            "Assets/Scenes/BasketballDemo.Building.unity";

        private const string TemporarySceneDataPath =
            "Assets/Scenes/BasketballDemo.Building";

        private const string ModelPath =
            "Assets/AI4AnimationRemake/Models/BasketballModel.asset";

        private const string MaterialFolder =
            "Assets/AI4AnimationRemake/Materials";

        private const string HudLayoutPath =
            "Assets/AI4AnimationRemake/UI/BasketballHUD.uxml";

        private const string HudStylePath =
            "Assets/AI4AnimationRemake/UI/BasketballHUD.uss";

        private const string HudPanelSettingsPath =
            "Assets/AI4AnimationRemake/UI/BasketballPanelSettings.asset";

        // Rebuilds the parity scene and its required visual compatibility pieces.
        [MenuItem("AI4Animation/Build Basketball Reference Scene")]
        public static void Build()
        {
            Lightmapping.Cancel();
            BasketballModelAsset model = AssetDatabase.LoadAssetAtPath<BasketballModelAsset>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException($"Missing model at {ModelPath}.");
            }

            if (!model.Validate(out string modelReason))
            {
                throw new InvalidOperationException(modelReason);
            }

            Scene targetScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(targetScene, TemporaryScenePath))
            {
                throw new InvalidOperationException($"Failed to save temporary scene {TemporaryScenePath}.");
            }
            Scene sourceScene = EditorSceneManager.OpenScene(LegacyScenePath, OpenSceneMode.Additive);

            try
            {
                SceneManager.SetActiveScene(targetScene);

                GameObject world = CloneRoot(sourceScene, targetScene, "World");
                GameObject[] playerObjects =
                {
                    CloneRoot(sourceScene, targetScene, "Player"),
                    CloneRoot(sourceScene, targetScene, "Player"),
                    CloneRoot(sourceScene, targetScene, "Player")
                };
                GameObject ball = CloneRoot(sourceScene, targetScene, "Ball");
                GameObject cameraRoot = CloneRoot(sourceScene, targetScene, "Camera");
                GameObject canvas = CloneRoot(sourceScene, targetScene, "Canvas");
                GameObject eventSystemRoot = CloneRoot(sourceScene, targetScene, "EventSystem");

                RemoveMissingScripts(world);
                for (int index = 0; index < playerObjects.Length; index++)
                {
                    RemoveMissingScripts(playerObjects[index]);
                }
                RemoveMissingScripts(ball);
                RemoveMissingScripts(cameraRoot);
                RemoveMissingScripts(canvas);
                RemoveMissingScripts(eventSystemRoot);

                Object.DestroyImmediate(canvas);
                GameObject uiRoot = new("BasketballUI");
                SceneManager.MoveGameObjectToScene(uiRoot, targetScene);

                // Keep the legacy physics layers: character, ball, UI, and world
                // must remain separated for the original 4097 collision mask.

                Material black = GetOrCreateMaterial("ReferenceBlack.mat", Color.black, 0.1f, 0.25f);
                Material grey = GetOrCreateMaterial(
                    "ReferenceGrey.mat",
                    new Color(0.78431374f, 0.78431374f, 0.78431374f, 1f),
                    0.1f,
                    0.25f);
                Material court = GetOrCreateMaterial(
                    "ReferenceCourt.mat",
                    new Color(0.5882353f, 0.5882353f, 0.5882353f, 1f),
                    0.25f,
                    0f);
                Material gold = GetOrCreateMaterial(
                    "ReferenceGold.mat",
                    new Color(0.85490197f, 0.64705884f, 0.1254902f, 1f),
                    0f,
                    0.5f);
                Material opponent = GetOrCreateMaterial(
                    "ReferenceOpponent.mat",
                    new Color(0.24f, 0.045f, 0.055f, 1f),
                    0.1f,
                    0.3f);

                AssignMaterial(playerObjects[0], black);
                AssignMaterial(playerObjects[1], black);
                AssignMaterial(playerObjects[2], opponent);
                AssignWorldMaterials(world, grey, court);
                AssignMaterial(ball, gold);
                ConfigureBallVisual(ball, gold, 0.125f);

                BasketballBallController ballController = ball.GetComponent<BasketballBallController>();
                if (ballController == null)
                {
                    ballController = ball.AddComponent<BasketballBallController>();
                }

                ballController.Configure(0.125f, BasketballBallAuthorityState.Controlled);
                Rigidbody ballBody = ball.GetComponent<Rigidbody>();
                ballBody.mass = 0.62369f;
                ballBody.interpolation = RigidbodyInterpolation.Interpolate;
                ballBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                PhysicsMaterial ballMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                    "Assets/AI4AnimationRemake/Models/Ball.physicMaterial");
                Collider ballCollider = ball.GetComponentInChildren<Collider>();
                if (ballCollider != null && ballMaterial != null)
                {
                    ballCollider.sharedMaterial = ballMaterial;
                }

                Camera camera = cameraRoot.GetComponentInChildren<Camera>(true);
                if (camera == null)
                {
                    throw new InvalidOperationException("Legacy Camera root does not contain a Camera component.");
                }
                camera.gameObject.tag = "MainCamera";
                BasketballLegacyCamera legacyCamera = camera.gameObject.GetComponent<BasketballLegacyCamera>();
                if (legacyCamera == null)
                {
                    legacyCamera = camera.gameObject.AddComponent<BasketballLegacyCamera>();
                }
                legacyCamera.Configure(playerObjects[0].transform);
                legacyCamera.enabled = false;
                ThirdPersonOrbitCamera orbitCamera =
                    camera.gameObject.GetComponent<ThirdPersonOrbitCamera>();
                if (orbitCamera == null)
                {
                    orbitCamera = camera.gameObject.AddComponent<ThirdPersonOrbitCamera>();
                }
                orbitCamera.Configure(playerObjects[0].transform);
                orbitCamera.enabled = true;

                Vector3 basePlayerPosition = playerObjects[0].transform.position;
                Vector3[] playerOffsets =
                {
                    Vector3.zero,
                    new Vector3(-3f, 0f, 2.5f),
                    new Vector3(3f, 0f, 2.5f)
                };
                int[] teams = { 0, 0, 1 };
                var members = new BasketballTeamMember[playerObjects.Length];
                // Switching scenes can unload an otherwise unreferenced ScriptableObject.
                // Reload once here, then share the same immutable model asset across all agents.
                model = AssetDatabase.LoadAssetAtPath<BasketballModelAsset>(ModelPath);
                if (model == null)
                {
                    throw new InvalidOperationException($"Missing model at {ModelPath}.");
                }
                for (int index = 0; index < playerObjects.Length; index++)
                {
                    GameObject player = playerObjects[index];
                    player.name = $"Player {index + 1}";
                    player.transform.position = basePlayerPosition + playerOffsets[index];
                    members[index] = ConfigurePlayer(
                        player,
                        index,
                        teams[index],
                        model,
                        ballController,
                        camera);
                }

                BasketballUIToolkitController uiController = ConfigureUIToolkit(
                    uiRoot,
                    members[0].Controller,
                    members[0].InputProvider,
                    members[0].Visualizer);
                GameObject matchRoot = new("BasketballMatch");
                SceneManager.MoveGameObjectToScene(matchRoot, targetScene);
                BasketballPossessionManager possessionManager =
                    matchRoot.AddComponent<BasketballPossessionManager>();
                possessionManager.Configure(members, ballController);
                BasketballMatchController matchController =
                    matchRoot.AddComponent<BasketballMatchController>();
                matchController.Configure(
                    members,
                    ballController,
                    possessionManager,
                    camera,
                    orbitCamera,
                    uiController);
                ConfigureEventSystem(eventSystemRoot);

                EditorSceneManager.MarkSceneDirty(targetScene);
                if (!EditorSceneManager.SaveScene(targetScene, OutputScenePath))
                {
                    throw new InvalidOperationException($"Failed to save {OutputScenePath}.");
                }
                EnsureSceneInBuildSettings(OutputScenePath);

                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, removeScene: true);
                }
                EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Single);
                AssetDatabase.DeleteAsset(TemporaryScenePath);

                Debug.Log(
                    $"Built {OutputScenePath}: three 26-bone neural players, two teams, shared-ball " +
                    "possession, teammate pass targeting, opponent steals, orbit camera, and UI Toolkit HUD.");
            }
            finally
            {
                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, removeScene: true);
                }
                AssetDatabase.DeleteAsset(TemporaryScenePath);
                if (AssetDatabase.IsValidFolder(TemporarySceneDataPath))
                {
                    AssetDatabase.DeleteAsset(TemporarySceneDataPath);
                }
            }
        }

        private static BasketballTeamMember ConfigurePlayer(
            GameObject player,
            int playerIndex,
            int teamId,
            BasketballModelAsset model,
            BasketballBallController ballController,
            Camera camera)
        {
            BasketballSkeleton skeleton = player.GetComponent<BasketballSkeleton>();
            if (skeleton == null)
            {
                skeleton = player.AddComponent<BasketballSkeleton>();
            }

            var bones = new Transform[BasketballSkeleton.BoneCount];
            Transform[] transforms = player.GetComponentsInChildren<Transform>(true);
            for (int boneIndex = 0; boneIndex < BasketballSkeleton.BoneCount; boneIndex++)
            {
                bones[boneIndex] = FindByName(
                    transforms,
                    BasketballSkeleton.CanonicalNames[boneIndex]);
                if (bones[boneIndex] == null)
                {
                    throw new InvalidOperationException(
                        $"Player {playerIndex + 1} is missing canonical bone " +
                        $"'{BasketballSkeleton.CanonicalNames[boneIndex]}'.");
                }
            }
            skeleton.Configure(bones);
            if (!skeleton.Validate(out string skeletonReason))
            {
                throw new InvalidOperationException(skeletonReason);
            }

            BasketballReferenceRig rig = player.GetComponent<BasketballReferenceRig>();
            if (rig == null)
            {
                rig = player.AddComponent<BasketballReferenceRig>();
            }
            rig.Configure(model, skeleton, ballController);
            if (!rig.Validate(out string rigReason))
            {
                throw new InvalidOperationException(rigReason);
            }

            BasketballKeyboardMouseInputProvider inputProvider =
                player.GetComponent<BasketballKeyboardMouseInputProvider>();
            if (inputProvider == null)
            {
                inputProvider = player.AddComponent<BasketballKeyboardMouseInputProvider>();
            }
            inputProvider.SetMode(BasketballKeyboardMouseInputProvider.InputMode.Keyboard);

            BasketballNeuralController neuralController =
                player.GetComponent<BasketballNeuralController>();
            if (neuralController == null)
            {
                neuralController = player.AddComponent<BasketballNeuralController>();
            }
            neuralController.Configure(rig, inputProvider, camera);

            LineRenderer lineRenderer = player.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = player.AddComponent<LineRenderer>();
            }
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            BasketballDebugVisualizer visualizer =
                player.GetComponent<BasketballDebugVisualizer>();
            if (visualizer == null)
            {
                visualizer = player.AddComponent<BasketballDebugVisualizer>();
            }
            visualizer.Configure(neuralController);

            BasketballDebugHUD legacyHud = player.GetComponent<BasketballDebugHUD>();
            if (legacyHud != null)
            {
                Object.DestroyImmediate(legacyHud);
            }

            BasketballTargetIndicator indicator =
                player.GetComponent<BasketballTargetIndicator>();
            if (indicator == null)
            {
                indicator = player.AddComponent<BasketballTargetIndicator>();
            }
            BasketballTeamMember member = player.GetComponent<BasketballTeamMember>();
            if (member == null)
            {
                member = player.AddComponent<BasketballTeamMember>();
            }
            member.Configure(
                playerIndex,
                teamId,
                neuralController,
                inputProvider,
                visualizer,
                indicator);
            return member;
        }

        [InitializeOnLoadMethod]
        private static void BuildWhenRequested()
        {
            EditorApplication.delayCall += ProcessBuildRequest;
        }

        private static void ProcessBuildRequest()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string openRequestPath = Path.Combine(projectRoot, OpenRequestFile);
            string requestPath = Path.Combine(projectRoot, BuildRequestFile);
            if (!File.Exists(requestPath) && !File.Exists(openRequestPath))
            {
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ProcessBuildRequest;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += ProcessBuildRequest;
                return;
            }

            if (File.Exists(openRequestPath))
            {
                File.Delete(openRequestPath);
                EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Single);
                return;
            }

            File.Delete(requestPath);
            Build();
        }

        private static GameObject CloneRoot(Scene source, Scene target, string name)
        {
            GameObject[] roots = source.GetRootGameObjects();
            GameObject sourceRoot = Array.Find(roots, item => item.name == name);
            if (sourceRoot == null)
            {
                throw new InvalidOperationException($"Legacy scene root '{name}' was not found.");
            }

            GameObject clone = Object.Instantiate(sourceRoot);
            clone.name = sourceRoot.name;
            SceneManager.MoveGameObjectToScene(clone, target);
            return clone;
        }

        private static void RemoveMissingScripts(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[i].gameObject);
            }
        }

        private static Transform FindByName(Transform[] transforms, string name)
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = layer;
            }
        }

        private static void AssignMaterial(GameObject root, Material material)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        private static void ConfigureBallVisual(GameObject ball, Material material, float radius)
        {
            MeshRenderer rootRenderer = ball.GetComponent<MeshRenderer>();
            MeshFilter rootFilter = ball.GetComponent<MeshFilter>();
            if (rootRenderer != null)
            {
                Object.DestroyImmediate(rootRenderer);
            }
            if (rootFilter != null)
            {
                Object.DestroyImmediate(rootFilter);
            }

            Transform existing = ball.transform.Find("Ball Visual");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Ball Visual";
            visual.transform.SetParent(ball.transform, false);
            visual.transform.localScale = Vector3.one * (2f * radius);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void AssignWorldMaterials(GameObject world, Material grey, Material court)
        {
            Renderer[] renderers = world.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                string objectName = renderers[i].gameObject.name;
                renderers[i].sharedMaterial = objectName.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0
                    ? court
                    : grey;
            }
        }

        private static Material GetOrCreateMaterial(
            string fileName,
            Color color,
            float metallic,
            float smoothness)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{fileName}";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            material.color = color;
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static BasketballUIToolkitController ConfigureUIToolkit(
            GameObject uiRoot,
            BasketballNeuralController neuralController,
            BasketballKeyboardMouseInputProvider inputProvider,
            BasketballDebugVisualizer visualizer)
        {
            VisualTreeAsset layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudLayoutPath);
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(HudStylePath);
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelSettingsPath);
            if (layout == null)
            {
                throw new InvalidOperationException($"Missing UI Toolkit layout at {HudLayoutPath}.");
            }
            if (panelSettings == null)
            {
                throw new InvalidOperationException(
                    $"Missing UI Toolkit panel settings at {HudPanelSettingsPath}.");
            }
            if (styleSheet == null)
            {
                throw new InvalidOperationException($"Missing UI Toolkit style at {HudStylePath}.");
            }

            UIDocument document = uiRoot.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;
            document.sortingOrder = 100;

            BasketballUIToolkitController uiController =
                uiRoot.AddComponent<BasketballUIToolkitController>();
            uiController.Configure(neuralController, inputProvider, visualizer, styleSheet);
            return uiController;
        }

        private static void ConfigureEventSystem(GameObject eventSystemRoot)
        {
            StandaloneInputModule legacyModule = eventSystemRoot.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                Object.DestroyImmediate(legacyModule);
            }
            if (eventSystemRoot.GetComponent<EventSystem>() == null)
            {
                eventSystemRoot.AddComponent<EventSystem>();
            }
            if (eventSystemRoot.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystemRoot.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == scenePath)
                {
                    scenes[index] = new EditorBuildSettingsScene(scenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
