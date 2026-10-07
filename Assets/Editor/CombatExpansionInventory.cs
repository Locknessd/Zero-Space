using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    /// <summary>Imported identities and serialized reachability, separate from motion verification.</summary>
    public static class CombatExpansionInventory
    {
        public const string Output = "GeneratedAssets/CombatExpansion";
        public const string Battle = "Assets/Scenes/BattleScene.unity";
        public static readonly string[] Sources =
        {
            "Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity",
            "Assets/Selected/Brutal_DoubleAxe_Anim/Animation/Humanoid",
            "Assets/Selected/FightingAnimsetPro/Animations/KB_Hits.fbx",
            "Assets/Selected/Mugen Tenshin Studio/Samurai Executions/Scenes/SampleScene.unity",
            "Assets/Selected/SlapFace",
            "Assets/Selected/Rough Pack Demo",
            "Assets/Selected/FightingAnimsetPro/Animations/KB_Punches.fbx",
            "Assets/Selected/FightingAnimsetPro/Animations/KB_Kicks.fbx"
        };

        [Serializable]
        public sealed class ClipRecord
        {
            public string path, guid, name, importRig, rootPolicy;
            public long localId;
            public float durationSeconds, frameRate, importScale;
            public bool humanoid, looping, rootCurves, motionCurves;
            public int curveCount;
            public string[] sourceScopes, registeredRoles, events;
            public string verification = "Pending motion preview and BattleScene validation";
        }

        [Serializable]
        public sealed class MoveRecord
        {
            public string fighter, pool, id, attack, reaction, recovery, avatar;
            public string attackerDriver, receiverDriver;
            public float range, reactionDelay;
            public bool paired, valid, hasPresentation;
            public int presentationCues;
        }

        [Serializable]
        public sealed class Report
        {
            public string utc, unityVersion, battlePath;
            public string[] verifiedSources;
            public ClipRecord[] clips;
            public MoveRecord[] moves;
        }

        public static string Identity(UnityEngine.Object asset)
        {
            if (!asset || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
                return "";
            return guid + ":" + id;
        }

        [MenuItem("Tools/Battle/Combat Expansion/Audit imported source coverage")]
        public static void Audit()
        {
            Directory.CreateDirectory(Output);
            var paths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string scope in Sources)
            {
                if (!File.Exists(scope) && !Directory.Exists(scope))
                    throw new FileNotFoundException("Required combat source is missing", scope);
                var assets = Directory.Exists(scope)
                    ? AssetDatabase.FindAssets("", new[] { scope }).Select(AssetDatabase.GUIDToAssetPath)
                    : scope.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                        ? AssetDatabase.GetDependencies(scope, true).AsEnumerable()
                        : new[] { scope };
                foreach (string path in assets)
                {
                    if (!paths.TryGetValue(path, out var scopes))
                        paths.Add(path, scopes = new HashSet<string>());
                    scopes.Add(scope);
                }
            }

            var scene = SceneManager.GetSceneByPath(Battle);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open BattleScene before auditing gameplay registration.");
            var moves = new List<MoveRecord>();
            foreach (var fighter in scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)))
            {
                AddMoves(fighter, fighter.lightCombatMoves, "Light", moves);
                AddMoves(fighter, fighter.heavyCombatMoves, "Heavy", moves);
            }

            var clips = new List<ClipRecord>();
            foreach (var entry in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string extension = Path.GetExtension(entry.Key).ToLowerInvariant();
                if (extension != ".fbx" && extension != ".anim" && extension != ".dae")
                    continue;
                var importer = AssetImporter.GetAtPath(entry.Key) as ModelImporter;
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(entry.Key).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                        continue;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id);
                    string identity = guid + ":" + id;
                    var roles = new List<string>();
                    foreach (var move in moves)
                    {
                        string prefix = move.fighter + "/" + move.pool + "/" + move.id;
                        if (move.attack == identity)
                            roles.Add(prefix + ":attacker");
                        if (move.reaction == identity)
                            roles.Add(prefix + ":receiver");
                        if (move.recovery == identity)
                            roles.Add(prefix + ":recovery");
                    }
                    clips.Add(new ClipRecord
                    {
                        path = entry.Key,
                        guid = guid,
                        localId = id,
                        name = clip.name,
                        durationSeconds = clip.length,
                        frameRate = clip.frameRate,
                        humanoid = clip.humanMotion,
                        looping = clip.isLooping,
                        rootCurves = clip.hasRootCurves,
                        motionCurves = clip.hasMotionCurves,
                        importRig = importer ? importer.animationType.ToString() : "Project animation",
                        importScale = importer ? importer.globalScale : 1,
                        rootPolicy = "Manual source graph; inspect per-action displacement before integration",
                        curveCount = AnimationUtility.GetCurveBindings(clip).Length,
                        sourceScopes = entry.Value.OrderBy(x => x).ToArray(),
                        registeredRoles = roles.ToArray(),
                        events = AnimationUtility.GetAnimationEvents(clip)
                            .Select(e => e.time.ToString("R") + "s:" + e.functionName).ToArray()
                    });
                }
            }
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                battlePath = Battle,
                verifiedSources = Sources,
                clips = clips.ToArray(),
                moves = moves.ToArray()
            };
            File.WriteAllText(Output + "/SourceInventory.json", JsonUtility.ToJson(report, true));
            File.WriteAllLines(Output + "/SourceCoverage.tsv", new[]
            {
                "GUID\tLocalID\tPath\tClip\tSeconds\tRig\tRegistered roles\tVerification"
            }.Concat(clips.Select(c => string.Join("\t", c.guid, c.localId, c.path, c.name,
                c.durationSeconds.ToString("R"), c.importRig, string.Join(";", c.registeredRoles), c.verification))));
            Debug.Log($"Combat source audit: {clips.Count} imported clips; {moves.Count} fighter move registrations.");
        }

        static void AddMoves(CharacterCombat fighter, CombatTripletData[] pool, string poolName,
            List<MoveRecord> records)
        {
            foreach (var move in pool ?? Array.Empty<CombatTripletData>())
            {
                if (move == null)
                    continue;
                var pair = move.sourcePair;
                var bank = fighter.battleSfx ? fighter.battleSfx.bank : null;
                var presentation = bank ? bank.FindMove(move) : null;
                records.Add(new MoveRecord
                {
                    fighter = fighter.name,
                    pool = poolName,
                    id = move.moveName,
                    attack = Identity(pair?.attack ? pair.attack : move.attackAnim),
                    reaction = Identity(pair?.reaction ? pair.reaction : move.hitAnim),
                    recovery = Identity(pair?.getUp ? pair.getUp : move.getUpAnim),
                    attackerDriver = Identity(pair?.attackerDriver),
                    receiverDriver = Identity(pair?.receiverDriver),
                    avatar = Identity(fighter.Animator ? fighter.Animator.avatar : null),
                    range = move.attackRange,
                    reactionDelay = pair?.reactionDelay ?? 0,
                    paired = pair != null && pair.Valid,
                    valid = move.IsValid,
                    hasPresentation = presentation != null,
                    presentationCues = presentation?.cues?.Length ?? 0
                });
            }
        }
    }
}
