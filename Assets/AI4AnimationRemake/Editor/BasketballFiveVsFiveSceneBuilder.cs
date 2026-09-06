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
        private const string PlayerPrefabPath =
            CharacterRoot + "/BasketballPlayer.prefab";
        private const string TeamsPrefabPath =
            CharacterRoot + "/BasketballTeams.prefab";
        private const string CourtRoot = "Assets/AI4AnimationRemake/Court/CrowdEyesTwin";
        private const string SourceCourtTemplatePath =
            CourtRoot + "/Source/BasketballSyntheticDataEnvironment.prefab";
        private const string CourtMaterialRoot = CourtRoot + "/Materials";
        private const string CourtPrefabPath = CourtRoot + "/BasketballCourt.prefab";
        private const string CourtFloorTexturePath =
            CourtRoot + "/Source/laminate_floor_02_diff_adjusted.png";
        private const string CourtFloorNormalPath =
            CourtRoot + "/Source/laminate_floor_02_nor_gl_2k.jpg";
        private const string CourtFloorMaskPath =
            CourtRoot + "/Source/laminate_floor_02_mask_2k.png";
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

        [MenuItem("Tools/AI4Animation/Build CrowdEyes-Twin Basketball Court")]
        public static void BuildCourtFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before building the court. Active scene: {scene.path}");
            }

            GameObject courtPrefab = BuildCourtPrefab();
            BasketballCourt court = InstallCourt(scene, courtPrefab);
            BasketballTeamMember[] players = Object
                .FindObjectsByType<BasketballTeamMember>(FindObjectsInactive.Include)
                .Where(player => player.gameObject.scene == scene)
                .OrderBy(player => player.PlayerIndex)
                .ToArray();
            WireMatch(players, court);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "BasketballDemo now uses the CrowdEyes-Twin FIBA court, two " +
                "team-aware hoops, aimed shot physics, and score tracking.");
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
                GameObject courtPrefab = BuildCourtPrefab();
                BasketballCourt court = InstallCourt(scene, courtPrefab);
                BasketballTeamMember[] players = BuildPlayers(scene, visualPrefab);
                WireMatch(players, court);
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
                BasketballHumanoidHandContactSolver handContact =
                    root.AddComponent<BasketballHumanoidHandContactSolver>();
                handContact.Configure(retargeter);
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

        private static GameObject BuildCourtPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CourtPrefabPath);
            BasketballCourt existingCourt = existing != null
                ? existing.GetComponent<BasketballCourt>()
                : null;
            Transform existingGeometry = existing != null
                ? existing.transform.Find("Geometry")
                : null;
            bool usesDemoAxes = existingGeometry != null && Quaternion.Angle(
                existingGeometry.localRotation,
                Quaternion.Euler(0f, -90f, 0f)) < 0.1f;
            if (existingCourt != null && existingCourt.PositiveZHoop != null &&
                existingCourt.NegativeZHoop != null &&
                usesDemoAxes)
            {
                CreateCourtMaterial(
                    "CourtFloor_URP",
                    CourtFloorTexturePath,
                    Color.white,
                    0.44f);
                Material existingRedBoxMaterial = CreateCourtMaterial(
                    "BackboardTarget_URP",
                    null,
                    new Color(0.72f, 0.012f, 0.006f, 1f),
                    0.08f,
                    reflective: false);
                EnsurePrefabRendererMaterial(
                    CourtPrefabPath,
                    "RedBox",
                    existingRedBoxMaterial);
                EnsurePrefabCourtBoundaries(CourtPrefabPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(CourtPrefabPath);
            }
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(CourtPrefabPath);
            }

            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                SourceCourtTemplatePath);
            if (sourceAsset == null)
            {
                throw new InvalidOperationException(
                    $"Missing CrowdEyes-Twin court extraction template at " +
                    SourceCourtTemplatePath);
            }

            EnsureFolder(CourtRoot);
            EnsureFolder(CourtMaterialRoot);
            Material floor = CreateCourtMaterial(
                "CourtFloor_URP",
                CourtFloorTexturePath,
                Color.white,
                0.44f);
            Material lines = CreateCourtMaterial(
                "CourtLines_URP",
                null,
                new Color(0.96f, 0.97f, 1f, 1f),
                0.2f);
            Material structure = CreateCourtMaterial(
                "Structure_URP",
                null,
                new Color(0.12f, 0.15f, 0.2f, 1f),
                0.38f);
            Material rim = CreateCourtMaterial(
                "Rim_URP",
                null,
                new Color(0.95f, 0.24f, 0.035f, 1f),
                0.48f);
            Material redBoxMaterial = CreateCourtMaterial(
                "BackboardTarget_URP",
                null,
                new Color(0.72f, 0.012f, 0.006f, 1f),
                0.08f,
                reflective: false);
            Material padding = CreateCourtMaterial(
                "Padding_URP",
                null,
                new Color(0.025f, 0.055f, 0.14f, 1f),
                0.24f);
            Material glass = CreateCourtMaterial(
                "BackboardGlass_URP",
                null,
                new Color(0.78f, 0.9f, 1f, 0.42f),
                0.72f,
                true);

            GameObject sourceRoot = PrefabUtility.LoadPrefabContents(SourceCourtTemplatePath);
            GameObject root = new("Basketball Court");
            try
            {
                Transform generated = sourceRoot.transform.Find(
                    "Court & Geometry/Procedural Basketball Court/Generated Court");
                if (generated == null)
                {
                    throw new InvalidOperationException(
                        "CrowdEyes-Twin environment is missing its Generated Court subtree.");
                }

                GameObject geometry = Object.Instantiate(generated.gameObject);
                geometry.name = "Geometry";
                geometry.transform.SetParent(root.transform, false);
                // CrowdEyes-Twin's generated court uses X as its longitudinal
                // axis, while BasketballDemo and its team formations use Z.
                // Rotate the extracted geometry once so team 0 really attacks
                // +Z and all trajectory/three-point calculations share axes.
                geometry.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.Euler(0f, -90f, 0f));
                geometry.transform.localScale = Vector3.one;
                if (PrefabUtility.IsPartOfPrefabInstance(geometry))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        geometry,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    string objectName = renderer.gameObject.name;
                    renderer.sharedMaterial = objectName switch
                    {
                        "Court Floor" => floor,
                        "Court Markings" => lines,
                        "Rim" => rim,
                        "RedBox" => redBoxMaterial,
                        "Backboard" => glass,
                        "BoardPad" => padding,
                        "Stanchion Base" => padding,
                        _ => structure
                    };
                    renderer.shadowCastingMode = objectName == "Court Markings"
                        ? ShadowCastingMode.Off
                        : ShadowCastingMode.On;
                    renderer.receiveShadows = objectName != "Court Markings";
                }

                Transform positiveBasket = geometry.transform.Find("Basket Positive Z");
                Transform negativeBasket = geometry.transform.Find("Basket Negative Z");
                if (positiveBasket == null || negativeBasket == null)
                {
                    throw new InvalidOperationException(
                        "CrowdEyes-Twin court must contain both basket ends.");
                }
                BasketballHoop positiveHoop = ConfigureHoop(positiveBasket, 0);
                BasketballHoop negativeHoop = ConfigureHoop(negativeBasket, 1);
                BasketballCourt court = root.AddComponent<BasketballCourt>();
                court.Configure(positiveHoop, negativeHoop);
                CreateCourtBoundaries(root.transform, court);
                root.AddComponent<BasketballScoreTracker>();

                GameObject firstSave = PrefabUtility.SaveAsPrefabAsset(root, CourtPrefabPath);
                if (firstSave == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save court prefab at {CourtPrefabPath}.");
                }

                GameObject savedRoot = PrefabUtility.LoadPrefabContents(CourtPrefabPath);
                try
                {
                    BasketballCourt savedCourt = savedRoot.GetComponent<BasketballCourt>();
                    Transform savedGeometry = savedRoot.transform.Find("Geometry");
                    BasketballHoop savedPositive = savedGeometry != null
                        ? savedGeometry.Find("Basket Positive Z")
                            ?.GetComponent<BasketballHoop>()
                        : null;
                    BasketballHoop savedNegative = savedGeometry != null
                        ? savedGeometry.Find("Basket Negative Z")
                            ?.GetComponent<BasketballHoop>()
                        : null;
                    if (savedCourt == null || savedPositive == null || savedNegative == null)
                    {
                        throw new InvalidOperationException(
                            "Saved court prefab lost its Hoop components during extraction.");
                    }
                    savedCourt.Configure(savedPositive, savedNegative);
                    PrefabUtility.SaveAsPrefabAsset(savedRoot, CourtPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(savedRoot);
                }
                return AssetDatabase.LoadAssetAtPath<GameObject>(CourtPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(sourceRoot);
                Object.DestroyImmediate(root);
            }
        }

        private static BasketballHoop ConfigureHoop(Transform basket, int teamId)
        {
            Transform rim = basket.Find("Rim");
            Transform backboard = basket.Find("Backboard");
            if (rim == null || backboard == null)
            {
                throw new InvalidOperationException(
                    $"{basket.name} is missing Rim or Backboard.");
            }
            BasketballHoop hoop = basket.gameObject.AddComponent<BasketballHoop>();
            hoop.Configure(teamId, rim, backboard, 0.225f);
            return hoop;
        }

        private static Material CreateCourtMaterial(
            string name,
            string texturePath,
            Color color,
            float smoothness,
            bool transparent = false,
            bool reflective = true)
        {
            EnsureFolder(CourtMaterialRoot);
            string path = $"{CourtMaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isCourtFloor = name == "CourtFloor_URP";
            Shader shader = Shader.Find(isCourtFloor
                ? "CrowdEyes/Basketball/Twin Court Floor URP"
                : "Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit shader is unavailable.");
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
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.SetFloat("_SpecularHighlights", reflective ? 1f : 0f);
            material.SetFloat("_EnvironmentReflections", reflective ? 1f : 0f);
            if (isCourtFloor)
            {
                material.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
                material.SetTexture(
                    "_NormalMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(CourtFloorNormalPath));
                material.SetTexture(
                    "_MaskMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(CourtFloorMaskPath));
                material.SetFloat("_NormalScale", 0.35f);
                material.SetFloat("_MetallicRemapMin", 0f);
                material.SetFloat("_MetallicRemapMax", 1f);
                material.SetFloat("_AORemapMin", 0f);
                material.SetFloat("_AORemapMax", 1f);
                material.SetFloat("_SmoothnessRemapMin", 0.62683564f);
                material.SetFloat("_SmoothnessRemapMax", 0.8630134f);
                material.SetFloat("_AlbedoBoost", 1.5f);
            }
            material.enableInstancing = true;
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.SetFloat("_SrcBlend", transparent
                ? (float)BlendMode.SrcAlpha
                : (float)BlendMode.One);
            material.SetFloat("_DstBlend", transparent
                ? (float)BlendMode.OneMinusSrcAlpha
                : (float)BlendMode.Zero);
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsurePrefabRendererMaterial(
            string prefabPath,
            string rendererObjectName,
            Material material)
        {
            if (material == null || string.IsNullOrEmpty(prefabPath))
            {
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = false;
            try
            {
                foreach (Renderer renderer in
                         prefabRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.gameObject.name != rendererObjectName ||
                        renderer.sharedMaterial == material)
                    {
                        continue;
                    }
                    renderer.sharedMaterial = material;
                    changed = true;
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void EnsurePrefabCourtBoundaries(string prefabPath)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                BasketballCourt court = prefabRoot.GetComponent<BasketballCourt>();
                if (court == null)
                {
                    throw new InvalidOperationException(
                        "Basketball Court prefab is missing BasketballCourt.");
                }

                Transform boundaryRoot = prefabRoot.transform.Find("Court Boundaries");
                BasketballCourtBoundary boundary = boundaryRoot != null
                    ? boundaryRoot.GetComponent<BasketballCourtBoundary>()
                    : null;
                bool complete = boundary != null &&
                                boundaryRoot.Find("Baseline Positive") != null &&
                                boundaryRoot.Find("Baseline Negative") != null &&
                                boundaryRoot.Find("Sideline Positive") != null &&
                                boundaryRoot.Find("Sideline Negative") != null;
                if (!complete)
                {
                    boundary = CreateCourtBoundaries(prefabRoot.transform, court);
                }
                else
                {
                    boundary.RefreshColliders();
                }
                SetLayerRecursively(
                    boundary.gameObject,
                    0);

                if (PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath) == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to update court boundaries at {prefabPath}.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static BasketballCourtBoundary CreateCourtBoundaries(
            Transform courtRoot,
            BasketballCourt court)
        {
            Transform existing = courtRoot.Find("Court Boundaries");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject boundaryRoot = new("Court Boundaries");
            boundaryRoot.transform.SetParent(courtRoot, false);
            boundaryRoot.layer = 0;

            BoxCollider baselinePositive = CreateBoundaryWall(
                boundaryRoot.transform,
                "Baseline Positive");
            BoxCollider baselineNegative = CreateBoundaryWall(
                boundaryRoot.transform,
                "Baseline Negative");
            BoxCollider sidelinePositive = CreateBoundaryWall(
                boundaryRoot.transform,
                "Sideline Positive");
            BoxCollider sidelineNegative = CreateBoundaryWall(
                boundaryRoot.transform,
                "Sideline Negative");

            BasketballCourtBoundary boundary =
                boundaryRoot.AddComponent<BasketballCourtBoundary>();
            boundary.Configure(
                court.CourtLength,
                court.CourtWidth,
                baselinePositive,
                baselineNegative,
                sidelinePositive,
                sidelineNegative);
            return boundary;
        }

        private static BoxCollider CreateBoundaryWall(
            Transform parent,
            string name)
        {
            GameObject wall = new(name);
            wall.transform.SetParent(parent, false);
            wall.layer = 0;
            return wall.AddComponent<BoxCollider>();
        }

        private static BasketballCourt InstallCourt(Scene scene, GameObject courtPrefab)
        {
            BasketballCourt existing = Object.FindAnyObjectByType<BasketballCourt>(
                FindObjectsInactive.Include);
            if (existing != null && existing.gameObject.scene == scene)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject world = GameObject.Find("World");
            if (world == null || world.scene != scene)
            {
                throw new InvalidOperationException("BasketballDemo requires a World root.");
            }
            DisableLegacyCourtPointLight(world.transform);
            Transform oldGround = world.transform.Find("Ground");
            if (oldGround != null)
            {
                Object.DestroyImmediate(oldGround.gameObject);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(
                courtPrefab,
                world.transform) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Could not instantiate Basketball Court.");
            }
            instance.name = "Basketball Court";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            int groundLayer = LayerMask.NameToLayer("Ground");
            SetLayerRecursively(instance, groundLayer >= 0 ? groundLayer : 0);
            Transform boundaryRoot = instance.transform.Find("Court Boundaries");
            if (boundaryRoot != null)
            {
                // Neural roots collide against Default + legacy environment.
                // The camera explicitly ignores BasketballCourtBoundary walls.
                SetLayerRecursively(
                    boundaryRoot.gameObject,
                    0);
            }
            return instance.GetComponent<BasketballCourt>();
        }

        private static void DisableLegacyCourtPointLight(Transform world)
        {
            Transform pointTransform = world.Find("Lights/Point");
            Light pointLight = pointTransform != null
                ? pointTransform.GetComponent<Light>()
                : null;
            if (pointLight == null || pointLight.type != LightType.Point ||
                !pointLight.enabled)
            {
                return;
            }

            // The original demo's range-100 fill light creates a large circular
            // overexposure on the remade court. The directional rig already
            // provides the intended scene lighting.
            pointLight.enabled = false;
            EditorUtility.SetDirty(pointLight);
        }

        private static BasketballTeamMember[] BuildPlayers(Scene scene, GameObject visualPrefab)
        {
            List<BasketballTeamMember> existingPlayers = Object
                .FindObjectsByType<BasketballTeamMember>(
                    FindObjectsInactive.Include)
                .Where(player => player.gameObject.scene == scene)
                .OrderBy(player => player.PlayerIndex)
                .ToList();
            if (existingPlayers.Count < 1)
            {
                throw new InvalidOperationException("BasketballDemo has no canonical player rig to clone.");
            }
            if (existingPlayers.Count > PlayerCount)
            {
                throw new InvalidOperationException(
                    $"BasketballDemo already contains {existingPlayers.Count} players; expected at most {PlayerCount}.");
            }

            GameObject playerPrefab = BuildFullPlayerPrefab(
                existingPlayers[0].gameObject,
                visualPrefab);
            GameObject teamsPrefab = BuildTeamsPrefab(playerPrefab);

            // Saving either prefab can cause Unity to reload matching scene
            // instances. Never retain Component references across that operation;
            // rescan current scene roots after both assets are complete.
            HashSet<GameObject> rootsToRemove = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Teams" ||
                    root.GetComponentInChildren<BasketballTeamMember>(true) != null)
                {
                    rootsToRemove.Add(root);
                }
            }
            foreach (GameObject root in rootsToRemove)
            {
                Undo.DestroyObjectImmediate(root);
            }

            GameObject teams = PrefabUtility.InstantiatePrefab(teamsPrefab) as GameObject;
            if (teams == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate team prefab at {TeamsPrefabPath}.");
            }
            teams.name = "Teams";
            SceneManager.MoveGameObjectToScene(teams, scene);
            Undo.RegisterCreatedObjectUndo(teams, "Install Basketball Teams Prefab");

            BasketballTeamMember[] players = teams
                .GetComponentsInChildren<BasketballTeamMember>(true)
                .OrderBy(player => player.PlayerIndex)
                .ToArray();
            if (players.Length != PlayerCount)
            {
                throw new InvalidOperationException(
                    $"Team prefab produced {players.Length} players; expected {PlayerCount}.");
            }

            return players;
        }

        private static GameObject BuildFullPlayerPrefab(
            GameObject sceneTemplate,
            GameObject visualPrefab)
        {
            GameObject root = Object.Instantiate(sceneTemplate);
            root.name = "Basketball Player";
            root.transform.SetParent(null);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                BasketballTeamMember player = root.GetComponent<BasketballTeamMember>();
                BasketballNeuralController controller =
                    root.GetComponent<BasketballNeuralController>();
                BasketballKeyboardMouseInputProvider input =
                    root.GetComponent<BasketballKeyboardMouseInputProvider>();
                BasketballDebugVisualizer visualizer =
                    root.GetComponent<BasketballDebugVisualizer>();
                BasketballTargetIndicator indicator =
                    root.GetComponent<BasketballTargetIndicator>();
                BasketballReferenceRig rig = root.GetComponent<BasketballReferenceRig>();
                if (player == null || controller == null || input == null || rig == null)
                {
                    throw new InvalidOperationException(
                        "Canonical player template is missing its neural runtime components.");
                }

                player.Configure(
                    0,
                    0,
                    1,
                    controller,
                    input,
                    visualizer,
                    indicator);

                BasketballHumanoidVisualRetargeter[] visuals =
                    root.GetComponentsInChildren<BasketballHumanoidVisualRetargeter>(true);
                for (int visualIndex = 1; visualIndex < visuals.Length; visualIndex++)
                {
                    Object.DestroyImmediate(visuals[visualIndex].gameObject);
                }
                if (visuals.Length == 0)
                {
                    GameObject visual = PrefabUtility.InstantiatePrefab(
                        visualPrefab,
                        root.transform) as GameObject;
                    if (visual == null)
                    {
                        throw new InvalidOperationException(
                            "Could not add the CrowdEyes visual to the full player prefab.");
                    }
                    visual.name = "CrowdEyes Basketball Player Visual";
                    visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    visual.transform.localScale = Vector3.one;
                    SetLayerRecursively(visual, root.layer);
                }

                BasketballPlayerBodyContact bodyContact =
                    root.GetComponent<BasketballPlayerBodyContact>();
                if (bodyContact == null)
                {
                    bodyContact = root.AddComponent<BasketballPlayerBodyContact>();
                }
                CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = Vector3.up * 0.86f;
                capsule.radius = 0.34f;
                capsule.height = 1.72f;
                capsule.isTrigger = false;
                Rigidbody body = root.GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.constraints = RigidbodyConstraints.FreezeRotation;

                // Scene references are intentionally assigned after the outer
                // Teams prefab is installed.
                SetObjectReference(rig, "ball", null);
                SetObjectReference(controller, "movementCamera", null);
                BasketballPlayerAppearance appearance =
                    root.GetComponentInChildren<BasketballPlayerAppearance>(true);
                appearance?.Apply();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save full player prefab at {PlayerPrefabPath}.");
                }
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildTeamsPrefab(GameObject playerPrefab)
        {
            GameObject teams = new("Teams");
            try
            {
                for (int teamId = 0; teamId < 2; teamId++)
                {
                    GameObject teamObject = new(teamId == 0 ? "Home" : "Away");
                    teamObject.transform.SetParent(teams.transform, false);
                    BasketballTeamGroup group = teamObject.AddComponent<BasketballTeamGroup>();
                    group.Configure(teamId, teamId == 0 ? "Home" : "Away");

                    for (int slot = 0; slot < TeamSize; slot++)
                    {
                        int index = teamId * TeamSize + slot;
                        GameObject playerObject = PrefabUtility.InstantiatePrefab(
                            playerPrefab,
                            teamObject.transform) as GameObject;
                        if (playerObject == null)
                        {
                            throw new InvalidOperationException(
                                $"Could not create player prefab instance {index + 1}.");
                        }
                        playerObject.name = $"Player {index + 1}";
                        Quaternion facing = teamId == 0
                            ? Quaternion.identity
                            : Quaternion.Euler(0f, 180f, 0f);
                        playerObject.transform.SetLocalPositionAndRotation(
                            Formation[index],
                            facing);

                        BasketballTeamMember player =
                            playerObject.GetComponent<BasketballTeamMember>();
                        player.Configure(
                            index,
                            teamId,
                            index + 1,
                            playerObject.GetComponent<BasketballNeuralController>(),
                            playerObject.GetComponent<BasketballKeyboardMouseInputProvider>(),
                            playerObject.GetComponent<BasketballDebugVisualizer>(),
                            playerObject.GetComponent<BasketballTargetIndicator>());
                        player.GetComponentInChildren<BasketballPlayerAppearance>(true)?.Apply();
                        EditorUtility.SetDirty(player);
                    }
                }

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(teams, TeamsPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save teams prefab at {TeamsPrefabPath}.");
                }
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(teams);
            }
        }

        private static void WireMatch(
            BasketballTeamMember[] players,
            BasketballCourt court)
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
            possession.UpgradeStealTuningDefaults();
            SetObjectReference(match, "court", court);
            SetObjectReference(possession, "court", court);
            BasketballBallController sharedBall =
                Object.FindAnyObjectByType<BasketballBallController>(
                    FindObjectsInactive.Include);
            Camera movementCamera = Object.FindAnyObjectByType<Camera>(
                FindObjectsInactive.Include);
            for (int index = 0; index < players.Length; index++)
            {
                BasketballTeamMember player = players[index];
                BasketballReferenceRig rig =
                    player.GetComponent<BasketballReferenceRig>();
                BasketballNeuralController controller = player.Controller != null
                    ? player.Controller
                    : player.GetComponent<BasketballNeuralController>();
                SetObjectReference(rig, "ball", sharedBall);
                SetObjectReference(controller, "movementCamera", movementCamera);
                EditorUtility.SetDirty(rig);
                EditorUtility.SetDirty(controller);
            }
            BasketballCourtBoundary courtBoundary = court != null
                ? court.GetComponentInChildren<BasketballCourtBoundary>(true)
                : null;
            if (courtBoundary != null)
            {
                courtBoundary.SetBall(sharedBall);
                courtBoundary.RefreshColliders();
                EditorUtility.SetDirty(courtBoundary);
            }
            BasketballRuleBasedTeamAI teamAI =
                match.GetComponent<BasketballRuleBasedTeamAI>();
            if (teamAI == null)
            {
                teamAI = match.gameObject.AddComponent<BasketballRuleBasedTeamAI>();
            }
            teamAI.UpgradeTuningDefaults();
            teamAI.Configure(players, possession, sharedBall, court);
            SetObjectReference(match, "ruleBasedTeamAI", teamAI);
            SetObjectReference(match, "decisionPolicyBehaviour", teamAI);
            BasketballWorldEventStream worldEvents =
                match.GetComponent<BasketballWorldEventStream>();
            if (worldEvents == null)
            {
                worldEvents = match.gameObject.AddComponent<BasketballWorldEventStream>();
            }
            worldEvents.Configure();
            SetObjectReference(match, "worldEventStream", worldEvents);
            SetObjectReference(possession, "worldEventStream", worldEvents);
            BasketballRulesManager rules =
                match.GetComponent<BasketballRulesManager>();
            if (rules == null)
            {
                rules = match.gameObject.AddComponent<BasketballRulesManager>();
            }
            rules.Configure(players, sharedBall, possession, court, worldEvents);
            SetObjectReference(match, "rulesManager", rules);
            BasketballRewardTracker rewards =
                match.GetComponent<BasketballRewardTracker>();
            if (rewards == null)
            {
                rewards = match.gameObject.AddComponent<BasketballRewardTracker>();
            }
            rewards.Configure(worldEvents, players);
            SetObjectReference(match, "rewardTracker", rewards);
            BasketballSkillTelemetryRecorder telemetry =
                match.GetComponent<BasketballSkillTelemetryRecorder>();
            if (telemetry == null)
            {
                telemetry =
                    match.gameObject.AddComponent<BasketballSkillTelemetryRecorder>();
            }
            telemetry.Configure(worldEvents);
            telemetry.SetRewardTracker(rewards);
            SetObjectReference(match, "telemetryRecorder", telemetry);
            BasketballScoreTracker score = court != null
                ? court.GetComponent<BasketballScoreTracker>()
                : null;
            if (score != null)
            {
                score.Configure(
                    court,
                    sharedBall,
                    possession);
                EditorUtility.SetDirty(score);
            }
            EditorUtility.SetDirty(match);
            EditorUtility.SetDirty(possession);
            EditorUtility.SetDirty(teamAI);
            EditorUtility.SetDirty(worldEvents);
            EditorUtility.SetDirty(rules);
            EditorUtility.SetDirty(rewards);
            EditorUtility.SetDirty(telemetry);
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name}.{propertyName} was not found.");
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
