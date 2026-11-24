using Alzheimer.Client.Game;
using Alzheimer.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace Alzheimer.Client.UI
{
    public static class SceneBootstrap
    {
        private static EventSystem _persistentEventSystem;
        private static bool _sceneLoadedSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!_sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
                _sceneLoadedSubscribed = true;
            }

            InitializeCurrentScene();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InitializeCurrentScene();
        }

        private static void InitializeCurrentScene()
        {
            EnsureCamera();
            EnsureEventSystem();
            EnsureManagers();
            BuildForScene(SceneManager.GetActiveScene().name);
        }

        private static void EnsureManagers()
        {
            if (Object.FindAnyObjectByType<GameManager>() == null)
            {
                var gameManager = new GameObject("GameManager");
                gameManager.AddComponent<GameManager>();
                gameManager.AddComponent<ReactionTimerController>();
            }

            if (Object.FindAnyObjectByType<NetworkManager>() == null)
            {
                var networkManager = new GameObject("NetworkManager");
                networkManager.AddComponent<NetworkManager>();
            }
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
            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            EventSystem primary = _persistentEventSystem;
            if (primary == null)
                primary = eventSystems.Length > 0 ? eventSystems[0] : null;

            foreach (var eventSystem in eventSystems)
            {
                if (eventSystem != null && eventSystem != primary)
                    Object.DestroyImmediate(eventSystem.gameObject);
            }

            if (primary == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                primary = eventSystemObject.AddComponent<EventSystem>();
            }

            _persistentEventSystem = primary;
            Object.DontDestroyOnLoad(primary.gameObject);

            var inputModules = primary.GetComponents<BaseInputModule>();
            foreach (var inputModule in inputModules)
                Object.DestroyImmediate(inputModule);

            primary.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private static void BuildForScene(string sceneName)
        {
            foreach (var existingUi in Object.FindObjectsByType<RuntimeSceneUi>(FindObjectsInactive.Include))
                Object.DestroyImmediate(existingUi.gameObject);

            if (sceneName != "MainMenu" && sceneName != "GameTable")
                return;

            var canvasObject = new GameObject(sceneName + "UI");
            canvasObject.AddComponent<RuntimeSceneUi>();
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
