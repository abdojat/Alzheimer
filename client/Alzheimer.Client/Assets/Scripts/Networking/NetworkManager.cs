using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);

        public static NetworkManager Instance { get; private set; }

        public bool IsConnected => Volatile.Read(ref _connected) != 0;
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

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly object _pendingSync = new object();
        private readonly object _connectionSync = new object();
        private readonly Dictionary<string, TaskCompletionSource<JToken>> _pending =
            new Dictionary<string, TaskCompletionSource<JToken>>();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private HttpClient _http;
        private int _connected;
        private int _connecting;
        private long _invocationNumber;
        private int _generation;

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
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Interlocked.Increment(ref _generation);
            DisconnectSocket();
        }

        public void Connect(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Server URL is unavailable."));
                return;
            }

            if (IsConnected)
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Already connected."));
                return;
            }

            if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Connection is already in progress."));
                return;
            }

            ClearSessionState();
            _ = ConnectAsync(serverUrl.Trim());
        }

        private async Task ConnectAsync(string serverUrl)
        {
            var generation = Interlocked.Increment(ref _generation);
            var cancellation = new CancellationTokenSource();
            var http = new HttpClient { Timeout = RequestTimeout };
            var socket = new ClientWebSocket();
            var handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_connectionSync)
            {
                if (generation != Volatile.Read(ref _generation))
                {
                    cancellation.Dispose();
                    http.Dispose();
                    socket.Dispose();
                    Interlocked.Exchange(ref _connecting, 0);
                    return;
                }

                _cancellation = cancellation;
                _http = http;
                _socket = socket;
            }

            try
            {
                var hubUrl = GetHubUrl(serverUrl);
                var negotiateUrl = hubUrl + "/negotiate?negotiateVersion=1";
                using (var negotiateRequest = new HttpRequestMessage(HttpMethod.Post, negotiateUrl))
                {
                    negotiateRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    using (var response = await http.SendAsync(negotiateRequest, cancellation.Token).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Server negotiate failed (" + (int)response.StatusCode + ").");

                        var negotiate = JsonConvert.DeserializeObject<NegotiateResponseV1>(body);
                        if (negotiate == null || string.IsNullOrWhiteSpace(negotiate.ConnectionToken))
                            throw new InvalidOperationException("Server returned an invalid negotiate response.");

                        await socket.ConnectAsync(
                            GetWebSocketUri(hubUrl, negotiate.ConnectionToken, negotiate.AccessToken),
                            cancellation.Token).ConfigureAwait(false);

                        _ = ReceiveAsync(socket, cancellation.Token, generation, handshake);
                        await SendFrameAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e", cancellation.Token)
                            .ConfigureAwait(false);

                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                        {
                            timeout.CancelAfter(RequestTimeout);
                            await WaitForTaskAsync(handshake.Task, timeout.Token).ConfigureAwait(false);
                        }
                    }
                }

                lock (_connectionSync)
                {
                    if (generation != Volatile.Read(ref _generation))
                        return;
                    if (_socket != socket || _cancellation != cancellation || socket.State != WebSocketState.Open)
                        throw new InvalidOperationException("Connection closed during the SignalR handshake.");
                    Volatile.Write(ref _connected, 1);
                }

                RunOnMainThread(() =>
                {
                    StatusChanged?.Invoke("Connected to server.");
                }, generation);
                _ = SendPingLoopAsync(socket, cancellation.Token, generation);
            }
            catch (OperationCanceledException)
            {
                if (CleanupConnection(generation))
                    RunOnMainThread(() => StatusChanged?.Invoke("Connection cancelled."), generation);
            }
            catch (Exception exception)
            {
                if (CleanupConnection(generation))
                    RunOnMainThread(() => StatusChanged?.Invoke("Connection failed: " + exception.Message), generation);
            }
            finally
            {
                Interlocked.Exchange(ref _connecting, 0);
            }
        }

        private static string GetHubUrl(string serverUrl)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Server URL must use http or https.");

            var path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith("/hubs/lobby", StringComparison.OrdinalIgnoreCase))
                path += "/hubs/lobby";

            var builder = new UriBuilder(uri)
            {
                Path = path,
                Query = "",
                Fragment = ""
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        private static Uri GetWebSocketUri(string hubUrl, string connectionToken, string accessToken)
        {
            var builder = new UriBuilder(hubUrl)
            {
                Scheme = hubUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
                Query = "id=" + Uri.EscapeDataString(connectionToken)
            };

            if (!string.IsNullOrEmpty(accessToken))
                builder.Query += "&access_token=" + Uri.EscapeDataString(accessToken);

            return builder.Uri;
        }

        public void Disconnect()
        {
            Interlocked.Increment(ref _generation);
            DisconnectSocket();
            ClearSessionState();
            RunOnMainThread(() => StatusChanged?.Invoke("Disconnected."));
        }

        private void DisconnectSocket()
        {
            ClientWebSocket socket;
            CancellationTokenSource cancellation;
            HttpClient http;
            lock (_connectionSync)
            {
                socket = _socket;
                cancellation = _cancellation;
                http = _http;
                _socket = null;
                _cancellation = null;
                _http = null;
            }

            Volatile.Write(ref _connected, 0);
            cancellation?.Cancel();
            socket?.Dispose();
            http?.Dispose();
            cancellation?.Dispose();
            FailPending();
        }

        private bool CleanupConnection(int generation)
        {
            ClientWebSocket socket;
            CancellationTokenSource cancellation;
            HttpClient http;
            lock (_connectionSync)
            {
                if (generation != Volatile.Read(ref _generation))
                    return false;

                if (_socket == null && _cancellation == null && _http == null)
                    return false;

                socket = _socket;
                cancellation = _cancellation;
                http = _http;
                _socket = null;
                _cancellation = null;
                _http = null;
            }

            Volatile.Write(ref _connected, 0);
            cancellation?.Cancel();
            socket?.Dispose();
            http?.Dispose();
            cancellation?.Dispose();
            FailPending();
            return true;
        }

        private void ClearSessionState()
        {
            Lobby = null;
            PublicGame = null;
            PrivateGame = null;
            LocalPlayerId = null;
            PublicSequence = 0;
            PrivateSequence = 0;
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
            var generation = Volatile.Read(ref _generation);
            try
            {
                var lobby = await InvokeAsync<LobbySnapshotV1>(target, arguments).ConfigureAwait(false);
                if (lobby == null)
                    throw new InvalidOperationException("Server returned an empty lobby response.");

                RunOnMainThread(() =>
                {
                    Lobby = lobby;
                    LocalPlayerId = lobby.LocalPlayerId;
                    LobbyChanged?.Invoke(lobby);
                }, generation);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)), generation);
            }
        }

        private async Task InvokeVoidAsync(string target, object[] arguments)
        {
            var generation = Volatile.Read(ref _generation);
            try
            {
                await InvokeAsync<JToken>(target, arguments).ConfigureAwait(false);
                if (target == "LeaveRoom")
                    RunOnMainThread(() => { Lobby = null; LocalPlayerId = null; }, generation);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)), generation);
            }
        }

        public void StartMatch()
        {
            _ = InvokeStartMatchAsync();
        }

        private async Task InvokeStartMatchAsync()
        {
            var generation = Volatile.Read(ref _generation);
            try
            {
                var response = await InvokeAsync<StartMatchResponseV1>("StartMatchV1", Array.Empty<object>())
                    .ConfigureAwait(false);
                if (response == null)
                    throw new InvalidOperationException("Server returned an empty start response.");

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
                }, generation);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)), generation);
            }
        }

        public void SubmitCommand(GameplayCommandV1 command)
        {
            if (command == null)
            {
                RunOnMainThread(() => CommandRejected?.Invoke("InvalidCommand"));
                return;
            }

            _ = InvokeCommandAsync(command);
        }

        private async Task InvokeCommandAsync(GameplayCommandV1 command)
        {
            var generation = Volatile.Read(ref _generation);
            try
            {
                var response = await InvokeAsync<GameCommandResponseV1>(
                    "SubmitGameplayCommandV1",
                    new object[] { command }).ConfigureAwait(false);
                if (response == null)
                    throw new InvalidOperationException("Server returned an empty command response.");

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
                }, generation);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => CommandRejected?.Invoke(GetErrorMessage(exception)), generation);
            }
        }

        private async Task<T> InvokeAsync<T>(string target, object[] arguments)
        {
            ClientWebSocket socket;
            CancellationToken token;
            lock (_connectionSync)
            {
                socket = _socket;
                token = _cancellation == null ? CancellationToken.None : _cancellation.Token;
            }

            if (!IsConnected || socket == null || socket.State != WebSocketState.Open || !token.CanBeCanceled)
                throw new InvalidOperationException("Not connected.");

            var invocationId = Interlocked.Increment(ref _invocationNumber).ToString(CultureInfo.InvariantCulture);
            var source = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingSync)
                _pending.Add(invocationId, source);

            var invocation = new Dictionary<string, object>
            {
                ["type"] = 1,
                ["invocationId"] = invocationId,
                ["target"] = target,
                ["arguments"] = arguments
            };

            try
            {
                await SendFrameAsync(socket, JsonConvert.SerializeObject(invocation) + "\u001e", token)
                    .ConfigureAwait(false);
                JToken result;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(InvocationTimeout);
                    await WaitForTaskAsync(source.Task, timeout.Token).ConfigureAwait(false);
                    result = await source.Task.ConfigureAwait(false);
                }
                if (result == null || result.Type == JTokenType.Null)
                    return default(T);
                return result.ToObject<T>();
            }
            catch
            {
                lock (_pendingSync)
                {
                    if (_pending.Remove(invocationId))
                        source.TrySetCanceled();
                }
                throw;
            }
        }

        private async Task SendFrameAsync(ClientWebSocket socket, string frame, CancellationToken cancellation)
        {
            var bytes = Encoding.UTF8.GetBytes(frame);
            await _sendGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (socket.State != WebSocketState.Open)
                    throw new InvalidOperationException("Not connected.");

                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellation).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private async Task ReceiveAsync(
            ClientWebSocket socket,
            CancellationToken cancellation,
            int generation,
            TaskCompletionSource<bool> handshake)
        {
            var buffer = new byte[4096];
            var parser = new SignalRFrameParser();
            var closeReason = "Connection closed by server.";
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation)
                        .ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        if (!string.IsNullOrEmpty(received.CloseStatusDescription))
                            closeReason = received.CloseStatusDescription;
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(
                                received.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                closeReason,
                                CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                    if (received.MessageType != WebSocketMessageType.Text)
                        throw new InvalidOperationException("SignalR JSON transport received a non-text WebSocket message.");

                    var closeReceived = false;
                    foreach (var frame in parser.Append(buffer, received.Count))
                    {
                        if (string.IsNullOrWhiteSpace(frame))
                            continue;
                        if (!ProcessFrame(frame, handshake, generation, out var serverCloseReason))
                        {
                            closeReason = serverCloseReason;
                            closeReceived = true;
                            break;
                        }
                    }

                    if (closeReceived)
                    {
                        if (socket.State == WebSocketState.Open)
                            await socket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                closeReason,
                                CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                closeReason = "Connection lost: " + exception.Message;
                handshake.TrySetException(exception);
            }
            finally
            {
                if (!handshake.Task.IsCompleted)
                {
                    if (cancellation.IsCancellationRequested)
                        handshake.TrySetCanceled();
                    else
                        handshake.TrySetException(new InvalidOperationException(closeReason));
                }
                if (!cancellation.IsCancellationRequested)
                    HandleConnectionEnded(generation, closeReason);
            }
        }

        private bool ProcessFrame(
            string frame,
            TaskCompletionSource<bool> handshake,
            int generation,
            out string closeReason)
        {
            closeReason = "Connection closed by server.";
            if (!handshake.Task.IsCompleted)
            {
                try
                {
                    var acknowledgement = JObject.Parse(frame);
                    var error = acknowledgement.Value<string>("error");
                    if (!string.IsNullOrEmpty(error))
                    {
                        var exception = new InvalidOperationException("SignalR handshake failed: " + error);
                        handshake.TrySetException(exception);
                        closeReason = exception.Message;
                        return false;
                    }

                    handshake.TrySetResult(true);
                    return true;
                }
                catch (Exception exception)
                {
                    handshake.TrySetException(new InvalidOperationException("Invalid SignalR handshake response.", exception));
                    closeReason = "Invalid SignalR handshake response.";
                    return false;
                }
            }

            try
            {
                var message = JObject.Parse(frame);
                var type = message.Value<int?>("type");
                if (!type.HasValue)
                    throw new InvalidOperationException("SignalR message has no type.");

                if (type.Value == 3)
                {
                    CompleteInvocation(message);
                    return true;
                }
                if (type.Value == 6)
                    return true;
                if (type.Value == 7)
                {
                    closeReason = message.Value<string>("error") ?? "SignalR server closed the connection.";
                    return false;
                }
                if (type.Value != 1)
                    return true;

                ProcessServerInvocation(message, generation);
            }
            catch (Exception exception)
            {
                RunOnMainThread(() => StatusChanged?.Invoke("Protocol error: " + exception.Message), generation);
            }

            return true;
        }

        private void CompleteInvocation(JObject message)
        {
            var invocationId = message.Value<string>("invocationId");
            if (string.IsNullOrEmpty(invocationId))
                return;

            TaskCompletionSource<JToken> source;
            lock (_pendingSync)
            {
                if (!_pending.TryGetValue(invocationId, out source))
                    return;
                _pending.Remove(invocationId);
            }

            var error = message.Value<string>("error");
            if (!string.IsNullOrEmpty(error))
                source.TrySetException(new InvalidOperationException(error));
            else
                source.TrySetResult(message["result"]);
        }

        private void ProcessServerInvocation(JObject message, int generation)
        {
            var target = message.Value<string>("target");
            var arguments = message["arguments"] as JArray;
            if (string.IsNullOrEmpty(target) || arguments == null || arguments.Count == 0)
                return;

            if (target == "RoomUpdated")
            {
                var lobby = arguments[0].ToObject<LobbySnapshotV1>();
                if (lobby == null)
                    throw new InvalidOperationException("Server sent an empty lobby update.");

                RunOnMainThread(() =>
                {
                    if (!string.IsNullOrEmpty(LocalPlayerId))
                        lobby.LocalPlayerId = LocalPlayerId;
                    Lobby = lobby;
                    LobbyChanged?.Invoke(lobby);
                }, generation);
            }
            else if (target == "RoomClosed")
            {
                RunOnMainThread(() => RoomClosed?.Invoke(), generation);
            }
            else if (target == "MatchStartedV1" || target == "GameStateUpdatedV1")
            {
                var update = arguments[0].ToObject<GamePublicUpdateV1>();
                if (update == null || update.View == null)
                    throw new InvalidOperationException("Server sent an invalid public game update.");
                RunOnMainThread(() => ApplyPublicState(update.Sequence, update.View), generation);
            }
            else if (target == "GamePrivateStateUpdatedV1")
            {
                var update = arguments[0].ToObject<GamePrivateUpdateV1>();
                if (update == null || update.View == null)
                    throw new InvalidOperationException("Server sent an invalid private game update.");
                RunOnMainThread(() => ApplyPrivateState(update.Sequence, update.View), generation);
            }
        }

        private async Task SendPingLoopAsync(ClientWebSocket socket, CancellationToken cancellation, int generation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await Task.Delay(PingInterval, cancellation).ConfigureAwait(false);
                    await SendFrameAsync(socket, "{\"type\":6}\u001e", cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                HandleConnectionEnded(generation, "Connection lost: " + exception.Message);
            }
        }

        private static async Task WaitForTaskAsync(Task task, CancellationToken cancellation)
        {
            var cancellationTask = Task.Delay(Timeout.Infinite, cancellation);
            if (await Task.WhenAny(task, cancellationTask).ConfigureAwait(false) != task)
                throw new OperationCanceledException(cancellation);
            await task.ConfigureAwait(false);
        }

        private void HandleConnectionEnded(int generation, string reason)
        {
            if (!CleanupConnection(generation))
                return;

            RunOnMainThread(() =>
            {
                StatusChanged?.Invoke(reason);
            }, generation);
        }

        private void FailPending()
        {
            TaskCompletionSource<JToken>[] pending;
            lock (_pendingSync)
            {
                pending = new TaskCompletionSource<JToken>[_pending.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
            }

            foreach (var source in pending)
                source.TrySetCanceled();
        }

        private void ApplyPublicState(long sequence, PublicGameViewV1 view)
        {
            if (view == null || sequence <= PublicSequence)
                return;
            PublicSequence = sequence;
            PublicGame = view;
            PublicStateChanged?.Invoke(view);
        }

        private void ApplyPrivateState(long sequence, PlayerGameViewV1 view)
        {
            if (view == null || sequence <= PrivateSequence)
                return;
            PrivateSequence = sequence;
            PrivateGame = view;
            PrivateStateChanged?.Invoke(view);
        }

        private void RunOnMainThread(Action action, int? generation = null)
        {
            _mainThreadActions.Enqueue(() =>
            {
                if (generation.HasValue && generation.Value != Volatile.Read(ref _generation))
                    return;
                action();
            });
        }

        private static string GetErrorMessage(Exception exception)
        {
            return exception.InnerException?.Message ?? exception.Message;
        }
    }
}
