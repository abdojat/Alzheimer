using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Alzheimer.Client.Game;
using Alzheimer.Client.Networking;
using Alzheimer.Client.UI;

namespace Alzheimer.Client.Tests
{
    public sealed class SceneBootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator RepeatedMenuAndTableTransitionsKeepOneUiStack()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;

            AssertSceneUi("MainMenu");

            for (var i = 0; i < 6; i++)
            {
                SceneManager.LoadScene(i % 2 == 0 ? "GameTable" : "MainMenu");
                yield return null;
                yield return null;

                AssertSceneUi(SceneManager.GetActiveScene().name);
            }

            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;

            AssertSceneUi("MainMenu");
        }

        [UnityTest]
        public IEnumerator EventSystemUsesInputSystemUiModuleOnce()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;

            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            Assert.That(eventSystems, Has.Length.EqualTo(1));

            var modules = eventSystems[0].GetComponents<InputSystemUIInputModule>();
            Assert.That(modules, Has.Length.EqualTo(1),
                "InputSystemUIInputModule should be the only UI input module.");
            Assert.That(eventSystems[0].GetComponent<BaseInputModule>(), Is.SameAs(modules[0]));
        }

        private static void AssertSceneUi(string sceneName)
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            Assert.That(Object.FindObjectsByType<RuntimeSceneUi>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
        }
    }
}
