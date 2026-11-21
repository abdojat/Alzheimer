using Alzheimer.Client.Game;
using Alzheimer.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alzheimer.Client.UI
{
    public static class SceneBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            EnsureCamera();
            EnsureEventSystem();

            if (FindObjectOfType<GameManager>() == null)
            {
                var gameManager = new GameObject("GameManager");
                gameManager.AddComponent<GameManager>();
                gameManager.AddComponent<ReactionTimerController>();
            }

            if (FindObjectOfType<NetworkManager>() == null)
            {
                var networkManager = new GameObject("NetworkManager");
                networkManager.AddComponent<NetworkManager>();
            }

            BuildForScene(SceneManager.GetActiveScene().name);
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var cameraObject = new GameObject("UICamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            camera.orthographic = true;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static void BuildForScene(string sceneName)
        {
            var canvasObject = new GameObject(sceneName + "UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var safeAreaObject = new GameObject("SafeArea");
            safeAreaObject.transform.SetParent(canvasObject.transform, false);
            var safeRect = safeAreaObject.AddComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            safeAreaObject.AddComponent<SafeArea>();

            if (sceneName == "MainMenu")
                safeAreaObject.AddComponent<MainMenuScreen>().Initialize(safeAreaObject);
            else if (sceneName == "GameTable")
                safeAreaObject.AddComponent<GameTableScreen>().Initialize(safeAreaObject);
        }
    }
}
