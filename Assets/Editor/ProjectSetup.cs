using System;
using Colonia09.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Colonia09.EditorTools
{
    /// <summary>Applies sprite import rules to everything under Resources/Sprites.</summary>
    public sealed class SpriteImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Sprites/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            if (assetPath.Contains("/Board/"))
            {
                // The battlefield background is rendered at 96 pixels per cell.
                importer.spritePixelsPerUnit = 96;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (assetPath.Contains("/Map/"))
            {
                // The campaign map is a full-screen picture shown in the UI.
                importer.spritePixelsPerUnit = 100;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            // Modules fill one cell. Enemies use 170 pixels per cell, with their size baked in.
            importer.spritePixelsPerUnit = assetPath.Contains("/Enemies/") ? 170 : 256;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
        }
    }

    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialPath = "Assets/Resources/Materials/SpriteUnlit.mat";

        [MenuItem("Colonia 09/Configurar proyecto")]
        public static void Run()
        {
            ConfigurePlayer();
            AssetDatabase.ImportAsset("Assets/Resources/Sprites", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            CreateMaterial();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("Colonia 09: project configured.");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Colonia 09";
            PlayerSettings.companyName = "Zeitend Interactive";
            PlayerSettings.bundleVersion = "0.9.1";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.lfaz.colonia09");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.lfaz.colonia09");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icon/AppIcon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 1170;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        static void CreateMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
            System.IO.Directory.CreateDirectory("Assets/Resources/Materials");
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) throw new InvalidOperationException("URP 2D unlit sprite shader not found.");
            AssetDatabase.CreateAsset(new Material(shader), MaterialPath);
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraGo = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            cameraGo.transform.position = new Vector3(3, -5, -10);
            new GameObject("Game", typeof(GameController));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        [MenuItem("Colonia 09/Compilar APK de prueba para Android")]
        public static void BuildAndroidDev()
        {
            Run();
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Android/Colonia09-dev.apk",
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            Debug.Log($"Colonia 09 build result: {report.summary.result}, errors {report.summary.totalErrors}, size {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Colonia 09/Generar proyecto de Xcode")]
        public static void BuildIosProject()
        {
            Run();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            Debug.Log($"Colonia 09 build result: {report.summary.result}, errors {report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Colonia 09/Compilar prueba para Windows")]
        public static void BuildWindowsDev()
        {
            Run();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/WindowsDev/Colonia09.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            Debug.Log($"Colonia 09 build result: {report.summary.result}, errors {report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
