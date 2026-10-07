using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class WebSocketManager
{
    // Main-thread inbox lives with the persistent socket, independently of scene load speed.
    // No timeout or size limit: loading must never discard the beginning of a battle.
    private readonly Queue<string> _battleMessages = new Queue<string>();
    private readonly HashSet<long> _retainedSequences = new HashSet<long>();
    private readonly HashSet<string> _retainedEventIds = new HashSet<string>();
    private readonly SortedDictionary<long, string> _pendingSequenceEvents = new SortedDictionary<long, string>();
    private string _sequenceMatchId;

    public int PendingBattleMessageCount => _battleMessages.Count + _pendingSequenceEvents.Count;
    public bool HasBattleState { get; private set; }

    /// <summary>GameManager calls this on the main thread after its Start has completed.</summary>
    public bool DispatchNextBattleMessage(Action<string> receiver)
    {
        if (receiver == null || _battleMessages.Count == 0) return false;
        // A failed handoff leaves the message in the inbox, rather than losing it.
        receiver(_battleMessages.Peek());
        _battleMessages.Dequeue();
        return true;
    }

    private static JToken ReadSocketPayload(string raw)
    {
        JToken token = JToken.Parse(raw);
        if (token is JArray args && args.Count == 1) token = args[0];
        if (token?.Type == JTokenType.String) token = JToken.Parse(token.Value<string>());
        return token;
    }

    private void RetainBattleMessage(string json)
    {
        var message = JObject.Parse(json);
        string type = message.Value<string>("type");
        if (type == "meme_battle_event")
        {
            RetainBattleEvent(message["event"]?.ToObject<MemeBattleEvent>());
            return;
        }

        JToken result = message["result"];
        if (type == "meme_battle_start_result" && result == null && !string.IsNullOrEmpty(message.Value<string>("raw")))
            result = ReadSocketPayload(message.Value<string>("raw"));
        if (type == "meme_battle_start_result" && result is JObject startResult)
        {
            string requestId = startResult.Value<string>("requestId");
            if (!string.IsNullOrEmpty(startMatchRequestId) && !string.IsNullOrEmpty(requestId) &&
                startMatchRequestId != requestId) return;
        }
        string matchId = type == "meme_battle_snapshot"
            ? message["snapshot"]?.Value<string>("matchId") : result?.Value<string>("matchId");
        if (!string.IsNullOrEmpty(matchId))
        {
            if (!string.IsNullOrEmpty(_currentMatchId) && _currentMatchId != matchId) return;
            _currentMatchId = matchId;
        }

        // A snapshot describes the current state. Its latestSequence must never acknowledge
        // historical events that Unity has not received, including FINISHED match replays.
        var history = (message["events"] ?? result?["events"])?.ToObject<List<MemeBattleEvent>>();
        history?.RemoveAll(ev => ev == null);
        history?.Sort((a, b) => a.sequence.CompareTo(b.sequence));
        bool hasReplay = history != null && history.Exists(IsNewBattleEvent);
        var snapshot = (message["snapshot"] ?? result?["snapshot"]) as JObject;
        var startError = result?["error"];
        bool startSucceeded = type == "meme_battle_start_result" && !string.IsNullOrEmpty(matchId) &&
            (startError == null || startError.Type == JTokenType.Null);
        if ((type == "meme_battle_snapshot" && snapshot != null) || startSucceeded ||
            type == "meme_battle_battle_result") HasBattleState = true;
        long snapshotSequence = snapshot?.Value<long?>("latestSequence") ?? _lastReceivedSequence;
        message["replayEvents"] = hasReplay || snapshotSequence != _lastReceivedSequence;
        message.Remove("events");
        RetainAndNotify(message.ToString(Formatting.None));
        if (history != null)
            foreach (var battleEvent in history) RetainBattleEvent(battleEvent);
        // The start response can describe a match which already emitted events. Ask for
        // its history too; duplicates from a live room subscription are retained only once.
        if (startSucceeded && IsConnected())
            _ = SubscribeToMatch(matchId, _lastReceivedSequence);
    }

    private bool IsNewBattleEvent(MemeBattleEvent battleEvent)
    {
        if (battleEvent == null || battleEvent.payload == null) return false;
        if (!string.IsNullOrEmpty(_currentMatchId) && battleEvent.matchId != _currentMatchId) return false;
        if (_sequenceMatchId != battleEvent.matchId) return true;
        return (battleEvent.sequence <= 0 || !_retainedSequences.Contains(battleEvent.sequence)) &&
            (string.IsNullOrEmpty(battleEvent.eventId) || !_retainedEventIds.Contains(battleEvent.eventId));
    }

    private void RetainBattleEvent(MemeBattleEvent battleEvent)
    {
        if (!IsNewBattleEvent(battleEvent)) return;
        HasBattleState = true;
        if (_sequenceMatchId != battleEvent.matchId)
        {
            _sequenceMatchId = battleEvent.matchId;
            _currentMatchId = battleEvent.matchId;
            _retainedSequences.Clear();
            _retainedEventIds.Clear();
            _pendingSequenceEvents.Clear();
            _lastReceivedSequence = 0;
        }
        string json = JsonConvert.SerializeObject(new { type = "meme_battle_event", @event = battleEvent });
        if (!string.IsNullOrEmpty(battleEvent.eventId)) _retainedEventIds.Add(battleEvent.eventId);
        if (battleEvent.sequence > 0)
        {
            _retainedSequences.Add(battleEvent.sequence);
            _pendingSequenceEvents.Add(battleEvent.sequence, json);
            // Live events can overtake the subscribe response. Wait for its missing history
            // and release a contiguous ordered prefix, so a winner cannot precede old turns.
            while (_pendingSequenceEvents.TryGetValue(_lastReceivedSequence + 1, out string next))
            {
                _pendingSequenceEvents.Remove(_lastReceivedSequence + 1);
                RetainAndNotify(next);
                _lastReceivedSequence++;
            }
        }
        else RetainAndNotify(json);
    }

    private void RetainAndNotify(string json)
    {
        _battleMessages.Enqueue(json);
        // LoadingScene observes messages to activate the scene; it never owns/replays the inbox.
        var observers = OnRawMessageReceived;
        if (observers == null) return;
        foreach (Action<string> observer in observers.GetInvocationList())
        {
            try { observer(json); }
            catch (Exception exception)
            { UnityEngine.Debug.LogError("WebSocketManager: message observer exception: " + exception); }
        }
    }
}
