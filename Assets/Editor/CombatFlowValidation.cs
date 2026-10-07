using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CombatFlowValidation
{
    private const string RequestPath = "Temp/CombatFlowValidation.request";
    private const string ReportPath = "Temp/CombatFlowValidation.txt";
    private static readonly List<Object> Created = new List<Object>();
    private static readonly List<string> Results = new List<string>();
    private static Scene _testScene;

    static CombatFlowValidation()
    {
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Combat/Validate Triplet Queue")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run combat validation outside Play mode.");
        Results.Clear();
        _testScene = EditorSceneManager.NewPreviewScene();
        try
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Resources/Combat/TripletCombat.controller");
            Require(controller != null, "Controller imports");
            Require(controller.animationClips.Distinct().Count() == 6, "Six distinct override slots");
            foreach (string name in new[] { "Attack", "Hit", "GetUp", "Victory" })
                Require(controller.layers[0].stateMachine.states.Single(state => state.state.name == name)
                    .state.behaviours.OfType<AnimationEndAction>().Count() == 1, name + " end callback wired");

            CharacterCombat left = Actor("Validation Left", Vector3.zero);
            CharacterCombat right = Actor("Validation Right", Vector3.right);
            CombatTripletData light = Move("Light", 0.3f, 0.5f, 0f);
            CombatTripletData heavy = Move("Heavy", 0.3f, 0.5f, 0.7f);
            left.lightCombatMoves = right.lightCombatMoves = new[] { light };
            left.heavyCombatMoves = right.heavyCombatMoves = new[] { heavy };
            Require(left.Initialize() && right.Initialize(), "Both fighters initialize");
            Require(left.Animator.runtimeAnimatorController != right.Animator.runtimeAnimatorController,
                "Per-character override isolation");

            int leftEnds = 0;
            int rightEnds = 0;
            left.SequenceEnded += (actor, id, completed) => { if (completed) leftEnds++; };
            right.SequenceEnded += (actor, id, completed) => { if (completed) rightEnds++; };
            // Match direct BattleScene input: let Idle run before the first key press.
            // Starting immediately after Rebind hid the trigger + Play double-entry bug.
            Tick(left, right, 120);
            Require(left.ExecuteAttack(heavy, right), "Heavy pair starts");
            Require(left.IsBusy && right.IsBusy && leftEnds == 0 && rightEnds == 0,
                "Attack and Hit remain busy at time zero after waiting in Idle");
            Require(!left.ExecuteAttack(light, right), "Busy pair refuses re-entry");
            Tick(left, right, 25);
            Require(leftEnds == 1 && rightEnds == 0 && right.IsBusy, "Attack end does not finish Hit/GetUp");
            Tick(left, right, 100);
            Require(rightEnds == 1 && !right.IsBusy, "GetUp completes victim sequence once");
            right.NotifyAnimationEnded(Animator.StringToHash("Base Layer.GetUp"), right.PlaybackId - 1, true);
            Require(rightEnds == 1, "Stale callbacks ignored");

            Require(left.ExecuteAttack(light, right), "Light pair starts after heavy");
            Tick(left, right, 50);
            Require(rightEnds == 2 && !right.IsBusy, "Null GetUp clears previous heavy GetUp");
            Require(left.ExecuteAttack(heavy, right, true), "Lethal pair starts");
            Tick(left, right, 50);
            Require(right.IsDead && !right.IsBusy && right.Animator.speed == 0f &&
                right.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Hit"),
                "Lethal hit finishes and holds without GetUp");
            left.ResetCombat();
            right.ResetCombat();

            ValidateLoadingDelivery(left, right);

            var managerRoot = new GameObject("Queue Validation");
            SceneManager.MoveGameObjectToScene(managerRoot, _testScene);
            Created.Add(managerRoot);
            var manager = managerRoot.AddComponent<GameManager>();
            manager.enableLocalInputTesting = false;
            manager.choreographAttackPositions = false;
            manager.debugMode = false;
            manager.leftCombat = left;
            manager.rightCombat = right;

            Feed(manager, "first", "TURN_STARTED", 1, new JObject { ["turnNumber"] = 1 });
            Feed(manager, "first", "ARGUMENT_SELECTED", 2, Argument("bot_a", "bot_b", "attack_heavy"));
            Require(manager.PendingEventCount == 1, "Incomplete turn remains one stack");
            Feed(manager, "first", "DAMAGE_APPLIED", 3, Damage("bot_a", "bot_b", 900));
            Feed(manager, "first", "HP_CHANGED", 4, new JObject { ["characterId"] = "bot_b", ["hpAfterAtomic"] = 900 });
            Feed(manager, "second", "ARGUMENT_SELECTED", 5, Argument("bot_b", "bot_a", "attack_light"));
            Feed(manager, "second", "DAMAGE_APPLIED", 6, Damage("bot_b", "bot_a", 800));
            Feed(manager, "second", "DAMAGE_APPLIED", 6, Damage("bot_b", "bot_a", 800));
            Require(manager.PendingEventCount == 2, "Duplicate delivery does not add a stack");
            int initialLeft = left.PlaybackId;
            int initialRight = right.PlaybackId;
            PumpQueue(manager, left, right);
            Require(manager.QueueError == null && manager.PendingEventCount == 0, "Backend stacks drain sequentially");
            Require(left.PlaybackId == initialLeft + 2 && right.PlaybackId == initialRight + 2,
                "Exactly one matched pair per backend turn");
            var hpKeys = (HashSet<string>)typeof(GameManager).GetField("_hpChangedAppliedKeys",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            Require(hpKeys.Count == 2, "DAMAGE_APPLIED plus HP_CHANGED writes HP once per turn");
            Feed(manager, "first", "HP_CHANGED", 7, new JObject { ["characterId"] = "bot_b", ["hpAfterAtomic"] = 900 });
            PumpQueue(manager, left, right);
            Require(left.PlaybackId == initialLeft + 2 && right.PlaybackId == initialRight + 2,
                "Late HP event does not replay combat");

            left.lightCombatMoves = Array.Empty<CombatTripletData>();
            Feed(manager, "invalid", "ARGUMENT_SELECTED", 8, Argument("bot_a", "bot_b", "attack_light"));
            Feed(manager, "invalid", "DAMAGE_APPLIED", 9, Damage("bot_a", "bot_b", 700));
            Feed(manager, "later", "ARGUMENT_SELECTED", 10, Argument("bot_b", "bot_a", "attack_light"));
            Feed(manager, "later", "DAMAGE_APPLIED", 11, Damage("bot_b", "bot_a", 600));
            PumpQueue(manager, left, right);
            Require(manager.QueueError != null && manager.PendingEventCount == 1,
                "Invalid pool faults queue without starting the next stack");

            ValidateScene();
            Results.Add("PASS");
        }
        catch (Exception exception)
        {
            Results.Add("FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            for (int index = Created.Count - 1; index >= 0; index--)
                if (Created[index] != null) Object.DestroyImmediate(Created[index]);
            Created.Clear();
            EditorSceneManager.ClosePreviewScene(_testScene);
            Directory.CreateDirectory("Temp");
            File.WriteAllLines(ReportPath, Results);
        }
    }

    private static void ValidateScene()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
        try
        {
            var manager = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameManager>(true))
                .Single();
            Require(manager.leftCombat != null && manager.rightCombat != null, "BattleScene fighter references migrated");
            foreach (var actor in scene.GetRootGameObjects().SelectMany(
                root => root.GetComponentsInChildren<CharacterCombat>(true)))
            {
                Require(actor.lightCombatMoves.Length > 0 && actor.lightCombatMoves.All(move => move.IsValid),
                    actor.name + " light pool");
                Require(actor.heavyCombatMoves.Length > 0 && actor.heavyCombatMoves.All(move => move.IsValid),
                    actor.name + " heavy pool");
                Require(actor.combatController != null && actor.targetAttackClip != null &&
                    actor.targetHitClip != null && actor.targetGetUpClip != null, actor.name + " controller slots");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void ValidateLoadingDelivery(CharacterCombat left, CharacterCombat right)
    {
        foreach (bool frontend in new[] { false, true })
        foreach (bool slowLoad in new[] { false, true })
        {
            string label = (frontend ? "Frontend" : "Client Input") + (slowLoad ? " slow" : " fast");
            var socketRoot = new GameObject("Loading Inbox " + label);
            var managerRoot = new GameObject("Loading Game " + label);
            SceneManager.MoveGameObjectToScene(socketRoot, _testScene);
            SceneManager.MoveGameObjectToScene(managerRoot, _testScene);
            Created.Add(socketRoot); Created.Add(managerRoot);
            var socket = socketRoot.AddComponent<WebSocketManager>();
            Set(socket, "_startupMode", frontend ? WebSocketManager.StartupMode.Frontend : WebSocketManager.StartupMode.ClientInput);
            var manager = managerRoot.AddComponent<GameManager>();
            manager.leftCombat = left; manager.rightCombat = right;
            manager.enableLocalInputTesting = false; manager.choreographAttackPositions = false; manager.debugMode = false;
            var history = new JArray
            {
                Event(1, null, "MATCH_CREATED", new JObject { ["characterIds"] = new JArray("bot_a", "bot_b"), ["initialHpAtomic"] = 1000 }),
                Event(2, "one", "ARGUMENT_SELECTED", Argument("bot_a", "bot_b", "attack_light")),
                Event(3, "one", "DAMAGE_APPLIED", Damage("bot_a", "bot_b", 900)),
                Event(4, "one", "HP_CHANGED", new JObject { ["characterId"] = "bot_b", ["hpAfterAtomic"] = 900 }),
                Event(5, "two", "ARGUMENT_SELECTED", Argument("bot_b", "bot_a", "attack_heavy")),
                Event(6, "two", "DAMAGE_APPLIED", Damage("bot_b", "bot_a", 800)),
                Event(7, "two", "HP_CHANGED", new JObject { ["characterId"] = "bot_a", ["hpAfterAtomic"] = 800 }),
                Event(8, null, "WINNER_DECLARED", new JObject())
            };
            var snapshot = new JObject { ["matchId"] = "loading-validation", ["state"] = "FINISHED", ["latestSequence"] = 8,
                ["characterHpAtomic"] = new JObject { ["bot_a"] = 0, ["bot_b"] = 900 },
                ["characterMaxHpAtomic"] = new JObject { ["bot_a"] = 1000, ["bot_b"] = 1000 } };
            Set(manager, "_socket", socket);
            // State may arrive before its historical events. It cannot advance the replay cursor.
            Retain(socket, new JObject { ["type"] = "meme_battle_start_result", ["result"] = new JObject {
                ["matchId"] = "loading-validation", ["snapshot"] = snapshot.DeepClone() } });
            Require((long)Get(socket, "_lastReceivedSequence") == 0, label + " start snapshot does not acknowledge missing history");
            Require(socket.HasBattleState, label + " late LoadingManager can detect already received match state");
            if (slowLoad) Retain(socket, new JObject { ["type"] = "meme_battle_snapshot", ["snapshot"] = snapshot, ["events"] = new JArray(history.Reverse()) });
            int buffered = socket.PendingBattleMessageCount;
            for (int frame = 0; frame < (slowLoad ? 600 : 1); frame++) Invoke(manager, "ReceivePendingBattleMessages");
            Require(socket.PendingBattleMessageCount == buffered && manager.PendingEventCount == 0,
                label + " no event is consumed before battle initialization, regardless of delay");
            Invoke(manager, "Start"); Set(manager, "_socket", socket);
            Require(manager.IsReadyForBattleEvents, label + " explicit readiness after initialization");
            var loadingRoot = new GameObject("Loading Presentation " + label); Created.Add(loadingRoot);
            SceneManager.MoveGameObjectToScene(loadingRoot, _testScene);
            var loading = loadingRoot.AddComponent<LoaddingManager>();
            loading.loadSceneOnConnected = false; // Verify detection without loading a scene in this preview fixture.
            var singleton = typeof(WebSocketManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previousSocket = singleton.GetValue(null);
            try
            {
                singleton.SetValue(null, socket);
                Invoke(loading, "TrySubscribe");
                Require((bool)Get(loading, "_matchStarted"), label + " late loading listener detects match state without receiving a new event");
            }
            finally
            {
                try { Invoke(loading, "OnDestroy"); }
                finally { singleton.SetValue(null, previousSocket); }
            }
            Set(manager, "_loadingScene", loading);
            buffered = socket.PendingBattleMessageCount;
            Invoke(manager, "ReceivePendingBattleMessages");
            Require(socket.PendingBattleMessageCount == buffered && manager.PendingEventCount == 0,
                label + " initialized game waits until the loading screen releases playback");
            typeof(LoaddingManager).GetProperty("IsBattlePresentationReady").GetSetMethod(true).Invoke(loading, new object[] { true });
            if (!slowLoad) Retain(socket, new JObject { ["type"] = "meme_battle_snapshot", ["snapshot"] = snapshot, ["events"] = history });
            // Retransmit the same history. Observe a live message arriving during the handoff.
            Retain(socket, new JObject { ["type"] = "meme_battle_snapshot", ["snapshot"] = snapshot.DeepClone(), ["events"] = history.DeepClone() });
            int before = socket.PendingBattleMessageCount;
            Retain(socket, new JObject { ["type"] = "meme_battle_event", ["event"] = history[3].DeepClone() });
            Require(socket.PendingBattleMessageCount == before, label + " duplicate live event is retained once");
            Require((long)Get(socket, "_lastReceivedSequence") == 8, label + " reconnect cursor follows retained contiguous history");
            left.ResetCombat(); right.ResetCombat();
            int completed = 0;
            Action<CharacterCombat, int, bool> onComplete = (fighter, playback, succeeded) =>
            {
                if (!succeeded || (bool)Get(manager, "_matchEnded"))
                    throw new InvalidOperationException("Combat was interrupted or result ran before animation completion");
                completed++;
            };
            Invoke(manager, "ReceivePendingBattleMessages");
            Retain(socket, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(9, null, "VALIDATION_LIVE", new JObject()) });
            Invoke(manager, "ReceivePendingBattleMessages");
            Require(socket.PendingBattleMessageCount == 0, label + " old backlog and new live message reach one game queue");
            left.SequenceEnded += onComplete; right.SequenceEnded += onComplete;
            try { PumpQueue(manager, left, right); }
            finally { left.SequenceEnded -= onComplete; right.SequenceEnded -= onComplete; }
            Require(manager.QueueError == null && manager.PendingEventCount == 0 &&
                completed == 4 && ((HashSet<string>)Get(manager, "_playedTurns")).Count == 2,
                label + " FINISHED history plays both complete combat pairs exactly once");
            Require((bool)Get(manager, "_matchEnded"), label + " result follows combat playback");
            left.ResetCombat(); right.ResetCombat();
        }

        var burstRoot = new GameObject("Large Loading Inbox"); Created.Add(burstRoot);
        SceneManager.MoveGameObjectToScene(burstRoot, _testScene);
        var burst = burstRoot.AddComponent<WebSocketManager>();
        for (int i = 1; i <= 1024; i++) Retain(burst, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(i, null, "VALIDATION", new JObject()) });
        Require(burst.PendingBattleMessageCount == 1024, "Long loads retain a backlog larger than the per-frame dispatch budget");
        var bulkRoot = new GameObject("Budgeted Game Inbox"); Created.Add(bulkRoot);
        SceneManager.MoveGameObjectToScene(bulkRoot, _testScene);
        var bulkGame = bulkRoot.AddComponent<GameManager>();
        bulkGame.leftCombat = left; bulkGame.rightCombat = right; bulkGame.enableLocalInputTesting = false;
        Invoke(bulkGame, "Start"); Set(bulkGame, "_socket", burst);
        Invoke(bulkGame, "ReceivePendingBattleMessages");
        Require(burst.PendingBattleMessageCount == 768 && bulkGame.PendingEventCount == 256,
            "Per-frame delivery budget leaves remaining events retained");
        for (int frame = 0; frame < 3; frame++) Invoke(bulkGame, "ReceivePendingBattleMessages");
        var stacks = ((IEnumerable)Get(bulkGame, "_queue")).Cast<object>();
        var sequences = stacks.SelectMany(stack => (List<MemeBattleEvent>)stack.GetType().GetField("events").GetValue(stack))
            .Select(ev => ev.sequence);
        Require(burst.PendingBattleMessageCount == 0 && bulkGame.PendingEventCount == 1024 &&
            sequences.SequenceEqual(Enumerable.Range(1, 1024).Select(v => (long)v)),
            "Entire large backlog delivered FIFO without eviction across four frames");
        bulkGame.ClearEventQueue();
        Retain(burst, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(1025, null, "VALIDATION", new JObject()) });
        try { burst.DispatchNextBattleMessage(raw => throw new InvalidOperationException("Expected consumer failure")); }
        catch (InvalidOperationException) { }
        Require(burst.PendingBattleMessageCount == 1, "A failed consumer retains its message for retry");
        var orderedRoot = new GameObject("Live Overtakes History"); Created.Add(orderedRoot);
        SceneManager.MoveGameObjectToScene(orderedRoot, _testScene);
        var ordered = orderedRoot.AddComponent<WebSocketManager>();
        Retain(ordered, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(3, null, "WINNER_DECLARED", new JObject()) });
        Require(ordered.PendingBattleMessageCount == 1 && !ordered.DispatchNextBattleMessage(raw => { }) &&
            (long)Get(ordered, "_lastReceivedSequence") == 0,
            "Live winner waits for missing historical events; reconnect cursor stays before the gap");
        Retain(ordered, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(1, null, "MATCH_CREATED", new JObject()) });
        Retain(ordered, new JObject { ["type"] = "meme_battle_event", ["event"] = Event(2, null, "TURN_RESOLVED", new JObject()) });
        long sequence = 0;
        while (ordered.DispatchNextBattleMessage(raw => {
            if (JObject.Parse(raw)["event"].Value<long>("sequence") != ++sequence)
                throw new InvalidOperationException("Live/history order was inverted");
        })) { }
        Require(sequence == 3 && (long)Get(ordered, "_lastReceivedSequence") == 3,
            "Missing history releases the live winner strictly in sequence order");
    }

    private static JObject Event(long sequence, string turn, string type, JObject payload) => new JObject {
        ["matchId"] = "loading-validation", ["turnId"] = turn, ["eventId"] = sequence.ToString(),
        ["sequence"] = sequence, ["eventType"] = type, ["payload"] = payload };
    private static void Retain(WebSocketManager socket, JObject message) => Invoke(socket, "RetainBattleMessage", message.ToString());
    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static CharacterCombat Actor(string name, Vector3 position)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, _testScene);
        Created.Add(root);
        root.transform.position = position;
        new GameObject("Probe").transform.SetParent(root.transform, false);
        var animator = root.AddComponent<Animator>();
        animator.applyRootMotion = false;
        var combat = root.AddComponent<CharacterCombat>();
        combat.idleAnim = Clip("Idle", 1f);
        return combat;
    }

    private static AnimationClip Clip(string name, float length)
    {
        var clip = new AnimationClip { name = name };
        clip.SetCurve("Probe", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, length, 1f));
        Created.Add(clip);
        return clip;
    }

    private static CombatTripletData Move(string name, float attack, float hit, float getUp)
    {
        return new CombatTripletData
        {
            moveName = name, attackAnim = Clip(name + " Attack", attack), hitAnim = Clip(name + " Hit", hit),
            getUpAnim = getUp > 0f ? Clip(name + " GetUp", getUp) : null, attackRange = 2f
        };
    }

    private static void Tick(CharacterCombat left, CharacterCombat right, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            left.Animator.Update(0.02f);
            right.Animator.Update(0.02f);
        }
    }

    private static void PumpQueue(GameManager manager, CharacterCombat left, CharacterCombat right)
    {
        var method = typeof(GameManager).GetMethod("RunQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        var routines = new Stack<IEnumerator>();
        routines.Push((IEnumerator)method.Invoke(manager, null));
        try
        {
            int frames = 0;
            while (routines.Count > 0 && frames++ < 1000)
            {
                IEnumerator routine = routines.Peek();
                if (!routine.MoveNext())
                {
                    (routines.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (routine.Current is IEnumerator nested) routines.Push(nested);
                else Tick(left, right, 1);
            }
            Require(routines.Count == 0, "Queue completes within validation frame budget");
        }
        finally
        {
            while (routines.Count > 0) (routines.Pop() as IDisposable)?.Dispose();
        }
    }

    private static JObject Argument(string attacker, string receiver, string animation) =>
        new JObject { ["actorCharacterId"] = attacker, ["targetCharacterId"] = receiver, ["animationId"] = animation };

    private static JObject Damage(string attacker, string receiver, long hp) =>
        new JObject { ["actorCharacterId"] = attacker, ["targetCharacterId"] = receiver,
            ["hpAfterAtomic"] = hp, ["damageAtomic"] = 100 };

    private static void Feed(GameManager manager, string turn, string type, long sequence, JObject payload)
    {
        manager.ApplyRawMessage(new JObject
        {
            ["type"] = "meme_battle_event",
            ["event"] = new JObject { ["matchId"] = "validation", ["turnId"] = turn, ["eventId"] = sequence.ToString(),
                ["sequence"] = sequence, ["eventType"] = type, ["payload"] = payload }
        }.ToString());
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Results.Add("OK: " + label);
    }
}
