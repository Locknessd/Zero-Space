using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
using SocketIOClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class WebSocketManager : MonoBehaviour
{
    public enum StartupMode
    {
        ClientInput,
        Frontend
    }

    public static WebSocketManager Instance { get; private set; }

    [Header("Startup")]
    [Tooltip("Client Input uses IDs entered in Unity. Frontend waits for FE. Applies on all platforms.")]
    [SerializeField] private StartupMode _startupMode = StartupMode.ClientInput;

    public StartupMode ActiveStartupMode => _startupMode;

    public bool IsFrontendControlled => ActiveStartupMode == StartupMode.Frontend;
    public bool IsWaitingForFrontend => IsFrontendControlled && !_frontendStartRequested;
    public string CurrentMatchId => _currentMatchId;
    public event Action OnFrontendStartRequested;

    [Header("Connection")]
    [Tooltip("WSS endpoint for the game backend.")]
    [SerializeField]
    private string _serverUrl = "http://<backend-host>:9092";

    [Tooltip("Automatically try to reconnect on unexpected disconnect.")]
    [SerializeField]
    private bool _autoReconnect = true;

    [Tooltip("Seconds to wait before attempting reconnect.")]
    [SerializeField]
    private float _reconnectDelay = 3f;

    public event Action<string> OnRawMessageReceived;
    // Invoked when a meme_battle_start_result is received. Args: requestId (may be null/empty), rawText
    public event Action<string, string> OnStartResultReceived;
    // Connection progress event (invoked on main thread via Update)
    public event Action<float, string> OnConnectionProgress;
    // Explicit connection error event: machine-readable code and human message
    public event Action<string, string> OnConnectionError;
#if UNITY_WEBGL && !UNITY_EDITOR
    private WebGLSocketIO _ws;
#else
    private SocketIO _ws;
#endif
    private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
    private bool _isClosing;
    private bool _destroyed;
    private bool _frontendStartRequested;
    private bool _frontendStartEventSent;
    private string _frontendMatchId;
    private string _frontendRequestId;
    private string _currentMatchId;
    // Only events retained in the battle inbox acknowledge replay; snapshot state is not an event.
    private long _lastReceivedSequence = 0;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(float, string)> _progressQueue = new System.Collections.Concurrent.ConcurrentQueue<(float, string)>();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string code, string msg)> _errorQueue = new System.Collections.Concurrent.ConcurrentQueue<(string code, string msg)>();
    private System.Threading.CancellationTokenSource _connectProgressCts;

    [Header("Socket.IO Auth")]
    [Tooltip("Service token for Socket.IO auth (do not commit in source control).")]
    [SerializeField]
    private string _serviceToken = "";

    [Header("Auto Start Match")]
    [Tooltip("If set, emit a custom event after socket connected. Use to trigger server-side match start for testing.")]
    public bool emitStartMatchOnConnect = false;
    [Tooltip("Event name to emit when auto-starting match")] public string startMatchEventName = "start_match";
    [Tooltip("Raw JSON string payload to send as event argument. Example: {\"matchId\":\"match_123\"}")] [TextArea]
    public string startMatchPayload = "{}";
    [Header("Logging")]
    [Tooltip("Enable verbose socket logging (debug). When false only important logs are shown).")]
    public bool verboseLogging = false;
    [Tooltip("If set, Build start payload from this requestId (safer than editing raw JSON)")]
    public string startMatchRequestId = "";

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

    // Query connection state for external callers
    public bool IsConnected()
    {
        return _ws != null && _ws.Connected;
    }

    private void Start()
    {
        if (IsWaitingForFrontend)
        {
            EnqueueProgress(0f, "Waiting for frontend to start the game...");
            return;
        }
        _ = ConnectAsync();
    }

    /// <summary>Called by FE with an existing match ID via unityInstance.SendMessage.</summary>
    [UnityEngine.Scripting.Preserve]
    public void StartMatchFromFrontend(string matchId)
    {
        StartGameFromFrontend(JsonConvert.SerializeObject(new FrontendStartOptions { matchId = matchId }));
    }

    /// <summary>Accepts JSON containing either matchId or requestId, plus optional connection settings.</summary>
    [UnityEngine.Scripting.Preserve]
    public void StartGameFromFrontend(string json)
    {
        if (!IsFrontendControlled)
        {
            EnqueueError("FRONTEND_START_DISABLED", "Select Frontend startup mode before calling the FE API.");
            return;
        }

        try
        {
            var config = JsonConvert.DeserializeObject<FrontendStartOptions>(json);
            string matchId = config?.matchId?.Trim();
            string requestId = config?.requestId?.Trim();
            bool hasMatchId = !string.IsNullOrEmpty(matchId);
            bool hasRequestId = !string.IsNullOrEmpty(requestId);
            if (!hasMatchId) matchId = null;
            if (!hasRequestId) requestId = null;
            if (config == null || hasMatchId == hasRequestId)
            {
                EnqueueError("FRONTEND_START_INVALID", "Provide exactly one non-empty matchId or requestId.");
                return;
            }

            string serverUrl = config.serverUrl?.Trim();
            if (!string.IsNullOrEmpty(serverUrl) &&
                (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ws" && uri.Scheme != "wss")))
            {
                EnqueueError("FRONTEND_START_INVALID", "serverUrl must be an absolute HTTP(S) or WS(S) URL.");
                return;
            }

            if (_frontendStartRequested)
            {
                if (!string.Equals(_frontendMatchId, matchId, StringComparison.Ordinal) ||
                    !string.Equals(_frontendRequestId, requestId, StringComparison.Ordinal))
                {
                    EnqueueError("FRONTEND_SESSION_ACTIVE", "A match is already selected. Reload Unity to start another.");
                    return;
                }
                // Repeated FE calls during connection or gameplay must not restart or resubscribe the match.
                if (_ws != null)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    if (!_ws.CanRetry) return;
                    _ws.Dispose();
                    _ws = null;
#else
                    return;
#endif
                }
            }

            if (!string.IsNullOrEmpty(serverUrl)) _serverUrl = serverUrl;
            if (config.serviceToken != null) SetServiceToken(config.serviceToken);
            _frontendMatchId = matchId;
            _frontendRequestId = requestId;
            startMatchRequestId = requestId ?? string.Empty;
            if (!_frontendStartRequested)
            {
                _currentMatchId = matchId;
                _lastReceivedSequence = 0;
            }
            _frontendStartRequested = true;
            // The persistent battle inbox captures messages even before LoadingScene subscribes.
            OnFrontendStartRequested?.Invoke();
            _ = ConnectAsync();
        }
        catch (Exception ex)
        {
            EnqueueError("FRONTEND_START_INVALID", "Invalid frontend startup data: " + ex.Message);
        }
    }

    [Serializable]
    private sealed class FrontendStartOptions
    {
        public string matchId;
        public string requestId;
        public string serverUrl;
        public string serviceToken;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [UnityEngine.Scripting.Preserve]
    public void OnWebGLSocketEvent(string json)
    {
        try { _ws?.HandleMessage(json); }
        catch (Exception ex) { EnqueueError("SOCKET_MESSAGE_INVALID", ex.Message); }
    }
#endif

    void LogInfo(string message, bool important = false)
    {
        if (important || verboseLogging)
        {
            Debug.Log(message);
        }
    }

    // Invoked for any incoming socket event via reflection-registered handler
    void OnAnyEventHandler(string eventName, SocketIOResponse response)
    {
        try
        {
            var raw = response?.ToString() ?? "";
            // Only log catch-all events in verbose mode to avoid noise
            LogInfo($"WebSocketManager INCOMING ANY [{System.DateTime.UtcNow:o}] event={eventName} raw={raw}", false);
            // Do not enqueue here to avoid duplicate processing; specific handlers will enqueue when needed.
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"WebSocketManager: OnAnyEventHandler exception: {ex}");
        }
    }

    public void SetServiceToken(string token)
    {
        _serviceToken = token;
    }

    private void Update()
    {
        // process connection progress updates queued from background tasks
        while (_progressQueue.TryDequeue(out var ps))
        {
            try
            {
                OnConnectionProgress?.Invoke(ps.Item1, ps.Item2);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: OnConnectionProgress handler exception: {ex}");
            }
        }
        // process explicit errors queued from background handlers
        while (_errorQueue.TryDequeue(out var e))
        {
            try
            {
                OnConnectionError?.Invoke(e.code, e.msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: OnConnectionError handler exception: {ex}");
            }
        }
        while (_incomingMessages.TryDequeue(out string msg))
        {
            try
            {
                LogInfo($"WebSocketManager DISPATCH INCOMING [{System.DateTime.UtcNow:o}] -> {msg}", false);
                RetainBattleMessage(msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: message handler exception: {ex}");
            }
        }
    }

    public async Task ConnectAsync()
    {
        if (_destroyed || IsWaitingForFrontend || _ws != null)
            return;

        _isClosing = false;
        var options = new SocketIOOptions
        {
            Auth = new
            {
                clientType = "UNITY_RENDERER",
                serviceToken = _serviceToken
            }
            ,
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            EIO = SocketIOClient.EngineIO.V4,
            Path = "/socket.io"
        };

#if UNITY_WEBGL && !UNITY_EDITOR
        options.Reconnection = _autoReconnect;
        options.ReconnectionDelay = Math.Max(0, _reconnectDelay * 1000);
        options.ReconnectionDelayMax = Math.Max(5000, (int)options.ReconnectionDelay);
        _ws = new WebGLSocketIO(this, _serverUrl, options);
#else
        options.Reconnection = _autoReconnect;
        _ws = new SocketIO(_serverUrl, options);
#endif
        var socket = _ws;

#if !UNITY_WEBGL || UNITY_EDITOR
        // Register a catch-all "OnAny" handler via reflection to log every incoming event
        try
        {
            var fi = typeof(SocketIO).GetField("_onAnyHandlers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fi != null)
            {
                var list = fi.GetValue(_ws) as System.Collections.IList;
                if (list != null)
                {
                    var handlerType = fi.FieldType.GenericTypeArguments[0];
                    var method = this.GetType().GetMethod(nameof(OnAnyEventHandler), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var d = Delegate.CreateDelegate(handlerType, this, method);
                    list.Add(d);
                    LogInfo("WebSocketManager: registered OnAny event logger", false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"WebSocketManager: failed to register OnAny logger: {ex}");
        }
#endif

        _ws.OnConnected += async (sender, e) =>
        {
            if (_ws != socket || _isClosing) return;
            LogInfo("WebSocketManager: Connected.", true);
            EnqueueProgress(1f, "Connected");
            if (_connectProgressCts != null) { _connectProgressCts.Cancel(); _connectProgressCts.Dispose(); _connectProgressCts = null; }
            // auto-resubscribe if we had a match before disconnect
            if (!string.IsNullOrEmpty(_currentMatchId))
            {
                await SubscribeToMatch(_currentMatchId, _lastReceivedSequence);
            }
            // Optionally emit a test/start-match event after successful connect
            bool shouldStartMatch = IsFrontendControlled
                ? !string.IsNullOrEmpty(_frontendRequestId) &&
                  (!_frontendStartEventSent || string.IsNullOrEmpty(_currentMatchId))
                : emitStartMatchOnConnect;
            if (shouldStartMatch && !string.IsNullOrEmpty(startMatchEventName))
            {
                try
                {
                    string effectiveRequestId = IsFrontendControlled ? _frontendRequestId : startMatchRequestId;
                    if (!string.IsNullOrEmpty(effectiveRequestId))
                    {
                        // build payload programmatically to avoid inspector formatting issues
                        // sanitize common inspector paste mistakes like "startMatchRequestId = value"
                        string rid = effectiveRequestId.Trim();
                        int eq = IsFrontendControlled ? -1 : rid.IndexOf('=');
                        if (eq >= 0)
                        {
                            var right = rid.Substring(eq + 1).Trim();
                            if (!string.IsNullOrEmpty(right)) rid = right;
                        }
                        // According to BE test contract, send stringified JSON as the second arg
                        var payloadStr = JsonConvert.SerializeObject(new { requestId = rid });
                        LogInfo($"WebSocketManager OUTGOING [{System.DateTime.UtcNow:o}]: Emit -> event={startMatchEventName} AS_STRINGIFIED_JSON payload={payloadStr} (sanitized from '{startMatchRequestId}')", true);
                        if (_ws != null) await _ws.EmitAsync(startMatchEventName, payloadStr);
                        else Debug.LogWarning("WebSocketManager: _ws is null when emitting start event");
                    }
                    else
                    {
                        await EmitLoggedAsync(startMatchEventName, startMatchPayload);
                    }
                    EnqueueProgress(0.97f, $"Emitted:{startMatchEventName}");
                    if (IsFrontendControlled) _frontendStartEventSent = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"WebSocketManager: failed to emit start event: {ex.Message}");
                    EnqueueError("EMIT_FAILED", ex.Message);
                }
            }
        };

        _ws.OnError += (sender, err) =>
        {
            try
            {
                var code = MapErrorToCode(err);
                EnqueueError(code, err);
                EnqueueProgress(0f, err);
                Debug.LogError($"WebSocketManager: OnError: {err}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: OnError handler exception: {ex}");
            }
        };

        _ws.OnReconnected += (sender, attempt) =>
        {
            EnqueueProgress(0.9f, $"Reconnected attempt {attempt}");
        };

        _ws.OnReconnectAttempt += (sender, attempt) =>
        {
            EnqueueProgress(0f, $"Reconnecting (attempt {attempt})");
        };

        _ws.OnReconnectFailed += (sender, e) =>
        {
            EnqueueError("SOCKET_RECONNECT_FAILED", "Reconnect attempts failed");
            EnqueueProgress(0f, "Reconnect attempts failed");
        };

        _ws.OnDisconnected += async (sender, e) =>
        {
            if (_ws != socket) return;
            LogInfo($"WebSocketManager: Disconnected: {e}", true);
            EnqueueProgress(0f, $"Disconnected");
#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser Socket.IO client owns reconnection and preserves its callbacks.
            await Task.CompletedTask;
#else
            // Socket.IO already reconnects on transport failures; only server disconnects need this call.
            if (_autoReconnect && !_isClosing && e == DisconnectReason.IOServerDisconnect)
            {
                await Task.Delay(TimeSpan.FromSeconds(_reconnectDelay));
                if (_ws == socket && !_isClosing && !_destroyed)
                {
                    try { await socket.ConnectAsync(); }
                    catch (Exception ex)
                    {
                        EnqueueError("SOCKET_CONNECT_FAILED", ex.Message);
                        socket.Dispose();
                        if (_ws == socket) _ws = null;
                    }
                }
            }
#endif
        };

        // Snapshot: includes current state + optionally an array of events
        _ws.On("meme_battle_snapshot", response =>
        {
            try
            {
                // Read with Newtonsoft so public model fields are handled on both transports.
                var snapshotData = JToken.Parse(response.ToString());
                if (snapshotData.Type == JTokenType.Array) snapshotData = snapshotData.First;
                if (snapshotData?.Type == JTokenType.String)
                    snapshotData = JToken.Parse(snapshotData.Value<string>());
                var snapResp = snapshotData?.ToObject<SnapshotResponse>();
                if (snapResp == null) return;
                if (!string.IsNullOrEmpty(snapResp.error))
                {
                    Debug.LogError($"WebSocketManager: subscribe error: {snapResp.error}");
                    EnqueueError(MapErrorToCode(snapResp.error), snapResp.error);
                    _incomingMessages.Enqueue(JsonConvert.SerializeObject(new { type = "error", error = snapResp.error }));
                    return;
                }

                // Normalize on the main thread, retaining history before acknowledging sequences.
                _incomingMessages.Enqueue(JsonConvert.SerializeObject(new {
                    type = "meme_battle_snapshot", snapshot = snapResp.snapshot, events = snapResp.events
                }));
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: meme_battle_snapshot exception: {ex}");
            }
        });

        // Start result handler (response to meme_battle_start)
        _ws.On("meme_battle_start_result", response =>
        {
            try
            {
                // Avoid deserializing into Newtonsoft types via the SocketIO client serializer
                // which uses System.Text.Json. Get raw text and enqueue it for consumers.
                string rawText = response.ToString();
                LogInfo($"WebSocketManager INCOMING [{System.DateTime.UtcNow:o}]: meme_battle_start_result rawText={rawText}", true);

                // Snapshot watermarks are not received-event cursors.
                var wrapped = JsonConvert.SerializeObject(new { type = "meme_battle_start_result", raw = rawText });
                _incomingMessages.Enqueue(wrapped);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: meme_battle_start_result exception: {ex}");
            }
        });

        // Individual game events
        _ws.On("meme_battle_event", response =>
        {
            try
            {
                string rawText = response.ToString();
                LogInfo($"WebSocketManager INCOMING meme_battle_event raw: {rawText}", false);

                var token = ReadSocketPayload(rawText);
                var events = token is JArray array
                    ? array.ToObject<System.Collections.Generic.List<MemeBattleEvent>>()
                    : new System.Collections.Generic.List<MemeBattleEvent> { token?.ToObject<MemeBattleEvent>() };
                events.RemoveAll(ev => ev == null);
                events.Sort((a, b) => a.sequence.CompareTo(b.sequence));
                foreach (var battleEvent in events)
                    _incomingMessages.Enqueue(JsonConvert.SerializeObject(new {
                        type = "meme_battle_event", @event = battleEvent
                    }));
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: meme_battle_event exception: {ex}");
            }
        });

        // Battle result handler (indicates match has concluded)
        _ws.On("meme_battle_battle_result", response =>
        {
            try
            {
                string rawText = response.ToString();
                LogInfo($"WebSocketManager INCOMING [{System.DateTime.UtcNow:o}]: meme_battle_battle_result rawText={rawText}", true);
                var wrapped = JsonConvert.SerializeObject(new { type = "meme_battle_battle_result", raw = rawText });
                _incomingMessages.Enqueue(wrapped);
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocketManager: meme_battle_battle_result exception: {ex}");
            }
        });

        try
        {
            // start background progress reporter
            if (_connectProgressCts != null) { _connectProgressCts.Cancel(); _connectProgressCts.Dispose(); _connectProgressCts = null; }
            _connectProgressCts = new System.Threading.CancellationTokenSource();
            EnqueueProgress(0f, "Connecting...");
#if !UNITY_WEBGL || UNITY_EDITOR
            var token = _connectProgressCts.Token;
            _ = Task.Run(async () =>
            {
                float p = 0f;
                while (!token.IsCancellationRequested && (_ws == null || !_ws.Connected))
                {
                    p = Math.Min(0.98f, p + 0.02f);
                    EnqueueProgress(p, "Connecting...");
                    try { await Task.Delay(200, token).ConfigureAwait(false); } catch { break; }
                }
            }, token);
#endif

            await _ws.ConnectAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocketManager: Connect failed: {ex}");
            EnqueueProgress(0f, $"Connect failed: {ex.Message}");
            EnqueueError("SOCKET_CONNECT_FAILED", ex.Message);
            if (_connectProgressCts != null) { _connectProgressCts.Cancel(); _connectProgressCts.Dispose(); _connectProgressCts = null; }
            socket.Dispose();
            if (_ws == socket) _ws = null;
        }
    }

    /// <summary>
    /// Subscribe to a meme battle match. Payload must be a JSON string per backend spec.
    /// </summary>
    public async Task SubscribeToMatch(string matchId, long afterSequence = 0)
    {
        if (IsWaitingForFrontend) return;
        if (IsFrontendControlled && !string.Equals(matchId, _currentMatchId, StringComparison.Ordinal))
        {
            EnqueueError("FRONTEND_MATCH_MISMATCH", "Only the match selected by the frontend may be subscribed.");
            return;
        }
        if (string.IsNullOrEmpty(matchId))
        {
            EnqueueProgress(0f, "ERROR:MATCH_SUBSCRIBE_INVALID:matchId and a non-negative afterSequence are required");
            return;
        }
        if (afterSequence < 0)
        {
            EnqueueProgress(0f, "ERROR:MATCH_SUBSCRIBE_INVALID:matchId and a non-negative afterSequence are required");
            return;
        }
        _currentMatchId = matchId;

        if (_ws == null || !_ws.Connected)
        {
            Debug.LogWarning("WebSocketManager: cannot subscribe, socket not connected.");
            return;
        }

        var req = new SubscribeRequest { matchId = matchId, afterSequence = afterSequence };
        string payload = JsonConvert.SerializeObject(req);
        try
        {
            LogInfo($"WebSocketManager: Emit meme_battle_subscribe AS_STRINGIFIED_JSON payload={payload}", true);
            await _ws.EmitAsync("meme_battle_subscribe", payload);
            EnqueueProgress(0.95f, "Subscribed, waiting for snapshot...");
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocketManager: SubscribeToMatch failed: {ex}");
            EnqueueProgress(0f, $"Subscribe failed: {ex.Message}");
        }
    }

    async System.Threading.Tasks.Task EmitLoggedAsync(string eventName, string payload)
    {
        try
        {
            var ts = System.DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(payload))
            {
                Debug.Log($"WebSocketManager OUTGOING [{ts}]: Emit -> event={eventName} (no payload)");
                if (_ws != null) await _ws.EmitAsync(eventName);
                else Debug.LogWarning("WebSocketManager: EmitLoggedAsync called but _ws is null");
                return;
            }

            // If payload looks like a plain token (not JSON), treat it as a requestId and build object
            string t = payload.Trim();
            if (!t.StartsWith("{") && !t.StartsWith("[") && !t.StartsWith("\""))
            {
                // build { requestId: payload }
                var obj = new JObject();
                obj["requestId"] = payload;
                        LogInfo($"WebSocketManager OUTGOING [{ts}]: Emit -> event={eventName} AS_BUILT_FROM_PLAINTEXT payload={obj}", eventName == startMatchEventName || eventName == "meme_battle_subscribe");
                if (_ws != null) await _ws.EmitAsync(eventName, obj);
                else Debug.LogWarning("WebSocketManager: EmitLoggedAsync called but _ws is null");
                return;
            }

            // Try parse payload as JSON and emit as object when possible
            try
            {
                var token = JToken.Parse(payload);
                LogInfo($"WebSocketManager OUTGOING [{ts}]: Emit -> event={eventName} AS_OBJECT payload={token}", eventName == startMatchEventName || eventName == "meme_battle_subscribe");
                if (_ws != null) await _ws.EmitAsync(eventName, token);
                else Debug.LogWarning("WebSocketManager: EmitLoggedAsync called but _ws is null");
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                // Try to handle an escaped JSON string, e.g. payload = "{\"requestId\":\"...\"}"
                try
                {
                    var inner = JsonConvert.DeserializeObject<string>(payload);
                    if (!string.IsNullOrEmpty(inner))
                    {
                        try
                        {
                            var token2 = JToken.Parse(inner);
                            LogInfo($"WebSocketManager OUTGOING [{ts}]: Emit -> event={eventName} AS_UNESCAPED_OBJECT payload={token2}", eventName == startMatchEventName || eventName == "meme_battle_subscribe");
                            if (_ws != null) await _ws.EmitAsync(eventName, token2);
                            else Debug.LogWarning("WebSocketManager: EmitLoggedAsync called but _ws is null");
                            return;
                        }
                        catch (Newtonsoft.Json.JsonReaderException) { /* fallthrough to send as string */ }
                    }
                }
                catch { /* ignore deserialization errors */ }

                LogInfo($"WebSocketManager OUTGOING [{ts}]: Emit -> event={eventName} AS_STRING payload={payload}", eventName == startMatchEventName || eventName == "meme_battle_subscribe");
                if (_ws != null) await _ws.EmitAsync(eventName, payload);
                else Debug.LogWarning("WebSocketManager: EmitLoggedAsync called but _ws is null");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocketManager: EmitLoggedAsync failed for {eventName}: {ex}");
            throw;
        }
    }

    void EnqueueProgress(float value, string status)
    {
        _progressQueue.Enqueue((value, status));
    }

    void EnqueueError(string code, string msg)
    {
        _errorQueue.Enqueue((code ?? "UNKNOWN_ERROR", msg ?? string.Empty));
    }

    string MapErrorToCode(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "UNKNOWN_ERROR";
        msg = msg.ToLowerInvariant();
        if (msg.Contains("valid socket.io credentials") || msg.Contains("credentials are required")) return "SOCKET_AUTH_REQUIRED";
        if (msg.Contains("not authenticated")) return "UNITY_AUTH_REQUIRED";
        if (msg.Contains("principal is invalid")) return "UNITY_AUTH_INVALID";
        if (msg.Contains("not allowed to use this socket.io event") || msg.Contains("event is not allowed")) return "UNITY_EVENT_FORBIDDEN";
        if (msg.Contains("meme battle match not found") || msg.Contains("match not found")) return "MATCH_NOT_FOUND";
        if (msg.Contains("matchid and a non-negative aftersequence") || msg.Contains("non-negative")) return "MATCH_SUBSCRIBE_INVALID";
        if (msg.Contains("unable to subscribe")) return "MATCH_SUBSCRIBE_INVALID";
        return "UNKNOWN_ERROR";
    }

    public async Task DisconnectAsync()
    {
        _isClosing = true;
        if (_connectProgressCts != null)
        {
            _connectProgressCts.Cancel();
            _connectProgressCts.Dispose();
            _connectProgressCts = null;
        }

        if (_ws != null)
        {
            var socket = _ws;
            _ws = null;
            try
            {
                await socket.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"WebSocketManager: Close exception: {ex}");
            }
            finally { socket.Dispose(); }
        }
    }

    public async Task SendMessageToServer(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("WebSocketManager: Attempted to send empty message.");
            return;
        }

        if (_ws == null || !_ws.Connected)
        {
            Debug.LogWarning("WebSocketManager: Socket not open.");
            return;
        }

        // Emit as "message" event. Adjust event name if your server expects a different event.
        await EmitLoggedAsync("message", message);
    }

    private async void OnApplicationQuit()
    {
        await DisconnectAsync();
    }

    private async void OnDestroy()
    {
        // Destroying a duplicate singleton must not touch the active connection.
        if (Instance != this) return;
        _destroyed = true;
        if (Instance == this) Instance = null;
        await DisconnectAsync();
    }
}



// Models for meme battle socket messages
[Serializable]
public class SubscribeRequest
{
    public string matchId;
    public long afterSequence;
}

[Serializable]
public class SnapshotResponse
{
    public MemeBattleMatchSnapshot snapshot;
    public System.Collections.Generic.List<MemeBattleEvent> events;
    public string error;
}

[Serializable]
public class MemeBattleMatchSnapshot
{
    public string matchId;
    public string state;
    public System.Collections.Generic.Dictionary<string, long> characterHpAtomic;
    public System.Collections.Generic.Dictionary<string, long> characterMaxHpAtomic;
    public string currentTurnId;
    public MemeBattleTurnSnapshot currentTurn;
    public string winnerCharacterId;
    public bool contributionWindowOpen;
    public long latestSequence;
    public int rulesetVersion;
    public string rngCommitment;
}

[Serializable]
public class MemeBattleTurnSnapshot
{
    public string turnId;
    public int turnNumber;
    public string state;
    public string voteWindowId;
    public System.Collections.Generic.List<Argument> arguments;
    public System.Collections.Generic.Dictionary<string, long> finalTotalsAtomic;
    public string selectedArgumentId;
    public int multiplierScaled;
    public string opensAt;
    public string closesAt;
    public string resolvedAt;
}

[Serializable]
public class Argument
{
    public string argumentId;
    public string targetCharacterId;
    public long baseDamageAtomic;
    public string animationId;
}

[Serializable]
public class MemeBattleEvent
{
    public string eventId;
    public string eventType;
    public string matchId;
    public string turnId;
    public long sequence;
    public int schemaVersion;
    public string occurredAt;
    public Newtonsoft.Json.Linq.JObject payload;
}


