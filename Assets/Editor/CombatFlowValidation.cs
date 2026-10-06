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
