using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using System;

public class LoaddingManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject loadingPanel;
    public Slider progressSlider;
    public Text percentText;
    public Text statusText;

    [Header("Behavior")]
    [Tooltip("Hide loading panel automatically when connected")] public bool hideOnConnected = true;
    [Tooltip("Seconds to wait after connected before hiding panel")] public float hideDelay = 0.5f;
    [Tooltip("Scene name to load after websocket connected")]
    public string targetSceneName = "BattleScene";
    [Tooltip("Automatically load target scene when connected")]
    public bool loadSceneOnConnected = true;

    float _targetProgress = 0f;
    string _status = "";
    bool _subscribed = false;
    bool _hasReachedFull = false;
    [Header("Auto Subscribe")]
    [Tooltip("If set, LoaddingManager will subscribe to this matchId as soon as socket connects. WebSocketManager retains incoming battle messages until GameManager is ready.")]
    public string autoSubscribeMatchId = "";

    private System.Action<string> _messageHandler;
    private bool _matchStarted = false;
    private AsyncOperation _loadOp = null;
    private bool _sceneReady = false;
    private bool _preloadStarted = false;
    private bool _persistAcrossScenes = false;

    public bool IsLoadingBattleScene => _preloadStarted;
    public bool IsBattlePresentationReady { get; private set; }

    void Start()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        TrySubscribe();
        // WebSocketManager owns the persistent battle inbox for both startup modes.
        if (WebSocketManager.Instance != null && WebSocketManager.Instance.IsFrontendControlled)
        {
            if (WebSocketManager.Instance.IsWaitingForFrontend)
                _status = "Waiting for frontend to start the game...";
        }
        else if (!string.IsNullOrEmpty(autoSubscribeMatchId))
        {
            PersistAcrossScenes();
        }
    }

    private void PersistAcrossScenes()
    {
        if (_persistAcrossScenes) return;
        _persistAcrossScenes = true;
        DontDestroyOnLoad(gameObject);
    }

    private void PrepareFrontendStart()
    {
        PersistAcrossScenes();
        _status = "Connecting...";
    }

    // Called by other systems (e.g. GameManager) to display an external error in the loading UI
    public void HandleExternalError(string code, string message)
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        _targetProgress = 0f;
        _status = MapErrorCodeToUserMessage(code, message ?? string.Empty);
        CancelInvoke(nameof(LoadTargetScene));
        CancelInvoke(nameof(HidePanel));
    }

    void TrySubscribe()
    {
        if (_subscribed) return;
        if (WebSocketManager.Instance != null)
        {
            WebSocketManager.Instance.OnConnectionProgress += HandleConnectionProgress;
            WebSocketManager.Instance.OnConnectionError += HandleConnectionError;
            WebSocketManager.Instance.OnFrontendStartRequested += PrepareFrontendStart;
            // Always listen for raw messages to detect match start (even without autoSubscribeMatchId)
            if (_messageHandler == null)
            {
                _messageHandler = (msg) => { DetectMatchStart(msg); };
                WebSocketManager.Instance.OnRawMessageReceived += _messageHandler;
            }
            _subscribed = true;
            // FE may call immediately after createUnityInstance, before this component's Start.
            if (WebSocketManager.Instance.IsFrontendControlled && !WebSocketManager.Instance.IsWaitingForFrontend)
                PrepareFrontendStart();
            if (WebSocketManager.Instance.IsConnected()) HandleConnectionProgress(1f, "Connected");
            // If the socket dispatched before this listener registered, its retained state
            // still activates the scene. The battle inbox remains owned by WebSocketManager.
            if (WebSocketManager.Instance.HasBattleState) OnMatchStartedDetected();
        }
    }

    void OnDestroy()
    {
        if (_subscribed && WebSocketManager.Instance != null)
        {
            WebSocketManager.Instance.OnConnectionProgress -= HandleConnectionProgress;
            WebSocketManager.Instance.OnConnectionError -= HandleConnectionError;
            WebSocketManager.Instance.OnFrontendStartRequested -= PrepareFrontendStart;
            if (_messageHandler != null)
            {
                WebSocketManager.Instance.OnRawMessageReceived -= _messageHandler;
            }
        }
    }

    void HandleConnectionProgress(float value, string status)
    {
        // invoked on main thread by WebSocketManager.Update
        // If we've already handled the first full connection, ignore subsequent lower updates
        if (_hasReachedFull)
        {
            // still update status if provided, but keep progress at full
            _status = status ?? string.Empty;
            return;
        }

        _targetProgress = Mathf.Clamp01(value);
        _status = status ?? string.Empty;
        if (_targetProgress >= 1f)
        {
            _hasReachedFull = true;
            Debug.Log($"LoaddingManager: reached full progress (hasReachedFull=true)");
            // Subscribe the configured client-input match; the socket already retains messages.
            if (!string.IsNullOrEmpty(autoSubscribeMatchId) &&
                (WebSocketManager.Instance == null || !WebSocketManager.Instance.IsFrontendControlled))
            {
                Debug.Log($"LoaddingManager: autoSubscribeMatchId present, subscribing={autoSubscribeMatchId}");
                SubscribeConfiguredMatch();
            }
            // begin preloading the target scene (allow activation only after start_result)
            if (loadSceneOnConnected && !string.IsNullOrEmpty(targetSceneName) && !_preloadStarted)
            {
                _preloadStarted = true;
                if (gameObject.activeInHierarchy)
                    StartCoroutine(PreloadAndForward(targetSceneName));
                else
                    CoroutineRunner.Run(PreloadAndForward(targetSceneName));
            }
        }
        else
        {
            if (loadingPanel != null) loadingPanel.SetActive(true);
        }
    }

    async void SubscribeConfiguredMatch()
    {
        if (WebSocketManager.Instance == null) return;
        // _messageHandler already set in TrySubscribe, no need to create again
        try
        {
            await WebSocketManager.Instance.SubscribeToMatch(autoSubscribeMatchId, 0);
            Debug.Log($"LoaddingManager: SubscribeToMatch called for matchId={autoSubscribeMatchId}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"LoaddingManager: SubscribeToMatch failed: {ex}");
        }
    }

    void DetectMatchStart(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        try
        {
            var j = JObject.Parse(msg);
            var type = j.Value<string>("type");
            if (!string.IsNullOrEmpty(type))
            {
                // Handle start_result: check requestId match, error or success
                if (type == "meme_battle_start_result")
                {
                    string incomingRequestId = j.Value<string>("requestId");
                    string raw = j.Value<string>("raw");
                    JToken inner = null;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        try { var parsed = JToken.Parse(raw); inner = (parsed.Type == JTokenType.Array && parsed.HasValues) ? parsed.First : parsed; }
                        catch { }
                    }

                    if (inner != null && inner.Type == JTokenType.Object && string.IsNullOrEmpty(incomingRequestId))
                    {
                        incomingRequestId = inner.Value<string>("requestId");
                    }

                    // expected request id from WebSocketManager (if set). If empty, accept any start_result.
                    string expected = WebSocketManager.Instance != null ? WebSocketManager.Instance.startMatchRequestId : null;
                    if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(incomingRequestId) && !string.Equals(expected, incomingRequestId, StringComparison.Ordinal))
                    {
                        // Not the start_result we are waiting for — ignore
                        return;
                    }

                    // if inner indicates error, show it
                    if (inner != null && inner.Type == JTokenType.Object)
                    {
                        var err = inner["error"];
                        if (err != null)
                        {
                            var code = err.Value<string>("code");
                            var message = err.Value<string>("message");
                            _status = MapErrorCodeToUserMessage(code ?? "MATCH_START_FAILED", message ?? "Start failed");
                            if (loadingPanel != null) loadingPanel.SetActive(true);
                            CancelInvoke(nameof(LoadTargetScene));
                            CancelInvoke(nameof(HidePanel));
                            return;
                        }
                        var state = inner.Value<string>("state");
                        if (string.IsNullOrEmpty(state) || state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) || state.Equals("FINISHED", StringComparison.OrdinalIgnoreCase))
                        {
                            OnMatchStartedDetected();
                            return;
                        }
                    }
                    // fallback: if no inner object, but wrapped result may exist under j["result"] (older path)
                    var result = j["result"];
                    if (result != null && result.Type == JTokenType.Object)
                    {
                        var state2 = result.Value<string>("state");
                        if (string.IsNullOrEmpty(state2) || state2.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) || state2.Equals("FINISHED", StringComparison.OrdinalIgnoreCase))
                        {
                            OnMatchStartedDetected();
                            return;
                        }
                    }
                }

                if (type == "meme_battle_snapshot")
                {
                    // snapshot indicates match state available -> consider match started
                    OnMatchStartedDetected();
                    return;
                }
                if (type == "meme_battle_start_result")
                {
                    // start result from server indicates match created/active
                    var result = j["result"];
                    if (result != null)
                    {
                        var state = result.Value<string>("state");
                        if (string.IsNullOrEmpty(state) || state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            OnMatchStartedDetected();
                            return;
                        }
                    }
                    else
                    {
                        // Some messages are enqueued with a raw string under 'raw' (when SocketIO client could not parse into JObject).
                        var raw = j.Value<string>("raw");
                        if (!string.IsNullOrEmpty(raw))
                        {
                            try
                            {
                                var inner = JObject.Parse(raw);
                                var innerState = inner.Value<string>("state");
                                if (string.IsNullOrEmpty(innerState) || innerState.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                                {
                                    OnMatchStartedDetected();
                                    return;
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            // if structure differs, still consider start result as match started
                            OnMatchStartedDetected();
                            return;
                        }
                    }
                }
                if (type == "meme_battle_event")
                {
                    var ev = j["event"];
                    if (ev != null)
                    {
                        var eventType = ev.Value<string>("eventType");
                        if (!string.IsNullOrEmpty(eventType) && (eventType == "MATCH_STARTED" || eventType == "MATCH_CREATED"))
                        {
                            OnMatchStartedDetected();
                            return;
                        }
                    }
                }
                // Battle result indicates match has concluded and can trigger scene activation
                if (type == "meme_battle_battle_result")
                {
                    OnMatchStartedDetected();
                    return;
                }
            }
        }
        catch { }
    }

    void OnMatchStartedDetected()
    {
        if (_matchStarted) return;
        _matchStarted = true;
        Debug.Log($"LoaddingManager: OnMatchStartedDetected called (_matchStarted=true). _hasReachedFull={_hasReachedFull} _sceneReady={_sceneReady} _preloadStarted={_preloadStarted}");
        // When match starts, ensure scene activation if preload already finished
        // If preload hasn't started, start it now so scene can be loaded and activated.
        if (!_preloadStarted && loadSceneOnConnected && !string.IsNullOrEmpty(targetSceneName))
        {
            _preloadStarted = true;
            if (gameObject.activeInHierarchy)
                StartCoroutine(PreloadAndForward(targetSceneName));
            else
                CoroutineRunner.Run(PreloadAndForward(targetSceneName));
        }

        // Attempt to activate scene if ready and both conditions satisfied
        TryActivateScene();
    }

    void HandleConnectionError(string code, string msg)
    {
        // keep loading panel visible and show friendly error
        if (loadingPanel != null) loadingPanel.SetActive(true);
        _targetProgress = 0f;
        _status = MapErrorCodeToUserMessage(code, msg ?? string.Empty);
        // prevent auto hide / scene load
        CancelInvoke(nameof(LoadTargetScene));
        CancelInvoke(nameof(HidePanel));
    }

    void LoadTargetScene()
    {
        // Async load the target scene (single mode)
        if (string.IsNullOrEmpty(targetSceneName)) return;
        // kept for backward compatibility, but main flow uses PreloadAndForward
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(PreloadAndForward(targetSceneName));
        }
        else
        {
            CoroutineRunner.Run(PreloadAndForward(targetSceneName));
        }
    }

    System.Collections.IEnumerator PreloadAndForward(string sceneName)
    {
        PersistAcrossScenes();
        var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        if (op == null) yield break;
        // don't activate until both scene ready and match started
        op.allowSceneActivation = false;
        _loadOp = op;
        Debug.Log($"LoaddingManager: Preload started for {sceneName}, initial progress={op.progress}");

        // wait until scene is loaded to the activation point (progress >= 0.9)
        while (op.progress < 0.9f)
        {
            yield return null;
        }
        _sceneReady = true;
        Debug.Log($"LoaddingManager: Preload reached ready (progress={op.progress}), _sceneReady=true");
        // attempt activation if both conditions met
        TryActivateScene();

        // wait until scene activation completes
        while (!op.isDone)
        {
            yield return null;
        }

        // Actual readiness replaces a fixed two-frame delay. The socket keeps every event
        // while GameManager and its fighters initialize, with no loading timeout.
        while (GameManager.Instance == null || !GameManager.Instance.IsReadyForBattleEvents)
            yield return null;

        if (hideDelay > 0f) yield return new WaitForSecondsRealtime(hideDelay);
        HidePanel();
        IsBattlePresentationReady = true;

        // destroy loading manager if it was moved to DontDestroyOnLoad
        if (_persistAcrossScenes) Destroy(gameObject);
    }

    void TryActivateScene()
    {
        Debug.Log($"LoaddingManager: TryActivateScene called. _loadOp={( _loadOp!=null ? "set" : "null")}, _sceneReady={_sceneReady}, _matchStarted={_matchStarted}, _hasReachedFull={_hasReachedFull}");
        if (_loadOp == null)
        {
            Debug.Log("LoaddingManager: no load operation available to activate.");
            return;
        }
        if (!_sceneReady)
        {
            Debug.Log($"LoaddingManager: scene not ready (progress={_loadOp.progress}).");
            return;
        }
        if (!_matchStarted)
        {
            Debug.Log("LoaddingManager: match not started yet; will not activate scene.");
            return;
        }
        // allow activation
        Debug.Log("LoaddingManager: Activating scene now.");
        _loadOp.allowSceneActivation = true;
    }

    void HidePanel()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    void Update()
    {
        if (!_subscribed)
            TrySubscribe();

        // smooth progress towards target
        if (progressSlider != null)
        {
            if (_hasReachedFull)
            {
                progressSlider.value = 1f;
                if (percentText != null) percentText.text = "100%";
            }
            else
            {
                float cur = progressSlider.value;
                float next = Mathf.MoveTowards(cur, _targetProgress, Time.deltaTime * 0.5f);
                progressSlider.value = next;
                if (percentText != null)
                {
                    int pct = Mathf.RoundToInt(progressSlider.value * 100f);
                    percentText.text = pct.ToString() + "%";
                }
            }
        }

        if (statusText != null)
        {
            statusText.text = _status ?? string.Empty;
        }
    }

    string MapErrorCodeToUserMessage(string code, string raw)
    {
        switch (code)
        {
            case "SOCKET_AUTH_REQUIRED":
            case "UNITY_AUTH_REQUIRED":
                return "Unable to authenticate the Unity renderer";
            case "UNITY_AUTH_INVALID":
                return "Invalid Unity authentication";
            case "UNITY_EVENT_FORBIDDEN":
                return "Unity renderer is not allowed to call this event";
            case "MATCH_SUBSCRIBE_INVALID":
                return "Invalid subscription data";
            case "MATCH_NOT_FOUND":
                return "Match not found";
            case "SOCKET_RECONNECT_FAILED":
                return "Unable to reconnect to the game server";
            default:
                return raw;
        }
    }
}
