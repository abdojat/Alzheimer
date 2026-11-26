using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
            AssertNetworkSubscriptionsBounded();

            for (var i = 0; i < 6; i++)
            {
                SceneManager.LoadScene(i % 2 == 0 ? "GameTable" : "MainMenu");
                yield return null;
                yield return null;

                AssertSceneUi(SceneManager.GetActiveScene().name);
                AssertNetworkSubscriptionsBounded();
            }

            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;

            AssertSceneUi("MainMenu");
            AssertNetworkSubscriptionsBounded();
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

        [Test]
        public void GameplayActionsSerializeAsTheServerEnumNumbers()
        {
            var expectedNames = new[]
            {
                "PeekInitialCards", "DrawCard", "DiscardDrawnCard", "ReplaceHandCard",
                "ActivateDrawnAbility", "ResolveJackAbility", "ResolveQueenAbility",
                "ResolveKingAbility", "CallAlzheimer", "EndTurn", "ThrowReactionCard",
                "MarkReactionDone"
            };

            for (var value = 0; value < expectedNames.Length; value++)
            {
                var action = (GameplayActionV1)value;
                Assert.That(action.ToString(), Is.EqualTo(expectedNames[value]));
                var json = JObject.Parse(JsonConvert.SerializeObject(GameplayCommandV1.Create(action)));
                Assert.That(json["action"].Type, Is.EqualTo(JTokenType.Integer));
                Assert.That((int)json["action"], Is.EqualTo(value));
                Assert.That(json.ToObject<GameplayCommandV1>().Action, Is.EqualTo(action));
            }
        }

        [Test]
        public void ProjectionEnumsUseTheServerNumericValues()
        {
            var publicJson = JObject.Parse(JsonConvert.SerializeObject(new PublicGameViewV1
            {
                Phase = GamePhaseV1.AwaitingPostResolutionDecision
            }));
            Assert.That((int)publicJson["phase"], Is.EqualTo(4));

            var privateJson = JObject.Parse(JsonConvert.SerializeObject(new PlayerGameViewV1
            {
                AvailableDrawResolutions = new[]
                {
                    DrawResolutionOptionV1.Discard,
                    DrawResolutionOptionV1.Replace,
                    DrawResolutionOptionV1.ActivateAbility
                }
            }));
            Assert.That(privateJson["availableDrawResolutions"].ToObject<int[]>(), Is.EqualTo(new[] { 0, 1, 2 }));

            var cardJson = JObject.Parse(JsonConvert.SerializeObject(new CardViewV1
            {
                Rank = CardRankV1.Queen,
                Suit = CardSuitV1.Hearts
            }));
            Assert.That((int)cardJson["rank"], Is.EqualTo(12));
            Assert.That((int)cardJson["suit"], Is.EqualTo(3));
        }

        [Test]
        public void SignalRParserHandlesPartialUtf8AndMultipleFrames()
        {
            var parser = new SignalRFrameParser();
            var bytes = Encoding.UTF8.GetBytes("{\"name\":\"J♥\"}\u001e{\"type\":6}\u001e");
            var split = System.Array.IndexOf(bytes, (byte)0xe2) + 1;
            var firstPart = parser.Append(bytes, split);
            Assert.That(firstPart, Is.Empty);

            var remainder = new byte[bytes.Length - split];
            System.Array.Copy(bytes, split, remainder, 0, remainder.Length);
            var frames = parser.Append(remainder, remainder.Length);
            Assert.That(frames, Has.Count.EqualTo(2));
            Assert.That(frames[0], Is.EqualTo("{\"name\":\"J♥\"}"));
            Assert.That(frames[1], Is.EqualTo("{\"type\":6}"));
        }

        [Test]
        public void DuplicateCompletionFramesDoNotCompleteAnInvocationTwice()
        {
            var manager = GetNetworkManager(out var created);
            try
            {
                var pendingField = typeof(NetworkManager).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic);
                var pending = (Dictionary<string, TaskCompletionSource<JToken>>)pendingField.GetValue(manager);
                var source = new TaskCompletionSource<JToken>();
                pending.Add("test-id", source);
                var completion = new JObject
                {
                    ["type"] = 3,
                    ["invocationId"] = "test-id",
                    ["result"] = true
                };
                var complete = typeof(NetworkManager).GetMethod("CompleteInvocation", BindingFlags.Instance | BindingFlags.NonPublic);

                complete.Invoke(manager, new object[] { completion });
                complete.Invoke(manager, new object[] { completion });

                Assert.That(source.Task.Result.Value<bool>(), Is.True);
                Assert.That(pending, Is.Empty);
            }
            finally
            {
                if (created)
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void DisconnectCompletesPendingInvocations()
        {
            var manager = GetNetworkManager(out var created);
            try
            {
                var pendingField = typeof(NetworkManager).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic);
                var pending = (Dictionary<string, TaskCompletionSource<JToken>>)pendingField.GetValue(manager);
                var source = new TaskCompletionSource<JToken>();
                pending.Add("pending-id", source);

                manager.Disconnect();

                Assert.That(source.Task.IsCanceled, Is.True);
                Assert.That(pending, Is.Empty);
            }
            finally
            {
                if (created)
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void StalePublicAndPrivateSequencesAreIgnored()
        {
            var manager = GetNetworkManager(out var created);
            try
            {
                manager.Disconnect();
                var publicUpdates = 0;
                var privateUpdates = 0;
                var newestPublic = new PublicGameViewV1 { Phase = GamePhaseV1.AwaitingPostResolutionDecision };
                var stalePublic = new PublicGameViewV1 { Phase = GamePhaseV1.AwaitingDraw };
                var newestPrivate = new PlayerGameViewV1 { Public = newestPublic };
                var stalePrivate = new PlayerGameViewV1 { Public = stalePublic };
                manager.PublicStateChanged += _ => publicUpdates++;
                manager.PrivateStateChanged += _ => privateUpdates++;
                var applyPublic = typeof(NetworkManager).GetMethod("ApplyPublicState", BindingFlags.Instance | BindingFlags.NonPublic);
                var applyPrivate = typeof(NetworkManager).GetMethod("ApplyPrivateState", BindingFlags.Instance | BindingFlags.NonPublic);

                applyPublic.Invoke(manager, new object[] { 5L, newestPublic });
                applyPublic.Invoke(manager, new object[] { 4L, stalePublic });
                applyPublic.Invoke(manager, new object[] { 5L, stalePublic });
                applyPrivate.Invoke(manager, new object[] { 5L, newestPrivate });
                applyPrivate.Invoke(manager, new object[] { 4L, stalePrivate });

                Assert.That(publicUpdates, Is.EqualTo(1));
                Assert.That(privateUpdates, Is.EqualTo(1));
                Assert.That(manager.PublicGame, Is.SameAs(newestPublic));
                Assert.That(manager.PrivateGame, Is.SameAs(newestPrivate));
            }
            finally
            {
                if (created)
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void MainThreadDispatchRunsQueuedCallbacksOnlyDuringUpdate()
        {
            var manager = GetNetworkManager(out var created);
            try
            {
                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                var callbackThreadId = -1;
                var dispatch = typeof(NetworkManager).GetMethod("RunOnMainThread", BindingFlags.Instance | BindingFlags.NonPublic);
                var update = typeof(NetworkManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                Task.Run(() => dispatch.Invoke(manager, new object[] { (System.Action)(() => callbackThreadId = Thread.CurrentThread.ManagedThreadId), null }))
                    .GetAwaiter().GetResult();

                Assert.That(callbackThreadId, Is.EqualTo(-1));
                update.Invoke(manager, null);
                Assert.That(callbackThreadId, Is.EqualTo(mainThreadId));
            }
            finally
            {
                if (created)
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void CardIdentityIsHiddenAgainAfterTemporaryReveal()
        {
            var cardObject = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            var labelObject = new GameObject("CardLabel", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(cardObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            var card = cardObject.AddComponent<CardView>();
            try
            {
                card.Initialize(0);
                Assert.That(label.text, Is.EqualTo("?"));
                card.Reveal("7 H");
                Assert.That(card.IsRevealed, Is.True);
                Assert.That(label.text, Is.EqualTo("7 H"));
                card.SetFaceDown();
                Assert.That(card.IsRevealed, Is.False);
                Assert.That(label.text, Is.EqualTo("?"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void GameTableHidesKnownHandCardAfterRevealDeadline()
        {
            var root = new GameObject("RevealTest", typeof(RectTransform));
            var screen = root.AddComponent<GameTableScreen>();
            try
            {
                screen.Initialize(root);
                var view = new PlayerGameViewV1
                {
                    OwnHand = new[]
                    {
                        new CardSlotViewV1
                        {
                            HandIndex = 0,
                            Knowledge = CardKnowledgeV1.KnownToOwner,
                            KnownCard = new CardViewV1 { Rank = CardRankV1.Seven, Suit = CardSuitV1.Hearts }
                        }
                    }
                };
                var renderHand = typeof(GameTableScreen).GetMethod("RenderHand", BindingFlags.Instance | BindingFlags.NonPublic);
                var revealKnownCards = typeof(GameTableScreen).GetMethod("RevealKnownCards", BindingFlags.Instance | BindingFlags.NonPublic);
                var cardsField = typeof(GameTableScreen).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic);
                var revealDeadlineField = typeof(GameTableScreen).GetField("_handRevealUntilUtc", BindingFlags.Instance | BindingFlags.NonPublic);
                var update = typeof(GameTableScreen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

                renderHand.Invoke(screen, new object[] { view });
                revealKnownCards.Invoke(screen, new object[] { view, new[] { 0 } });
                renderHand.Invoke(screen, new object[] { view });
                var card = ((List<CardView>)cardsField.GetValue(screen))[0];
                Assert.That(card.IsRevealed, Is.True);

                revealDeadlineField.SetValue(screen, System.DateTimeOffset.UtcNow.AddSeconds(-1));
                update.Invoke(screen, null);

                Assert.That(card.IsRevealed, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ActionButtonsUseASeparateVerticalLayout()
        {
            var canvasObject = new GameObject("ActionLayoutCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var root = new GameObject("ActionLayoutRoot", typeof(RectTransform));
            root.transform.SetParent(canvasObject.transform, false);
            var screen = root.AddComponent<GameTableScreen>();
            screen.Initialize(root);
            var createButton = typeof(GameTableScreen).GetMethod("CreateActionButton", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var i = 0; i < 4; i++)
                createButton.Invoke(screen, new object[] { "ACTION " + i, (UnityEngine.Events.UnityAction)(() => { }) });

            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var parentField = typeof(GameTableScreen).GetField("_actionParent", BindingFlags.Instance | BindingFlags.NonPublic);
            var actionParent = (Transform)parentField.GetValue(screen);
            Assert.That(actionParent.childCount, Is.EqualTo(4));
            for (var i = 0; i < actionParent.childCount - 1; i++)
            {
                var current = (RectTransform)actionParent.GetChild(i);
                var next = (RectTransform)actionParent.GetChild(i + 1);
                Assert.That(Mathf.Abs(current.anchoredPosition.y - next.anchoredPosition.y),
                    Is.GreaterThanOrEqualTo((current.rect.height + next.rect.height) / 2f));
            }

            UnityEngine.Object.DestroyImmediate(canvasObject);
        }

        private static NetworkManager GetNetworkManager(out bool created)
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            created = manager == null;
            if (created)
                manager = new GameObject("NetworkManagerTest").AddComponent<NetworkManager>();
            return manager;
        }

        private static void AssertNetworkSubscriptionsBounded()
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            foreach (var eventName in new[]
            {
                "StatusChanged", "LobbyChanged", "PublicStateChanged", "PrivateStateChanged",
                "CommandRejected", "RoomClosed"
            })
            {
                var eventField = typeof(NetworkManager).GetField(
                    eventName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var handlers = eventField.GetValue(manager) as System.Delegate;
                Assert.That(handlers == null ? 0 : handlers.GetInvocationList().Length,
                    Is.LessThanOrEqualTo(1),
                    eventName + " should not accumulate screen subscriptions.");
            }
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
