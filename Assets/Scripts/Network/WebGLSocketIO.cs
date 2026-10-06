#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using UnityEngine;

/// <summary>Socket.IO browser transport. Its API matches the subset used by WebSocketManager.</summary>
internal sealed class WebGLSocketIO : IDisposable
{
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private readonly string _receiver;
    private readonly string _url;
    private readonly SocketIOOptions _options;
    private readonly Dictionary<string, Action<WebGLSocketIOResponse>> _handlers =
        new Dictionary<string, Action<WebGLSocketIOResponse>>();

    public bool Connected { get; private set; }
    public bool CanRetry { get; private set; }
    public event EventHandler OnConnected;
    public event EventHandler<string> OnError;
    public event EventHandler<string> OnDisconnected;
    public event EventHandler<int> OnReconnected;
    public event EventHandler<int> OnReconnectAttempt;
    public event EventHandler OnReconnectFailed;

    public WebGLSocketIO(WebSocketManager owner, string url, SocketIOOptions options)
    {
        _receiver = owner.gameObject.name;
        _url = url;
        _options = options;
    }

    public void On(string eventName, Action<WebGLSocketIOResponse> handler) => _handlers[eventName] = handler;

    public Task ConnectAsync()
    {
        string options = JsonConvert.SerializeObject(new
        {
            auth = _options.Auth,
            path = _options.Path,
            transports = new[] { "websocket" },
            autoConnect = false,
            forceNew = true,
            reconnection = _options.Reconnection,
            reconnectionDelay = _options.ReconnectionDelay,
            reconnectionDelayMax = _options.ReconnectionDelayMax,
            timeout = _options.ConnectionTimeout.TotalMilliseconds
        });
        ZeroSpaceSocketIO_Connect(_connectionId, _receiver, _url, options,
            Application.streamingAssetsPath.TrimEnd('/') + "/ZeroSpace/socket.io.min.js");
        return Task.CompletedTask;
    }

    public Task EmitAsync(string eventName, params object[] args)
    {
        if (!Connected) throw new InvalidOperationException("Browser socket is not connected.");
        ZeroSpaceSocketIO_Emit(_connectionId, eventName, JsonConvert.SerializeObject(args));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        ZeroSpaceSocketIO_Disconnect(_connectionId);
        Connected = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        ZeroSpaceSocketIO_Disconnect(_connectionId);
        Connected = false;
        _handlers.Clear();
    }

    public void HandleMessage(string json)
    {
        var message = JObject.Parse(json);
        if (message.Value<string>("connectionId") != _connectionId) return;
        string eventName = message.Value<string>("eventName");
        string data = message.Value<string>("data") ?? string.Empty;
        switch (eventName)
        {
            case "connect":
                Connected = true;
                CanRetry = false;
                OnConnected?.Invoke(this, EventArgs.Empty);
                break;
            case "disconnect":
                Connected = false;
                CanRetry = !_options.Reconnection;
                OnDisconnected?.Invoke(this, data);
                break;
            case "fatal_error":
                CanRetry = true;
                OnError?.Invoke(this, data);
                break;
            case "connect_error":
                OnError?.Invoke(this, data);
                break;
            case "reconnect":
                OnReconnected?.Invoke(this, int.Parse(data));
                break;
            case "reconnect_attempt":
                OnReconnectAttempt?.Invoke(this, int.Parse(data));
                break;
            case "reconnect_failed":
                CanRetry = true;
                OnReconnectFailed?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (_handlers.TryGetValue(eventName, out var handler))
                    handler(new WebGLSocketIOResponse(data));
                break;
        }
    }

    [DllImport("__Internal")]
    private static extern void ZeroSpaceSocketIO_Connect(string id, string receiver, string url,
        string options, string libraryUrl);
    [DllImport("__Internal")]
    private static extern void ZeroSpaceSocketIO_Emit(string id, string eventName, string arguments);
    [DllImport("__Internal")]
    private static extern void ZeroSpaceSocketIO_Disconnect(string id);
}

internal sealed class WebGLSocketIOResponse
{
    private readonly string _json;
    public WebGLSocketIOResponse(string json) => _json = json;
    public override string ToString() => _json;

    public T GetValue<T>()
    {
        var token = JToken.Parse(_json);
        if (token.Type == JTokenType.Array) token = token.First;
        if (token?.Type == JTokenType.String) token = JToken.Parse(token.Value<string>());
        return token == null ? default(T) : token.ToObject<T>();
    }
}
#endif
