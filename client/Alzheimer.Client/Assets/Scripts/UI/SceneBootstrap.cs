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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            if (_sceneLoadedSubscribed)
                SceneManager.sceneLoaded -= HandleSceneLoaded;

            _sceneLoadedSubscribed = false;
            _persistentEventSystem = null;
        }

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
            var gameManagers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include);
            if (gameManagers.Length == 0)
            {
                var gameManager = new GameObject("GameManager");
                gameManager.AddComponent<GameManager>();
                gameManager.AddComponent<ReactionTimerController>();
            }
            else
            {
                for (var i = 1; i < gameManagers.Length; i++)
                    Object.Destroy(gameManagers[i].gameObject);

                if (gameManagers[0].GetComponent<ReactionTimerController>() == null)
                    gameManagers[0].gameObject.AddComponent<ReactionTimerController>();
            }

            var networkManagers = Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include);
            if (networkManagers.Length == 0)
            {
                var networkManager = new GameObject("NetworkManager");
                networkManager.AddComponent<NetworkManager>();
            }
            else
            {
                for (var i = 1; i < networkManagers.Length; i++)
                    Object.Destroy(networkManagers[i].gameObject);
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
                    Object.Destroy(eventSystem.gameObject);
            }

            if (primary == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                primary = eventSystemObject.AddComponent<EventSystem>();
            }

            _persistentEventSystem = primary;
            Object.DontDestroyOnLoad(primary.gameObject);

            var inputModules = primary.GetComponents<BaseInputModule>();
            InputSystemUIInputModule inputModule = null;
            foreach (var module in inputModules)
            {
                if (module is InputSystemUIInputModule inputSystemModule)
                {
                    if (inputModule == null)
                        inputModule = inputSystemModule;
                    else
                        Object.Destroy(inputSystemModule);
                }
                else
                {
                    Object.Destroy(module);
                }
            }

            if (inputModule == null)
                primary.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private static void BuildForScene(string sceneName)
        {
            if (sceneName != "MainMenu" && sceneName != "GameTable")
                return;

            var existingUi = Object.FindObjectsByType<RuntimeSceneUi>(FindObjectsInactive.Include);
            GameObject canvasObject;
            if (existingUi.Length > 0)
            {
                canvasObject = existingUi[0].gameObject;
                canvasObject.SetActive(false);
                for (var i = canvasObject.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(canvasObject.transform.GetChild(i).gameObject);
            }
            else
            {
                canvasObject = new GameObject(sceneName + "UI");
                canvasObject.AddComponent<RuntimeSceneUi>();
            }

            for (var i = 1; i < existingUi.Length; i++)
                Object.Destroy(existingUi[i].gameObject);

            canvasObject.name = sceneName + "UI";
            var canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
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

            canvasObject.SetActive(true);
        }
    }
}
