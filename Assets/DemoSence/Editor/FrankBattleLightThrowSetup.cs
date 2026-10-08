using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string LightThrowReview = "GeneratedAssets/LightThrowReview";
        static readonly string[] LightThrowNames = { "JPBOM", "SIHO", "GSWING" };
        static bool IsNewLightThrow(CombatTripletData move) => LightThrowNames.Any(n => move.moveName == "Light_" + n);

        public static void BeginLightThrowPlayValidation() => CombatFeelPlayValidation.BeginLightThrows();

        static CombatTripletData MakeLightThrow(CharacterCombat fighter, string name, bool floorLift = true)
        {
            var library = AssetDatabase.LoadAssetAtPath<FrankUnarmedLibrary>(Vol10Output + "/UnarmedLibrary.asset");
            var template = fighter.lightCombatMoves.First(m => m.sourcePair != null && m.sourcePair.unarmedIndex >= 0 && !IsNewLightThrow(m));
            int index = Array.FindIndex(library.pairs, p => p.sourceName == name);
            if (index < 0) throw new Exception("Missing paired Light throw: " + name);
            var p = library.pairs[index]; var old = template.sourcePair;
            float offsetGain = old.receiverOffset.z / library.pairs[old.unarmedIndex].receiverOffset.z;
            var pair = new FrankBattlePair {
                attackerDriver = old.attackerDriver, receiverDriver = old.receiverDriver,
                attack = p.attacker, reaction = p.receiver, getUp = old.getUp,
                receiverOffset = p.receiverOffset * offsetGain, receiverRotation = p.receiverRotation,
                spacing = old.spacing, bodySpacing = old.bodySpacing, unarmedIndex = index,
                pepeAttacks = old.pepeAttacks, showWeapon = false
            };
            string motion = LightThrowReview + "/" + fighter.name + "_" + name + "_FloorMotion.csv";
            if (floorLift && File.Exists(motion)) pair.receiverFloorLift = File.ReadAllLines(motion).Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => Mathf.Max(0, .005f - ParseContactFloat(l.Split(',')[1]))).ToArray();
            return new CombatTripletData {
                moveName = "Light_" + name, attackAnim = pair.attack, hitAnim = pair.reaction, getUpAnim = pair.getUp,
                attackRange = Mathf.Abs(pair.receiverOffset.z), weapon = TrumpWeaponManager.WeaponType.None,
                skill = BattleSkill.None, sourcePair = pair
            };
        }

        sealed class ThrowBodyFloor : IDisposable
        {
            readonly SkinnedMeshRenderer[] skins;
            readonly Dictionary<SkinnedMeshRenderer, int[]> core = new Dictionary<SkinnedMeshRenderer, int[]>();
            readonly Mesh scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            readonly List<Vector3> vertices = new List<Vector3>();
            public ThrowBodyFloor(CharacterCombat receiver)
            {
                skins = receiver.Animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s => s.enabled && s.sharedMesh && s.sharedMesh.vertexCount > 1000).ToArray();
                var coreBones = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                    HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.Jaw }
                    .Select(b => receiver.Animator.GetBoneTransform(b)).Where(b => b).ToHashSet();
                foreach (var skin in skins)
                {
                    var bones = skin.bones; var weights = skin.sharedMesh.boneWeights;
                    var indices = new List<int>();
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var w = weights[i];
                        float weight = (coreBones.Contains(bones[w.boneIndex0]) ? w.weight0 : 0) +
                            (coreBones.Contains(bones[w.boneIndex1]) ? w.weight1 : 0) +
                            (coreBones.Contains(bones[w.boneIndex2]) ? w.weight2 : 0) +
                            (coreBones.Contains(bones[w.boneIndex3]) ? w.weight3 : 0);
                        if (weight >= .7f) indices.Add(i);
                    }
                    if (indices.Count < 50) throw new Exception("No body core vertices for throw landing.");
                    core.Add(skin, indices.ToArray());
                }
            }
            public Vector3 Sample()
            {
                var lowest = new Vector3(0, float.PositiveInfinity, 0);
                foreach (var skin in skins)
                {
                    scratch.Clear(); skin.BakeMesh(scratch, true); scratch.GetVertices(vertices);
                    foreach (int index in core[skin])
                    {
                        var point = skin.transform.TransformPoint(vertices[index]);
                        if (point.y < lowest.y) lowest = point;
                    }
                }
                return lowest;
            }
            public void Dispose() => Object.DestroyImmediate(scratch);
        }

        public static void BakeNewLightThrowFloorLift()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var source in fighters)
                foreach (string name in LightThrowNames)
                {
                    var move = MakeLightThrow(source, name, false); var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new Exception("Floor bake rejected throw.");
                    var pair = source.SourcePlayback; var data = new StringBuilder("time,coreY\n");
                    using (var floor = new ThrowBodyFloor(target))
                    for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * FrankBattlePair.ReceiverFloorSamplesPerSecond); frame++)
                    {
                        float time = Mathf.Min(pair.Duration, (float)frame / FrankBattlePair.ReceiverFloorSamplesPerSecond);
                        pair.EvaluateAt(time); float y = floor.Sample().y;
                        if (!float.IsFinite(y)) throw new Exception("Invalid body pose during floor bake.");
                        data.AppendLine(FormattableString.Invariant($"{time:F6},{y:F7}"));
                    }
                    File.WriteAllText($"{LightThrowReview}/{source.name}_{name}_FloorMotion.csv", data.ToString()); pair.Cancel();
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void RefineNewLightThrowLandings()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var selection = new StringBuilder("fighter,move,landing,time,bone,x,y,z,coreY,descent\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var source in fighters)
                foreach (string name in LightThrowNames)
                {
                    var move = MakeLightThrow(source, name); var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                    var windows = name == "GSWING" ? new[] { new Vector2(.28f, .40f), new Vector2(1.53f, 1.63f) } :
                        name == "JPBOM" ? new[] { new Vector2(1.03f, 1.12f) } : new[] { new Vector2(.67f, .94f) };
                    using (var floor = new ThrowBodyFloor(target))
                    for (int index = 0; index < windows.Length; index++)
                    {
                        var window = windows[index]; var csv = new StringBuilder("time,coreY,x,z,descent\n"); bool found = false;
                        float bestY = float.PositiveInfinity, bestTime = 0; Vector3 bestPoint = Vector3.zero;
                        pair.EvaluateAt(window.x - 1f / 480); float previous = floor.Sample().y;
                        for (int frame = 0; frame <= Mathf.CeilToInt((window.y - window.x) * 480); frame++)
                        {
                            float time = Mathf.Min(window.y, window.x + frame / 480f); pair.EvaluateAt(time);
                            var point = floor.Sample(); float descent = (previous - point.y) * 480; previous = point.y;
                            if (point.y < bestY) { bestY = point.y; bestTime = time; bestPoint = point; }
                            csv.AppendLine(FormattableString.Invariant($"{time:F6},{point.y:F6},{point.x:F6},{point.z:F6},{descent:F6}"));
                            if (found || point.y > .012f || descent < 1) continue;
                            var bone = NearestContactBone(target, point);
                            var local = target.Animator.GetBoneTransform(bone).InverseTransformPoint(point);
                            selection.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{index},{time:F6},{bone},{local.x:F7},{local.y:F7},{local.z:F7},{point.y:F6},{descent:F6}"));
                            found = true;
                        }
                        // The first GSWING takedown settles its core a few cm
                        // above the plane while elbows/legs meet the floor. Use
                        // its exact lowest pose instead of inventing a late hit.
                        if (!found && name == "GSWING" && index == 0 && bestY <= .06f)
                        {
                            pair.EvaluateAt(bestTime); var bone = NearestContactBone(target, bestPoint);
                            var local = target.Animator.GetBoneTransform(bone).InverseTransformPoint(bestPoint);
                            selection.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{index},{bestTime:F6},{bone},{local.x:F7},{local.y:F7},{local.z:F7},{bestY:F6},0\n").TrimEnd());
                            found = true;
                        }
                        File.WriteAllText($"{LightThrowReview}/{source.name}_{name}_{index}_FloorFine.csv", csv.ToString());
                        if (!found) throw new Exception("No descending body/floor contact: " + source.name + "/" + name + "/" + index);
                    }
                    pair.Cancel(); File.WriteAllText(LightThrowReview + "/Landings.csv", selection.ToString());
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static BattleSfxBank.Cue[] NewLightThrowCues(string fighter, CombatTripletData move)
        {
            var landings = File.ReadAllLines(LightThrowReview + "/Landings.csv").Skip(1).Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(',')).Where(r => r[0] == fighter && r[1] == move.moveName).OrderBy(r => ParseContactFloat(r[3])).ToArray();
            if (landings.Length != (move.moveName == "Light_GSWING" ? 2 : 1)) throw new Exception("New Light throw coverage is incomplete.");
            var cues = new List<BattleSfxBank.Cue>();
            void Swing(float time, string source, HumanBodyBones bone = HumanBodyBones.Chest)
            { cues.Add(new BattleSfxBank.Cue { seconds = time, group = "light_swing", contactSource = source, contactBone = bone }); }
            switch (move.moveName)
            {
                case "Light_JPBOM": Swing(.40f, "RightHand"); Swing(.96f, "Receiver", HumanBodyBones.Hips); break;
                case "Light_SIHO": Swing(.18f, "RightHand"); Swing(.47f, "Receiver"); break;
                case "Light_GSWING":
                    Swing(.24f, "RightHand"); Swing(.69f, "Receiver"); Swing(.89f, "Receiver");
                    Swing(1.07f, "Receiver"); Swing(1.34f, "Receiver", HumanBodyBones.Hips); break;
                default: throw new Exception("Unknown new Light throw.");
            }
            for (int index = 0; index < landings.Length; index++)
            {
                var row = landings[index];
                cues.Add(new BattleSfxBank.Cue {
                    seconds = ParseContactFloat(row[3]), group = "body_fall", finalLanding = index == landings.Length - 1,
                    damageOnLanding = true, hasContactPoint = true, contactSource = "Ground",
                    contactBone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), row[4]),
                    contactOffset = new Vector3(ParseContactFloat(row[5]), ParseContactFloat(row[6]), ParseContactFloat(row[7]))
                });
            }
            return cues.OrderBy(c => c.seconds).ToArray();
        }

        static string ThrowAssetRef(Object asset)
        {
            if (!asset) return "{fileID: 0}";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long file)) throw new Exception("New Light throw reference is not an asset.");
            int type = AssetDatabase.GetAssetPath(asset).EndsWith(".anim", StringComparison.Ordinal) || asset is ScriptableObject ? 2 : 3;
            return $"{{fileID: {file}, guid: {guid}, type: {type}}}";
        }

        static string SerializeLightThrow(CombatTripletData move, string nl)
        {
            var p = move.sourcePair;
            string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string V(Vector3 v) => $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}}}";
            string Q(Quaternion q) => $"{{x: {F(q.x)}, y: {F(q.y)}, z: {F(q.z)}, w: {F(q.w)}}}";
            return string.Join(nl, new[] {
                "  - moveName: " + move.moveName, "    attackAnim: " + ThrowAssetRef(move.attackAnim), "    hitAnim: " + ThrowAssetRef(move.hitAnim),
                "    getUpAnim: " + ThrowAssetRef(move.getUpAnim), FormattableString.Invariant($"    attackRange: {move.attackRange:R}"), "    weapon: 0", "    skill: 0",
                "    sourcePair:", "      attackerDriver: " + ThrowAssetRef(p.attackerDriver), "      receiverDriver: " + ThrowAssetRef(p.receiverDriver),
                "      attack: " + ThrowAssetRef(p.attack), "      reaction: " + ThrowAssetRef(p.reaction), "      getUp: " + ThrowAssetRef(p.getUp),
                "      receiverOffset: " + V(p.receiverOffset), "      receiverRotation: " + Q(p.receiverRotation), "      spacing: " + ThrowAssetRef(p.spacing),
                FormattableString.Invariant($"      bodySpacing: {p.bodySpacing:R}"), "      unarmedIndex: " + p.unarmedIndex,
                "      pepeAttacks: " + (p.pepeAttacks ? "1" : "0"), "      showWeapon: 0", "      reactionDelay: 0", "      receiverFloorLift:" }) + nl +
                string.Join(nl, p.receiverFloorLift.Select(v => FormattableString.Invariant($"      - {v:R}"))) + nl;
        }

        [MenuItem("Tools/Battle/Add JPBOM, SIHO and GSWING to Light")]
        public static void InstallNewLightThrows()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat }; var bank = game.battleVfx.timeline;
                var additions = fighters.ToDictionary(f => f.name, f => LightThrowNames.Select(n => MakeLightThrow(f, n)).ToArray());
                var profiles = bank.moves.Where(p => !LightThrowNames.Any(n => p.label.EndsWith("Light_" + n, StringComparison.Ordinal))).ToList();
                foreach (var fighter in fighters)
                foreach (var move in additions[fighter.name])
                    profiles.Add(new BattleSfxBank.Move {
                        label = fighter.name + " " + move.moveName, fighter = move.sourcePair.pepeAttacks ? BattleSfxBank.Fighter.Pepe : BattleSfxBank.Fighter.Mankey,
                        attack = move.attackAnim, reaction = move.hitAnim, cues = NewLightThrowCues(fighter.name, move)
                    });
                string before = File.ReadAllText(SfxScene), nl = before.Contains("\r\n") ? "\r\n" : "\n";
                int patched = 0;
                string after = Regex.Replace(before, @"(?ms)^  lightCombatMoves:\r?\n.*?(?=^  heavyCombatMoves:)", match => {
                    string fighter = match.Value.Contains("pepeAttacks: 1") ? "Pepe" : "Mankey";
                    string old = Regex.Replace(match.Value, @"(?ms)^  - moveName: Light_(?:JPBOM|SIHO|GSWING)\r?\n.*?(?=^  - moveName:|\z)", "");
                    patched++; return old + string.Concat(additions[fighter].Select(m => SerializeLightThrow(m, nl)));
                });
                if (patched != 2 || profiles.Count != bank.moves.Length + (bank.moves.Any(p => p.label.EndsWith("Light_JPBOM", StringComparison.Ordinal)) ? 0 : 6))
                    throw new Exception("New Light scene/profile staging failed.");
                Undo.RecordObject(bank, "Add calibrated Light throws"); bank.moves = profiles.ToArray();
                EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
                if (after != before) File.WriteAllText(SfxScene, after, new UTF8Encoding(false));
                var live = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SfxScene);
                if (live.IsValid() && live.isLoaded)
                foreach (var fighter in live.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)))
                {
                    if (!additions.ContainsKey(fighter.name)) continue;
                    Undo.RecordObject(fighter, "Add JPBOM, SIHO and GSWING Light attacks");
                    fighter.lightCombatMoves = fighter.lightCombatMoves.Where(m => !IsNewLightThrow(m)).Concat(additions[fighter.name]).ToArray();
                    EditorUtility.SetDirty(fighter);
                }
                File.WriteAllText(LightThrowReview + "/Installation.txt", "Added three paired throws to both Light pools (five Light moves per fighter). Six character-specific VFX/audio timelines reuse the six existing source clips and two existing calibrated drivers. Existing Light and Heavy moves retained.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void SurveyNewLightThrows()
        {
            Directory.CreateDirectory(LightThrowReview);
            if (!File.Exists(LightThrowReview + "/SceneBefore.unity.txt")) File.Copy(SfxScene, LightThrowReview + "/SceneBefore.unity.txt");
            if (!File.Exists(LightThrowReview + "/BankBefore.asset.txt")) File.Copy(SfxBankPath, LightThrowReview + "/BankBefore.asset.txt");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene); BattleLightingRig lighting = null;
            var inventory = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { f.battleSfx = null; f.battleVfx = null; f.hitEffect = null; }
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                foreach (var source in fighters)
                foreach (string name in LightThrowNames)
                {
                    var move = MakeLightThrow(source, name, false); var target = fighters.Single(f => f != source);
                    foreach (var f in fighters) f.ResetCombat();
                    source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new Exception("New Light throw rejected.");
                    var pair = source.SourcePlayback; string prefix = LightThrowReview + "/" + source.name + "_" + name;
                    inventory.AppendLine($"{source.name}/{name}: attack={move.attackAnim.name} {move.attackAnim.length:F6}s; reaction={move.hitAnim.name} {move.hitAnim.length:F6}s; getUp={move.getUpAnim.name}; range={move.attackRange:F6}");
                    var csv = new StringBuilder("time,coreY,coreX,coreZ,hipsY,headY,chestY,handSpeed,hipsSpeed,gripGap,attackerHipsZ,receiverHipsZ\n");
                    var previousHands = new Vector3[2]; Vector3 previousHips = Vector3.zero;
                    using (var floor = new ThrowBodyFloor(target))
                    for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * 120); frame++)
                    {
                        float time = Mathf.Min(pair.Duration, frame / 120f); pair.EvaluateAt(time); var point = floor.Sample();
                        var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        var head = target.Animator.GetBoneTransform(HumanBodyBones.Head).position;
                        var chest = target.Animator.GetBoneTransform(HumanBodyBones.Chest).position;
                        var hands = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand }.Select(b => source.Animator.GetBoneTransform(b).position).ToArray();
                        var feet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }.Select(b => target.Animator.GetBoneTransform(b).position).ToArray();
                        float handSpeed = frame == 0 ? 0 : Enumerable.Range(0, 2).Max(i => Vector3.Distance(hands[i], previousHands[i]) * 120);
                        float hipsSpeed = frame == 0 ? 0 : Vector3.Distance(hips, previousHips) * 120;
                        float grip = hands.Max(h => feet.Min(f => Vector3.Distance(h, f)));
                        csv.AppendLine(FormattableString.Invariant($"{time:F6},{point.y:F6},{point.x:F6},{point.z:F6},{hips.y:F6},{head.y:F6},{chest.y:F6},{handSpeed:F6},{hipsSpeed:F6},{grip:F6},{source.Animator.GetBoneTransform(HumanBodyBones.Hips).position.z:F6},{hips.z:F6}"));
                        previousHands = hands; previousHips = hips;
                    }
                    File.WriteAllText(prefix + "_Motion.csv", csv.ToString());
                    int tiles = Mathf.CeilToInt(pair.Duration / .1f) + 1, rows = Mathf.CeilToInt(tiles / 6f);
                    var sheet = new Texture2D(2400, rows * 280, TextureFormat.RGB24, false);
                    var labels = new StringBuilder("row,column,time\n");
                    try
                    {
                        for (int tile = 0; tile < tiles; tile++)
                        {
                            float time = Mathf.Min(pair.Duration, tile * .1f); pair.EvaluateAt(time);
                            int row = tile / 6, col = tile % 6;
                            CaptureContactPose(camera, fighters, pair, sheet, col * 400, (rows - row - 1) * 280);
                            labels.AppendLine(FormattableString.Invariant($"{row},{col},{time:F3}"));
                        }
                        sheet.Apply(); File.WriteAllBytes(prefix + "_Timeline.png", sheet.EncodeToPNG()); File.WriteAllText(prefix + "_Timeline.csv", labels.ToString());
                    }
                    finally { Object.DestroyImmediate(sheet); pair.Cancel(); }
                    File.WriteAllText(LightThrowReview + "/Inventory.txt", inventory.ToString());
                }
            }
            finally { if (lighting) lighting.ShutdownRig(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void ValidateNewLightThrows()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene); var report = new StringBuilder();
            int cases = 0, landings = 0; float lowest = float.PositiveInfinity;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat }; var vfx = game.battleVfx;
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                foreach (var source in fighters)
                {
                    if (source.lightCombatMoves.Length != 5 || LightThrowNames.Any(n => source.lightCombatMoves.Count(m => m.moveName == "Light_" + n && m.IsValid) != 1))
                        throw new Exception("Both Light pools must contain the original two moves and all three new pairs.");
                    foreach (var move in source.lightCombatMoves.Where(IsNewLightThrow))
                    {
                        var offset = move.sourcePair.receiverOffset;
                        var rotation = move.sourcePair.receiverRotation;
                        if (!float.IsFinite(offset.x) || !float.IsFinite(offset.y) || !float.IsFinite(offset.z) ||
                            !float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) || !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w))
                            throw new Exception("Throw has invalid serialized pair coordinates: " + move.moveName);
                        var profile = vfx.timeline.FindMove(move);
                        if (profile == null || profile.fighter != (move.sourcePair.pepeAttacks ? BattleSfxBank.Fighter.Pepe : BattleSfxBank.Fighter.Mankey) ||
                            profile.cues.Count(c => c.group == "body_fall") != (move.moveName == "Light_GSWING" ? 2 : 1) ||
                            profile.cues.Any(c => c.seconds >= Mathf.Max(move.attackAnim.length, move.hitAnim.length)))
                            throw new Exception("New throw uses an incomplete or wrong character timeline.");
                        foreach (bool mirrored in new[] { false, true })
                        foreach (bool lethal in new[] { false, true })
                        foreach (bool skipped in new[] { false, true })
                        {
                            foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch(); game.battleSfx.ResetForMatch();
                            var target = fighters.Single(f => f != source);
                            source.Animator.transform.position = Vector3.zero; target.Animator.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                            float laneDepth = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position.z;
                            if (!source.ExecuteAttack(move, target, lethal)) throw new Exception("New Light throw rejected.");
                            var pair = source.SourcePlayback; var times = BattleHitDamageSequence.ContactTimes(profile, pair.Duration);
                            long total = lethal ? 1000 : 103, hp = 1000, sum = 0; int applied = 0, effects = 0, contacts = 0, swings = 0;
                            var damage = new BattleHitDamageSequence(1000, lethal ? 0 : 897, total, times, (value, portion) => { hp = value; sum += portion; applied++; });
                            Action<FrankBattlePairPlayback, float> advance = (owner, time) => damage.Advance(time);
                            Action<BattleVfxPlayer.Impact> contact = impact => contacts++;
                            pair.TimelineAdvanced += advance; vfx.ContactOccurred += contact;
                            Action<string, GameObject> observe = (id, root) => {
                                if (id == "blade_slash" || id == "thrust_swing") throw new Exception("Unarmed throw played a weapon slash.");
                                if (id == "light_swing")
                                {
                                    var cue = profile.cues.Single(c => c.group == "light_swing" && Mathf.Abs(c.seconds - pair.SampleTime) < .0001f);
                                    var bone = cue.contactSource == "Receiver" ? target.Animator.GetBoneTransform(cue.contactBone) :
                                        source.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), cue.contactSource));
                                    if (Vector3.Distance(root.transform.position, bone.position - camera.transform.forward * .35f) > .002f) throw new Exception("Throw whoosh left the moving hand/body.");
                                    swings++;
                                }
                                if (id != "ground_impact") return;
                                var landing = profile.cues.Single(c => c.group == "body_fall" && Mathf.Abs(c.seconds - pair.SampleTime) < .0001f);
                                int reached = times.Count(t => t <= landing.seconds);
                                if (applied != reached) throw new Exception("Ground VFX preceded its damage.");
                                var point = target.Animator.GetBoneTransform(landing.contactBone).TransformPoint(landing.contactOffset);
                                var ground = point; ground.y = vfx.groundHeight + .035f;
                                if (Mathf.Abs(point.y) > .06f || Vector3.Distance(root.transform.position, ground) > .002f)
                                    throw new Exception("Throw VFX does not match its body/floor contact.");
                                if (root.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) == 0) throw new Exception("Throw ground impact is invisible before hit-stop.");
                                effects++; landings++;
                            };
                            vfx.EffectPlayed += observe;
                            try
                            {
                                // Sample between baked frames too: floor correction
                                // must not hide penetration only on its own keys.
                                if (!lethal && !skipped)
                                using (var floor = new ThrowBodyFloor(target))
                                for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * FrankBattlePair.ReceiverFloorSamplesPerSecond * 2); frame++)
                                {
                                    float time = Mathf.Min(pair.Duration, (float)frame / (FrankBattlePair.ReceiverFloorSamplesPerSecond * 2));
                                    pair.EvaluateAt(time); float y = floor.Sample().y; lowest = Mathf.Min(lowest, y);
                                    if (!float.IsFinite(y) || y < -.035f) throw new Exception($"Invalid body/floor pose: {source.name}/{move.moveName} at {time:F6}s y={y:F6}.");
                                    if (Mathf.Abs(target.Animator.GetBoneTransform(HumanBodyBones.Hips).position.z - laneDepth) > .0001f) throw new Exception("Throw changed the battle lane depth.");
                                }
                                pair.EvaluateAt(0);
                                if (skipped) pair.AdvanceTo(pair.Duration);
                                else foreach (var cue in profile.cues)
                                {
                                    int before = effects; pair.AdvanceTo(cue.seconds - .0001f);
                                    if (effects != before) throw new Exception("Early ground effect.");
                                    pair.AdvanceTo(cue.seconds); int current = effects;
                                    pair.AdvanceTo(cue.seconds); pair.AdvanceTo(cue.seconds - .05f);
                                    if (effects != current) throw new Exception("Repeated throw effect.");
                                }
                                pair.AdvanceTo(pair.Duration);
                                if (effects != times.Length || contacts != times.Length || applied != times.Length || sum != total || hp != (lethal ? 0 : 897) ||
                                    swings != profile.cues.Count(c => c.group == "light_swing")) throw new Exception("Throw lost an effect or changed server HP/damage totals.");
                                pair.Cancel(); pair.AdvanceTo(pair.Duration);
                                if (vfx.ActiveEffectCount != 0) throw new Exception("Cancelled throw retained VFX.");
                                report.AppendLine($"PASS {source.name}/{move.moveName}: {effects} ground contacts, {swings} body/hand whooshes; mirror={mirrored}, lethal={lethal}, skip={skipped}"); cases++;
                            }
                            finally { vfx.EffectPlayed -= observe; vfx.ContactOccurred -= contact; pair.TimelineAdvanced -= advance; damage.Cancel(); pair.Cancel(); }
                        }
                    }
                }
                report.AppendLine(FormattableString.Invariant($"ALL PASS: {cases} cases, {landings} exact landing effects; independent fighter timelines, no duplicate or early effect, mirrored/lethal/frame-skip/cancel, exact server damage/HP; core minimum between baked frames={lowest:F6}m."));
            }
            catch (Exception error) { report.AppendLine("FAIL " + error); throw; }
            finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(LightThrowReview + "/Validation.txt", report.ToString()); }
        }

        public static void CaptureNewLightThrowEffects()
        {
            Directory.CreateDirectory(LightThrowReview + "/After");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene); BattleLightingRig lighting = null;
            var rt = RenderTexture.GetTemporary(1280, 720, 24); var previous = RenderTexture.active;
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false); var report = new StringBuilder("fighter,move,row,time,effect\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat }; var vfx = game.battleVfx;
                foreach (var f in fighters) f.Initialize();
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                var director = camera.GetComponent<FrankCinematicCamera>();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); lighting.RefreshLighting(.22f);
                foreach (var source in fighters)
                foreach (var move in source.lightCombatMoves.Where(IsNewLightThrow))
                {
                    var cues = vfx.timeline.FindMove(move).cues;
                    var sheet = new Texture2D(1200, cues.Length * 280, TextureFormat.RGB24, false);
                    try
                    {
                        for (int index = 0; index < cues.Length; index++)
                        {
                            var cue = cues[index];
                            foreach (var f in fighters) f.ResetCombat(); vfx.ResetForMatch();
                            var target = fighters.Single(f => f != source); bool reverse = source == fighters[1];
                            source.Animator.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                            target.Animator.transform.position = source.Animator.transform.position + Vector3.right * move.attackRange * (reverse ? -1 : 1);
                            var roots = new HashSet<GameObject>(); var current = new HashSet<GameObject>();
                            Action<string, GameObject> observe = (id, root) => { roots.Add(root); if (Mathf.Abs(source.SourcePlayback.SampleTime - cue.seconds) < .0001f) current.Add(root); };
                            vfx.EffectPlayed += observe;
                            try
                            {
                                source.ExecuteAttack(move, target); var pair = source.SourcePlayback;
                                pair.EvaluateAt(cue.seconds - .025f); CaptureContactPose(camera, fighters, pair, sheet, 0, (cues.Length - index - 1) * 280);
                                pair.AdvanceTo(cue.seconds);
                                foreach (var root in roots)
                                {
                                    root.SetActive(current.Contains(root));
                                    if (root.activeSelf) foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) p.Simulate(cue.group == "light_swing" ? .12f : .035f, false, true, false);
                                }
                                CaptureContactPose(camera, fighters, pair, sheet, 400, (cues.Length - index - 1) * 280);
                                camera.targetTexture = rt; director.ResetView(); director.Apply(0, true);
                                CaptureStrongPose(camera, fighters, pair, stamp, rt);
                                File.WriteAllBytes($"{LightThrowReview}/After/{source.name}_{move.moveName}_{index:00}_{cue.group}.png", stamp.EncodeToPNG());
                                camera.orthographic = true; foreach (var root in roots) root.SetActive(false);
                                pair.EvaluateAt(cue.seconds + .025f); CaptureContactPose(camera, fighters, pair, sheet, 800, (cues.Length - index - 1) * 280);
                                report.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},{index},{cue.seconds:F6},{cue.group}")); pair.Cancel();
                            }
                            finally { vfx.EffectPlayed -= observe; source.SourcePlayback?.Cancel(); }
                        }
                        sheet.Apply(); File.WriteAllBytes($"{LightThrowReview}/After/{source.name}_{move.moveName}_Effects.png", sheet.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(sheet); }
                }
                File.WriteAllText(LightThrowReview + "/After.csv", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); RenderTexture.active = previous;
                Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
