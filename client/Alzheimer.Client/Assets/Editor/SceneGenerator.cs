using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Alzheimer.Client.Editor
{
    public static class SceneGenerator
    {
        [MenuItem("Alzheimer/Generate and Configure Project")]
        public static void GenerateAndConfigure()
        {
            ConfigureAndroid();
            ConfigureUrp();
            GenerateMainMenu();
            GenerateGameTable();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/GameTable.unity", true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Alzheimer scenes and Android configuration generated successfully.");
        }

        public static void GenerateAndExit()
        {
            GenerateAndConfigure();
            EditorApplication.Exit(0);
        }

        private static void GenerateMainMenu()
        {
            GenerateScene(
                "Assets/Scenes/MainMenu.unity",
                "MainMenu");
        }

        private static void GenerateGameTable()
        {
            GenerateScene(
                "Assets/Scenes/GameTable.unity",
                "GameTable");
        }

        private static void GenerateScene(string scenePath, string sceneName)
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;

            new GameObject("SceneBootstrapMarker").tag = "EditorOnly";

            EnsureDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void EnsureDirectory(string directory)
        {
            if (AssetDatabase.IsValidFolder(directory))
                return;

            var parent = "Assets";
            var parts = directory.Split('/');
            var current = parent;
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void ConfigureAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android,
                BuildTarget.Android);
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                "com.yourstudio.alzheimer");
            PlayerSettings.productName = "Alzheimer";
            PlayerSettings.companyName = "Alzheimer";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            Debug.Log(
                $"Android configured: SDK={PlayerSettings.Android.minSdkVersion}, " +
                $"architectures={PlayerSettings.Android.targetArchitectures}, " +
                $"orientation={PlayerSettings.defaultInterfaceOrientation}");
        }

        private static void ConfigureUrp()
        {
            var urpAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                "Assets/Settings/URP2D.asset");
            if (urpAsset == null)
            {
                Debug.LogWarning("URP 2D asset was not found at Assets/Settings/URP2D.asset.");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = urpAsset;
            QualitySettings.renderPipeline = urpAsset;
        }
    }
}
