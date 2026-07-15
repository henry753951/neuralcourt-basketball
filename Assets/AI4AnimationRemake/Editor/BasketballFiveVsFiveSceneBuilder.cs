using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrowdEyes.AI4Animation.Basketball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CrowdEyes.AI4Animation.Editor
{
    /// <summary>
    /// Builds the CrowdEyes-Twin visual prefab and expands BasketballDemo from
    /// three canonical neural rigs to a complete 5v5 match.
    /// </summary>
    [InitializeOnLoad]
    public static class BasketballFiveVsFiveSceneBuilder
    {
        private const int TeamSize = 5;
        private const int PlayerCount = TeamSize * 2;
        private const string ScenePath = "Assets/Scenes/BasketballDemo.unity";
        private const string CharacterRoot =
            "Assets/AI4AnimationRemake/Characters/CrowdEyesTwin";
        private const string ModelPath =
            CharacterRoot + "/Models/BSD_Athlete_Male_01.fbx";
        private const string TextureRoot = CharacterRoot + "/Textures";
        private const string MaterialRoot = CharacterRoot + "/Materials";
        private const string AppearanceRoot = CharacterRoot + "/Appearance";
        private const string MeshRoot = CharacterRoot + "/Meshes";
        private const string NumberAtlasPath = MaterialRoot + "/JerseyNumberAtlas.png";
        private const string NumberMaterialPath = MaterialRoot + "/BSD_Jersey_Number_URP.mat";
        private const string BlueProfilePath = AppearanceRoot + "/Team_Blue.asset";
        private const string RedProfilePath = AppearanceRoot + "/Team_Red.asset";
        private const string VisualPrefabPath =
            CharacterRoot + "/BasketballPlayerVisual.prefab";
        private const string OneShotMarker =
            "Assets/AI4AnimationRemake/Editor/BuildBasketballFiveVsFive.once";

        private static readonly Vector3[] Formation =
        {
            new(-5.2f, 0f, -3.8f),
            new(-2.6f, 0f, -1.5f),
            new(0f, 0f, -4.8f),
            new(2.6f, 0f, -1.5f),
            new(5.2f, 0f, -3.8f),
            new(-5.2f, 0f, 3.8f),
            new(-2.6f, 0f, 1.5f),
            new(0f, 0f, 4.8f),
            new(2.6f, 0f, 1.5f),
            new(5.2f, 0f, 3.8f)
        };

        static BasketballFiveVsFiveSceneBuilder()
        {
            EditorApplication.delayCall += TryRunOneShot;
        }

        [MenuItem("Tools/AI4Animation/Build CrowdEyes-Twin 5v5 Basketball Demo")]
        public static void BuildFromMenu()
        {
            BuildActiveBasketballScene();
        }

        private static void TryRunOneShot()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(OneShotMarker) == null ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            try
            {
                BuildActiveBasketballScene();
                AssetDatabase.DeleteAsset(OneShotMarker);
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void BuildActiveBasketballScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before building the 5v5 demo. Active scene: {scene.path}");
            }

            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            bool restoreReadability = importer != null && !importer.isReadable;
            if (restoreReadability)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            try
            {
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                GameObject visualPrefab = BuildVisualPrefab();
                BasketballTeamMember[] players = BuildPlayers(scene, visualPrefab);
                WireMatch(players);
            }
            finally
            {
                if (restoreReadability)
                {
                    importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                    if (importer != null)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "BasketballDemo now contains two five-player teams using the " +
                "CrowdEyes-Twin Humanoid visual and the shared Batch10 GPU scheduler.");
        }

        private static GameObject BuildVisualPrefab()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException($"Unable to load Humanoid model at {ModelPath}.");
            }

            Material[] skinMaterials =
            {
                CreateLitMaterial(
                    "BSD_Skin_African_URP",
                    TextureRoot + "/Skin_YoungAfricanMale.png",
                    Color.white),
                CreateLitMaterial(
                    "BSD_Skin_Asian_URP",
                    TextureRoot + "/Skin_YoungAsianMale.png",
                    Color.white),
                CreateLitMaterial(
                    "BSD_Skin_Caucasian_URP",
                    TextureRoot + "/Skin_YoungCaucasianMale.png",
                    Color.white),
                CreateLitMaterial(
                    "BSD_Skin_Caucasian2_URP",
                    TextureRoot + "/Skin_YoungCaucasianMale2.png",
                    Color.white)
            };
            Material eyebrows = CreateLitMaterial(
                "BSD_Eyebrows_URP", TextureRoot + "/Eyebrows_004.png", Color.white, true);
            Material eyelashes = CreateLitMaterial(
                "BSD_Eyelashes_URP", TextureRoot + "/Eyelashes_01.png", Color.white, true);
            Material eyes = CreateLitMaterial(
                "BSD_Eyes_URP", TextureRoot + "/Eyes_Brown.png", Color.white);
            Material hair = CreateLitMaterial(
                "BSD_Hair_URP", TextureRoot + "/Hair_Short01.png", Color.white, true);
            Material shoes = CreateLitMaterial(
                "BSD_Shoes_URP", TextureRoot + "/Shoes_Running01.png", Color.white);
            Material teeth = CreateLitMaterial(
                "BSD_Teeth_URP", TextureRoot + "/Teeth_Base.png", Color.white);
            Material jersey = CreateLitMaterial(
                "BSD_Team_Jersey_URP", null, Color.white);
            Material shorts = CreateLitMaterial(
                "BSD_Team_Shorts_URP", null, Color.white);
            Material numberMaterial = BuildJerseyNumberMaterial();
            BasketballTeamAppearanceProfile[] teamProfiles = BuildTeamProfiles();

            GameObject root = new("CrowdEyes Basketball Player Visual");
            try
            {
                GameObject model = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException("Could not instantiate the imported player model.");
                }
                model.name = "BSD_Athlete_Male_01";
                model.transform.SetParent(root.transform, false);
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;

                Animator animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    throw new InvalidOperationException(
                        "BSD_Athlete_Male_01 must import with a valid Humanoid Avatar.");
                }
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;

                Renderer bodyRenderer = null;
                SkinnedMeshRenderer jerseyRenderer = null;
                Renderer shortsRenderer = null;
                foreach (SkinnedMeshRenderer renderer in
                         model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material material = MatchesRenderer(renderer, "Body")
                        ? skinMaterials[0]
                        : MatchesRenderer(renderer, "Eyebrows")
                            ? eyebrows
                            : MatchesRenderer(renderer, "Eyelashes")
                                ? eyelashes
                                : MatchesRenderer(renderer, "Eyes")
                                    ? eyes
                                    : MatchesRenderer(renderer, "Hair")
                                        ? hair
                                        : MatchesRenderer(renderer, "Jersey")
                                            ? jersey
                                            : MatchesRenderer(renderer, "Shoes")
                                                ? shoes
                                                : MatchesRenderer(renderer, "Shorts")
                                                    ? shorts
                                                    : MatchesRenderer(renderer, "Teeth")
                                                        ? teeth
                                                        : null;
                    if (material != null)
                    {
                        renderer.sharedMaterial = material;
                    }
                    renderer.updateWhenOffscreen = false;
                    renderer.allowOcclusionWhenDynamic = true;

                    if (MatchesRenderer(renderer, "Body")) bodyRenderer = renderer;
                    if (MatchesRenderer(renderer, "Jersey")) jerseyRenderer = renderer;
                    if (MatchesRenderer(renderer, "Shorts")) shortsRenderer = renderer;
                }

                if (bodyRenderer == null || jerseyRenderer == null || shortsRenderer == null)
                {
                    throw new InvalidOperationException(
                        "Imported player is missing Body_UnityExport, Jersey, or Shorts renderers.");
                }

                BasketballHumanoidVisualRetargeter retargeter =
                    root.AddComponent<BasketballHumanoidVisualRetargeter>();
                retargeter.Configure(animator);
                BasketballJerseyNumberDisplay numberDisplay =
                    BuildJerseyNumberDisplay(root, jerseyRenderer, numberMaterial);
                BasketballPlayerAppearance appearance =
                    root.AddComponent<BasketballPlayerAppearance>();
                appearance.Configure(
                    bodyRenderer,
                    jerseyRenderer,
                    shortsRenderer,
                    numberDisplay,
                    skinMaterials,
                    teamProfiles);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save visual prefab at {VisualPrefabPath}.");
                }
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private enum NumberPatchLayout
        {
            Left,
            Right,
            CenteredSingle
        }

        private static BasketballTeamAppearanceProfile[] BuildTeamProfiles()
        {
            EnsureFolder(AppearanceRoot);
            BasketballTeamAppearanceProfile blue = CreateTeamProfile(
                BlueProfilePath,
                0,
                "Blue Team",
                new Color(0.035f, 0.18f, 0.92f, 1f),
                new Color(0.01f, 0.035f, 0.2f, 1f));
            BasketballTeamAppearanceProfile red = CreateTeamProfile(
                RedProfilePath,
                1,
                "Red Team",
                new Color(0.88f, 0.035f, 0.025f, 1f),
                new Color(0.22f, 0.012f, 0.008f, 1f));
            return new[] { blue, red };
        }

        private static BasketballTeamAppearanceProfile CreateTeamProfile(
            string path,
            int teamId,
            string displayName,
            Color jersey,
            Color shorts)
        {
            BasketballTeamAppearanceProfile profile =
                AssetDatabase.LoadAssetAtPath<BasketballTeamAppearanceProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<BasketballTeamAppearanceProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.Configure(teamId, displayName, jersey, shorts, Color.white);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static BasketballJerseyNumberDisplay BuildJerseyNumberDisplay(
            GameObject root,
            SkinnedMeshRenderer jerseyRenderer,
            Material material)
        {
            if (jerseyRenderer == null || jerseyRenderer.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    "The CrowdEyes-Twin jersey mesh is required for jersey numbers.");
            }

            BasketballJerseyNumberDisplay display =
                root.AddComponent<BasketballJerseyNumberDisplay>();
            SkinnedMeshRenderer frontTens = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Front Tens", material, true, NumberPatchLayout.Left);
            SkinnedMeshRenderer frontOnes = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Front Ones", material, true, NumberPatchLayout.Right);
            SkinnedMeshRenderer frontSingle = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Front Single", material, true, NumberPatchLayout.CenteredSingle);
            SkinnedMeshRenderer backTens = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Back Tens", material, false, NumberPatchLayout.Left);
            SkinnedMeshRenderer backOnes = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Back Ones", material, false, NumberPatchLayout.Right);
            SkinnedMeshRenderer backSingle = CreateNumberPatch(
                jerseyRenderer, "Jersey Number Back Single", material, false, NumberPatchLayout.CenteredSingle);
            display.Configure(
                frontTens,
                frontOnes,
                frontSingle,
                backTens,
                backOnes,
                backSingle);
            return display;
        }

        private static SkinnedMeshRenderer CreateNumberPatch(
            SkinnedMeshRenderer jerseyRenderer,
            string name,
            Material material,
            bool front,
            NumberPatchLayout layout)
        {
            GameObject patchObject = new(name);
            patchObject.transform.SetParent(jerseyRenderer.transform, false);
            SkinnedMeshRenderer renderer = patchObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = BuildNumberPatchMesh(
                jerseyRenderer.sharedMesh,
                name,
                front,
                layout);
            renderer.sharedMaterial = material;
            renderer.rootBone = jerseyRenderer.rootBone;
            renderer.bones = jerseyRenderer.bones;
            renderer.localBounds = jerseyRenderer.localBounds;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.skinnedMotionVectors = false;
            renderer.updateWhenOffscreen = false;
            return renderer;
        }

        private static Mesh BuildNumberPatchMesh(
            Mesh source,
            string name,
            bool front,
            NumberPatchLayout layout)
        {
            EnsureFolder(MeshRoot);
            string path = $"{MeshRoot}/{name.Replace(' ', '_')}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }

            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            BoneWeight[] sourceWeights = source.boneWeights;
            int[] sourceTriangles = source.triangles;
            List<Vector3> vertices = new();
            List<Vector3> normals = new();
            List<Vector4> tangents = new();
            List<Vector2> uv = new();
            List<BoneWeight> weights = new();
            List<int> triangles = new();
            Dictionary<int, int> vertexMap = new();
            Bounds bounds = source.bounds;
            float digitWidth = bounds.extents.x * 0.62f;
            float gap = bounds.extents.x * 0.035f;
            float xMin;
            float xMax;
            if (layout == NumberPatchLayout.Left)
            {
                xMin = -digitWidth - gap;
                xMax = -gap;
            }
            else if (layout == NumberPatchLayout.Right)
            {
                xMin = gap;
                xMax = digitWidth + gap;
            }
            else
            {
                float centeredWidth = digitWidth * 0.78f;
                xMin = -centeredWidth * 0.5f;
                xMax = centeredWidth * 0.5f;
            }

            float zMin = bounds.center.z - bounds.extents.z * 0.18f;
            float zMax = bounds.center.z + bounds.extents.z * 0.58f;
            if (layout == NumberPatchLayout.CenteredSingle)
            {
                float center = (zMin + zMax) * 0.5f;
                float halfHeight = (zMax - zMin) * 0.41f;
                zMin = center - halfHeight;
                zMax = center + halfHeight;
            }
            float depthThreshold = front
                ? bounds.min.y + bounds.size.y * 0.24f
                : bounds.max.y - bounds.size.y * 0.24f;

            for (int triangleIndex = 0;
                 triangleIndex < sourceTriangles.Length;
                 triangleIndex += 3)
            {
                int a = sourceTriangles[triangleIndex];
                int b = sourceTriangles[triangleIndex + 1];
                int c = sourceTriangles[triangleIndex + 2];
                Vector3 center = (sourceVertices[a] + sourceVertices[b] + sourceVertices[c]) / 3f;
                Vector3 normal = (sourceNormals[a] + sourceNormals[b] + sourceNormals[c]).normalized;
                bool onFacingSurface = front
                    ? center.y <= depthThreshold && normal.y < -0.35f
                    : center.y >= depthThreshold && normal.y > 0.35f;
                if (!onFacingSurface || center.x < xMin || center.x > xMax ||
                    center.z < zMin || center.z > zMax)
                {
                    continue;
                }

                triangles.Add(AddPatchVertex(a));
                triangles.Add(AddPatchVertex(b));
                triangles.Add(AddPatchVertex(c));
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            if (sourceTangents.Length == sourceVertices.Length)
            {
                mesh.SetTangents(tangents);
            }
            mesh.SetUVs(0, uv);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = source.bindposes;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;

            int AddPatchVertex(int sourceIndex)
            {
                if (vertexMap.TryGetValue(sourceIndex, out int existingIndex))
                {
                    return existingIndex;
                }

                Vector3 sourceVertex = sourceVertices[sourceIndex];
                Vector3 sourceNormal = sourceNormals[sourceIndex];
                int newIndex = vertices.Count;
                vertexMap[sourceIndex] = newIndex;
                vertices.Add(sourceVertex + sourceNormal * 0.003f);
                normals.Add(sourceNormal);
                if (sourceTangents.Length == sourceVertices.Length)
                {
                    tangents.Add(sourceTangents[sourceIndex]);
                }
                float horizontal = Mathf.InverseLerp(xMin, xMax, sourceVertex.x);
                if (front)
                {
                    horizontal = 1f - horizontal;
                }
                uv.Add(new Vector2(
                    horizontal,
                    Mathf.InverseLerp(zMin, zMax, sourceVertex.z)));
                weights.Add(sourceWeights[sourceIndex]);
                return newIndex;
            }
        }

        private static Material BuildJerseyNumberMaterial()
        {
            BuildJerseyNumberAtlas();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Unlit shader is unavailable.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(NumberMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "BSD Jersey Number URP" };
                AssetDatabase.CreateAsset(material, NumberMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture(
                "_BaseMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(NumberAtlasPath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.1f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.enableInstancing = true;
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildJerseyNumberAtlas()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(NumberAtlasPath) != null)
            {
                return;
            }

            const int cellWidth = 64;
            const int height = 96;
            Texture2D texture = new(
                cellWidth * 10,
                height,
                TextureFormat.RGBA32,
                true,
                true);
            Color32[] pixels = Enumerable
                .Repeat(new Color32(0, 0, 0, 0), texture.width * texture.height)
                .ToArray();
            string[] glyphs =
            {
                "01110100011001110101110011000101110",
                "00100011000010000100001000010001110",
                "01110100010000100010001000100011111",
                "11110000010000101110000010000111110",
                "00010001100101010010111110001000010",
                "11111100001000011110000010000111110",
                "01110100001000011110100011000101110",
                "11111000010001000100010000100001000",
                "01110100011000101110100011000101110",
                "01110100011000101111000010000101110"
            };
            for (int digit = 0; digit < glyphs.Length; digit++)
            {
                DrawGlyph(
                    pixels,
                    texture.width,
                    digit * cellWidth,
                    cellWidth,
                    height,
                    glyphs[digit]);
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            File.WriteAllBytes(NumberAtlasPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(NumberAtlasPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(NumberAtlasPath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static void DrawGlyph(
            Color32[] pixels,
            int atlasWidth,
            int cellX,
            int cellWidth,
            int cellHeight,
            string glyph)
        {
            const int columns = 5;
            const int rows = 7;
            const int scale = 9;
            int originX = cellX + (cellWidth - columns * scale) / 2;
            int originY = (cellHeight - rows * scale) / 2;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (glyph[row * columns + column] != '1')
                    {
                        continue;
                    }
                    for (int y = -1; y <= scale; y++)
                    {
                        for (int x = -1; x <= scale; x++)
                        {
                            int pixelX = originX + column * scale + x;
                            int pixelY = originY + (rows - 1 - row) * scale + y;
                            bool border = x < 1 || y < 1 ||
                                          x >= scale - 1 || y >= scale - 1;
                            pixels[pixelY * atlasWidth + pixelX] = border
                                ? new Color32(8, 12, 20, 255)
                                : new Color32(245, 240, 225, 255);
                        }
                    }
                }
            }
        }

        private static Material CreateLitMaterial(
            string name,
            string texturePath,
            Color color,
            bool alphaClip = false)
        {
            EnsureFolder(MaterialRoot);
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
            }
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D texture = string.IsNullOrEmpty(texturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.32f);
            material.enableInstancing = true;
            material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.35f);
            if (alphaClip)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = -1;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static bool MatchesRenderer(Renderer renderer, string semanticName)
        {
            if (renderer == null || string.IsNullOrEmpty(renderer.name))
            {
                return false;
            }
            string expectedSuffix = $"_{semanticName}_UnityExport";
            return renderer.name.EndsWith(
                expectedSuffix,
                StringComparison.OrdinalIgnoreCase);
        }

        private static BasketballTeamMember[] BuildPlayers(Scene scene, GameObject visualPrefab)
        {
            List<BasketballTeamMember> players = Object
                .FindObjectsByType<BasketballTeamMember>(
                    FindObjectsInactive.Include)
                .Where(player => player.gameObject.scene == scene)
                .OrderBy(player => player.PlayerIndex)
                .ToList();
            if (players.Count < 1)
            {
                throw new InvalidOperationException("BasketballDemo has no canonical player rig to clone.");
            }
            if (players.Count > PlayerCount)
            {
                throw new InvalidOperationException(
                    $"BasketballDemo already contains {players.Count} players; expected at most {PlayerCount}.");
            }

            GameObject template = players[0].gameObject;
            while (players.Count < PlayerCount)
            {
                GameObject clone = Object.Instantiate(template);
                clone.name = $"Player {players.Count + 1}";
                SceneManager.MoveGameObjectToScene(clone, scene);
                Undo.RegisterCreatedObjectUndo(clone, "Create 5v5 Basketball Player");
                players.Add(clone.GetComponent<BasketballTeamMember>());
            }

            for (int index = 0; index < players.Count; index++)
            {
                BasketballTeamMember player = players[index];
                player.gameObject.name = $"Player {index + 1}";
                int teamId = index < TeamSize ? 0 : 1;
                Quaternion facing = teamId == 0
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 180f, 0f);
                player.transform.SetPositionAndRotation(Formation[index], facing);

                BasketballNeuralController controller =
                    player.GetComponent<BasketballNeuralController>();
                BasketballKeyboardMouseInputProvider input =
                    player.GetComponent<BasketballKeyboardMouseInputProvider>();
                BasketballDebugVisualizer visualizer =
                    player.GetComponent<BasketballDebugVisualizer>();
                BasketballTargetIndicator indicator =
                    player.GetComponent<BasketballTargetIndicator>();
                player.Configure(
                    index,
                    teamId,
                    index + 1,
                    controller,
                    input,
                    visualizer,
                    indicator);

                BasketballHumanoidVisualRetargeter[] visuals =
                    player.GetComponentsInChildren<BasketballHumanoidVisualRetargeter>(true);
                for (int visualIndex = 1; visualIndex < visuals.Length; visualIndex++)
                {
                    Object.DestroyImmediate(visuals[visualIndex].gameObject);
                }
                if (visuals.Length == 0)
                {
                    GameObject visual = PrefabUtility.InstantiatePrefab(
                        visualPrefab,
                        player.transform) as GameObject;
                    if (visual == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not add visual prefab to Player {index + 1}.");
                    }
                    visual.name = "CrowdEyes Basketball Player Visual";
                    visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    visual.transform.localScale = Vector3.one;
                    SetLayerRecursively(visual, player.gameObject.layer);
                }
                BasketballPlayerAppearance appearance =
                    player.GetComponentInChildren<BasketballPlayerAppearance>(true);
                appearance?.Apply();
                EditorUtility.SetDirty(player);
            }
            return players.ToArray();
        }

        private static void WireMatch(BasketballTeamMember[] players)
        {
            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>(FindObjectsInactive.Include);
            BasketballPossessionManager possession =
                Object.FindAnyObjectByType<BasketballPossessionManager>(FindObjectsInactive.Include);
            if (match == null || possession == null)
            {
                throw new InvalidOperationException(
                    "BasketballDemo requires BasketballMatchController and BasketballPossessionManager.");
            }

            SetObjectArray(match, "players", players);
            SetObjectArray(possession, "players", players);
            EditorUtility.SetDirty(match);
            EditorUtility.SetDirty(possession);
        }

        private static void SetObjectArray(
            Object target,
            string propertyName,
            IReadOnlyList<BasketballTeamMember> players)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name}.{propertyName} is not a serialized array.");
            }
            property.arraySize = players.Count;
            for (int index = 0; index < players.Count; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = players[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
