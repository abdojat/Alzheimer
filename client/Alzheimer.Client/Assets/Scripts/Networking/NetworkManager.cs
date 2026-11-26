using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Alzheimer.Client.Networking
{
    public sealed class NetworkManager : MonoBehaviour, INetworkService
    {
        public static NetworkManager Instance { get; private set; }

        public bool IsConnected { get; private set; }
        public event Action<string> StatusChanged;
        public event Action<LobbySnapshotV1> LobbyChanged;
        public event Action<PublicGameViewV1> PublicStateChanged;
        public event Action<PlayerGameViewV1> PrivateStateChanged;
        public event Action<string> CommandRejected;
        public event Action RoomClosed;

        public LobbySnapshotV1 Lobby { get; private set; }
        public PublicGameViewV1 PublicGame { get; private set; }
        public PlayerGameViewV1 PrivateGame { get; private set; }
        public string LocalPlayerId { get; private set; }
        public long PublicSequence { get; private set; }
        public long PrivateSequence { get; private set; }

        private readonly Queue<Action> _mainThreadActions = new Queue<Action>();
        private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new Dictionary<string, TaskCompletionSource<object>>();
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private HttpClient _http;
        private int _invocationNumber;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            while (_mainThreadActions.Count > 0)
            {
                var action = _mainThreadActions.Dequeue();
                action();
            }
        }

        public void Connect(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                StatusChanged?.Invoke("Server URL is unavailable.");
                Debug.LogWarning("Alzheimer network service requires a server URL.");
                return;
            }

            _ = ConnectAsync(serverUrl);
        }

        private async Task ConnectAsync(string serverUrl)
        {
            DisconnectSocket();
            _cancellation = new CancellationTokenSource();
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            try
            {
                var hubUrl = GetHubUrl(serverUrl);
                var negotiateUrl = hubUrl + "/negotiate?negotiateVersion=1";
                using var negotiateRequest = new HttpRequestMessage(HttpMethod.Post, negotiateUrl)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                using var response = await _http.SendAsync(negetiateRequest, _cancellation.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Server negotiate failed (" + (int)response.StatusCode + ").");

                var negotiate = JsonConvert.DeserializeObject<NegotiateResponseV1>(body);
                if (negotiate == null)
                    throw new InvalidOperationException("Server returned an invalid negotiate response.");

                var token = string.IsNullOrEmpty(negotiate.AccessToken)
                    ? negotiate.ConnectionToken
                    : negotiate.AccessToken;
                var socketUrl = hubUrl.Replace("http://", "ws://").Replace("https://", "wss://")
                    + "?id=" + Uri.EscapeDataString(token);

                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(new Uri(socketUrl), _cancellation.Token).ConfigureAwait(false);
                await SendFrameAsync("{\"protocol\":\"json\",\"version\":1}\u001e").ConfigureAwait(false);

                RunOnMainThread(() =>
                {
                    IsConnected = true;
                    StatusChanged?.Invoke("Connected to server.");
                });

                _ = ReceiveAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Connection cancelled."));
            }
            catch (Exception exception)
            {
                RunOnMainThread(() =>
                {
                    IsConnected = false;
                    StatusChanged?.Invoke("Connection failed: " + exception.Message);
                });
            }
        }

        private static string GetHubUrl(string serverUrl)
        {
            var url = serverUrl.TrimEnd('/');
            if (!url.EndsWith("/hubs/lobby", StringComparison.OrdinalIgnoreCase))
                url += "/hubs/lobby";
            return url;
        }

        public void Disconnect()
        {
            DisconnectSocket();
            RunOnMainThread(() =>
            {
                IsConnected = false;
                StatusChanged?.Invoke("Disconnected.");
            });
        }

        private void DisconnectSocket()
        {
            try { _cancellation?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
            try { _http?.Dispose(); } catch { }
            _socket = null;
            _cancellation = null;
            _http = null;
        }

        public void CreateRoom(string playerName, int deckCount, int maxPlayers)
        {
            _ = InvokeLobbyAsync("CreateRoom", new object[] { deckCount, maxPlayers, playerName });
        }

        public void JoinRoom(string code, string playerName)
        {
            _ = InvokeLobbyAsync("JoinRoom", new object[] { code.Trim().ToUpperInvariant(), playerName });
        }

        public void LeaveRoom()
        {
            _ = InvokeVoidAsync("LeaveRoom", Array.Empty<object>());
        }

        private async Task InvokeLobbyAsync(string target, object[] arguments)
        {
            try
            {
                var lobby = await InvokeAsync<LobbySnapshotV1>(target, arguments).ConfigureAwait(false);
                RunOnMainThread(() =>
                {
                    Lobby = lobby;
                    LocalPlayerId = lobby.LocalPlayerId;
                    LobbyChanged?.Invoke(lobby);
                });
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)));
            }
        }

        private async Task InvokeVoidAsync(string target, object[] arguments)
        {
            try
            {
                await InvokeAsync<object>(target, arguments).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)));
            }
        }

        public void StartMatch()
        {
            _ = InvokeStartMatchAsync();
        }

        private async Task InvokeStartMatchAsync()
        {
            try
            {
                var response = await InvokeAsync<StartMatchResponseV1>("StartMatchV1", Array.Empty<object>())
                    .ConfigureAwait(false);
                RunOnMainThread(() =>
                {
                    if (!response.Accepted)
                    {
                        CommandRejected?.Invoke(response.ErrorCode ?? "StartMatchRejected");
                        return;
                    }
                    if (response.PublicView != null)
                        ApplyPublicState(response.StateSequence ?? 0, response.PublicView);
                    if (response.PrivateView != null)
                        ApplyPrivateState(response.StateSequence ?? 0, response.PrivateView);
                });
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)));
            }
        }

        public void SubmitCommand(GameplayCommandV1 command)
        {
            _ = InvokeCommandAsync(command);
        }

        private async Task InvokeCommandAsync(GameplayCommandV1 command)
        {
            try
            {
                var response = await InvokeAsync<GameCommandResponseV1>(
                    "SubmitGameplayCommandV1",
                    new object[] { command }).ConfigureAwait(false);
                RunOnMainThread(() =>
                {
                    if (!response.Accepted)
                    {
                        CommandRejected?.Invoke(response.ErrorCode ?? "CommandRejected");
                        return;
                    }
                    if (response.PublicView != null)
                        ApplyPublicState(response.StateSequence ?? PublicSequence, response.PublicView);
                    if (response.PrivateView != null)
                        ApplyPrivateState(response.StateSequence ?? PrivateSequence, response.PrivateView);
                });
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)));
            }
        }

        private async Task<T> InvokeAsync<T>(string target, object[] arguments)
        {
            if (!IsConnected || _socket == null || _socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected.");

            var invocationId = (++_invocationNumber).ToString();
            var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending)
                _pending.Add(invocationId, source);

            var invocation = new
            {
                type = 1,
                invocationId,
                target,
                arguments
            };
            await SendFrameAsync(JsonConvert.SerializeObject(invocation) + "\u001e").ConfigureAwait(false);

            var result = await source.Task.ConfigureAwait(false);
            if (result is HubError)
                throw new InvalidOperationException(((HubError)result).Message);
            if (result is null)
                return default(T);
            if (result is JToken token)
                return token.ToObject<T>();
            return (T)result;
        }

        private async Task SendFrameAsync(string frame)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected.");
            var bytes = Encoding.UTF8.GetBytes(frame);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellation.Token)
                .ConfigureAwait(false);
        }

        private async Task ReceiveAsync(CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            var incoming = new StringBuilder();
            try
            {
                while (!cancellation.IsCancellationRequested && _socket != null)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var received = await _socket.ReceiveAsync(segment, cancellation).ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;
                    incoming.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));

                    while (incoming.ToString().Contains("\u001e"))
                    {
                        var text = incoming.ToString();
                        var separator = text.IndexOf('\u001e');
                        var frame = text.Substring(0, separator);
                        incoming.Remove(0, separator + 1);
                        if (!string.IsNullOrWhiteSpace(frame))
                            ProcessFrame(frame);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                RunOnMainThread(() =>
                {
                    IsConnected = false;
                    StatusChanged?.Invoke("Connection lost: " + exception.Message);
                });
            }
        }

        private void ProcessFrame(string frame)
        {
            try
            {
                var message = JObject.Parse(frame);
                var type = message.Value<int>("type");
                if (type == 3)
                {
                    var invocationId = message.Value<string>("invocationId");
                    TaskCompletionSource<object> source;
                    lock (_pending)
                    {
                        if (!_pending.TryGetValue(invocationId, out source))
                            return;
                        _pending.Remove(invocationId);
                    }
                    var error = message.Value<string>("error");
                    if (!string.IsNullOrEmpty(error))
                        source.TrySetResult(new HubError(error));
                    else
                        source.TrySetResult(message["result"]);
                    return;
                }
                if (type != 1)
                    return;

                var target = message.Value<string>("target");
                var arguments = message["arguments"] as JArray;
                if (target == "RoomUpdated" && arguments != null)
                {
                    var lobby = arguments[0].ToObject<LobbySnapshotV1>();
                    RunOnMainThread(() =>
                    {
                        if (!string.IsNullOrEmpty(LocalPlayerId))
                            lobby.LocalPlayerId = LocalPlayerId;
                        Lobby = lobby;
                        LobbyChanged?.Invoke(lobby);
                    });
                }
                else if (target == "RoomClosed")
                {
                    RunOnMainThread(() => RoomClosed?.Invoke());
                }
                else if (target == "MatchStartedV1" || target == "GameStateUpdatedV1")
                {
                    if (arguments != null)
                    {
                        var update = arguments[0].ToObject<GamePublicUpdateV1>();
                        RunOnMainThread(() => ApplyPublicState(update.Sequence, update.View));
                    }
                }
                else if (target == "GamePrivateStateUpdatedV1")
                {
                    if (arguments != null)
                    {
                        var update = arguments[0].ToObject<GamePrivateUpdateV1>();
                        RunOnMainThread(() => ApplyPrivateState(update.Sequence, update.View));
                    }
                }
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Protocol error: " + exception.Message));
            }
        }

        private void ApplyPublicState(long sequence, PublicGameViewV1 view)
        {
            if (sequence < PublicSequence)
                return;
            PublicSequence = sequence;
            PublicGame = view;
            PublicStateChanged?.Invoke(view);
        }

        private void ApplyPrivateState(long sequence, PlayerGameViewV1 view)
        {
            if (sequence < PrivateSequence)
                return;
            PrivateSequence = sequence;
            PrivateGame = view;
            PrivateStateChanged?.Invoke(view);
        }

        private void RunOnMainThread(Action action)
        {
            lock (_mainThreadActions)
                _mainThreadActions.Enqueue(action);
        }

        private static string GetErrorMessage(Exception exception)
        {
            return exception.InnerException?.Message ?? exception.Message;
        }

        private sealed class HubError
        {
            public HubError(string message) { Message = message; }
            public string Message { get; }
        }
    }
}
    }
}
