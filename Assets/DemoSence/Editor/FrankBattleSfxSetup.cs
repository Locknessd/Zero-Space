using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string SfxScene = "Assets/Scenes/BattleScene.unity";
        const string SfxBankPath = "Assets/Audio/Battle/BattleSfxBank.asset";
        [Serializable] sealed class SfxSelection { public SfxSelectionGroup[] groups; }
        [Serializable] sealed class SfxSelectionGroup
        {
            public string id;
            public float volume, pitchMin, pitchMax;
            public SfxSelectionClip[] clips;
        }
        [Serializable] sealed class SfxSelectionClip { public string path, playbackPath; public float peakDbFS, playbackPeakDbFS; }

        [MenuItem("Tools/Battle/Install selected SFX")]
        public static void InstallBattleSfx()
        {
            var selection = JsonUtility.FromJson<SfxSelection>(File.ReadAllText("Assets/Audio/Battle/BattleSfxSelection.json"));
            var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(SfxBankPath);
            bool existing = bank;
            if (!bank) bank = ScriptableObject.CreateInstance<BattleSfxBank>();
            bank.groups = selection.groups.Select(g => new BattleSfxBank.Group
            {
                id = g.id, volume = g.volume, pitch = new Vector2(g.pitchMin, g.pitchMax),
                clips = g.clips.Select(c => LoadBattleSfxClip(string.IsNullOrEmpty(c.playbackPath) ? c.path : c.playbackPath)).ToArray(),
                // Bring variant peaks closer without rewriting the source WAV/importer.
                // Cap boosts on quiet foley; final output is clamped by the player.
                clipGains = g.clips.Select(c => string.IsNullOrEmpty(c.playbackPath) ?
                    Mathf.Min(4f, Mathf.Pow(10f, (-6f - c.peakDbFS) / 20f)) : 1f).ToArray()
            }).ToArray();
            foreach (var group in bank.groups)
                group.startOffsets = BattleAudioTimingSetup.Offsets(group);
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var player = game.GetComponent<BattleSfxPlayer>();
                if (!player) player = game.gameObject.AddComponent<BattleSfxPlayer>();
                player.bank = bank;
                player.masterVolume = .95f;
                player.uiButtons = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Button>(true)).Distinct().ToArray();
                game.battleSfx = player;
                var fighters = new[] { game.leftCombat, game.rightCombat };
                var profiles = new List<BattleSfxBank.Move>();
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = player;
                    foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                    {
                        if (move.sourcePair == null || !move.sourcePair.Valid) throw new Exception("No source pair " + move.moveName);
                        if (profiles.Any(p => p.attack == move.sourcePair.attack && p.reaction == move.sourcePair.reaction)) continue;
                        profiles.Add(new BattleSfxBank.Move
                        {
                            label = fighter.name + " " + move.moveName,
                            attack = move.sourcePair.attack,
                            reaction = move.sourcePair.reaction,
                            cues = SfxCuesFor(move)
                        });
                    }
                    EditorUtility.SetDirty(fighter);
                }
                bank.moves = profiles.ToArray();
                if (!existing) AssetDatabase.CreateAsset(bank, SfxBankPath);
                EditorUtility.SetDirty(bank);
                EditorUtility.SetDirty(game);
                EditorUtility.SetDirty(player);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Directory.CreateDirectory("Temp/FrankRetarget");
                File.WriteAllText("Temp/FrankRetarget/battle-sfx-install.txt",
                    $"Installed {bank.groups.Sum(g => g.clips.Length)} clips, {bank.moves.Length} timelines, " +
                    $"2 fighter references, {player.uiButtons.Length} UI buttons.\n" +
                    string.Join("\n", bank.moves.Select(m => m.label + ": " + string.Join(", ", m.cues.Select(c => $"{c.seconds:F2}s {c.group}")))));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static AudioClip LoadBattleSfxClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!clip)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            if (!clip && path.Contains("/Clicks_Taps/"))
            {
                // Repack the two very short legacy UI WAVs as plain PCM with a
                // short silent tail. Source files and their GUIDs stay intact.
                string converted = "Assets/Audio/Battle/Clips/" + Path.GetFileName(path);
                Directory.CreateDirectory("Assets/Audio/Battle/Clips");
                using (var input = new BinaryReader(File.OpenRead(path)))
                {
                    input.BaseStream.Position = 12;
                    byte[] format = null, data = null;
                    while (input.BaseStream.Position + 8 <= input.BaseStream.Length)
                    {
                        string chunk = Encoding.ASCII.GetString(input.ReadBytes(4));
                        int size = input.ReadInt32();
                        byte[] bytes = input.ReadBytes(size);
                        if (chunk == "fmt ") format = bytes;
                        if (chunk == "data") data = bytes;
                        if ((size & 1) != 0) input.ReadByte();
                    }
                    if (format == null || data == null || BitConverter.ToUInt16(format, 0) != 1)
                        throw new Exception("Expected PCM UI sound: " + path);
                    int block = BitConverter.ToUInt16(format, 12);
                    int byteRate = BitConverter.ToInt32(format, 8);
                    int paddedLength = Math.Max(data.Length, ((byteRate / 10 + block - 1) / block) * block);
                    using (var output = new BinaryWriter(File.Create(converted)))
                    {
                        output.Write(Encoding.ASCII.GetBytes("RIFF")); output.Write(36 + paddedLength);
                        output.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); output.Write(16); output.Write(format, 0, 16);
                        output.Write(Encoding.ASCII.GetBytes("data")); output.Write(paddedLength);
                        output.Write(data); output.Write(new byte[paddedLength - data.Length]);
                    }
                }
                AssetDatabase.ImportAsset(converted, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(converted) as AudioImporter;
                if (importer)
                {
                    var settings = importer.defaultSampleSettings;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(converted);
            }
            if (!clip) throw new Exception("Missing SFX after explicit import: " + path);
            if (path.StartsWith("Assets/Audio/Battle/Clips/Processed/", StringComparison.Ordinal))
            {
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad || settings.compressionFormat != AudioCompressionFormat.PCM || !settings.preloadAudioData)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }
            return clip;
        }

        static BattleSfxBank.Cue[] SfxCuesFor(CombatTripletData move)
        {
            var cues = new List<BattleSfxBank.Cue>();
            void Cue(float time, string id, bool final = false) => cues.Add(new BattleSfxBank.Cue
                { seconds = time, group = id, finalLanding = final });
            void Hit(float time, string swing, string impact)
            {
                // Put the whoosh's measured energy peak just before contact.
                float lead = swing == "blade_swing" ? .23f : swing == "heavy_swing" ? .19f :
                    swing == "thrust_swing" ? .105f : .13f;
                Cue(Mathf.Max(0, time - lead), swing);
                Cue(time, impact);
            }
            // Source-pose samples at 30 Hz and visual contact review, shared by
            // Mankey and Pepe. Receiver landing is separate from attacker landing.
            switch (move.moveName)
            {
                case "Light_1": Cue(.48f, "light_swing"); Cue(1.05f, "light_swing"); Cue(1.27f, "body_fall", true); Cue(1.29f, "comic_fall"); break;
                case "Light_3": Cue(.53f, "light_swing"); Cue(1.53f, "light_swing"); Cue(1.77f, "body_fall", true); Cue(1.79f, "comic_fall"); break;
                case "Heavy_5":
                    Hit(1.47f, "light_swing", "heavy_hit"); Hit(2.30f, "blade_swing", "light_hit");
                    Hit(2.90f, "blade_swing", "heavy_hit"); Hit(3.67f, "blade_swing", "light_hit");
                    Hit(4.20f, "blade_swing", "heavy_hit"); Cue(4.97f, "body_fall", true); break;
                case "Heavy_6":
                    Hit(1.47f, "heavy_swing", "heavy_hit"); Hit(2.27f, "heavy_swing", "heavy_hit");
                    Hit(2.97f, "heavy_swing", "heavy_hit"); Hit(4.40f, "heavy_swing", "heavy_hit");
                    Cue(5.07f, "body_fall", true); break;
                case "Heavy_7":
                    Hit(1.17f, "thrust_swing", "stab_hit"); Hit(2.73f, "blade_swing", "light_hit");
                    Hit(3.30f, "blade_swing", "light_hit"); Hit(3.73f, "light_swing", "heavy_hit");
                    Hit(4.70f, "thrust_swing", "stab_hit"); Hit(5.60f, "light_swing", "heavy_hit");
                    Cue(6.03f, "body_fall", true); break;
                case "Heavy_2":
                    Hit(1.47f, "heavy_swing", "heavy_hit"); Hit(2.30f, "light_swing", "heavy_hit");
                    Cue(2.83f, "body_fall"); Hit(3.47f, "heavy_swing", "heavy_hit");
                    Hit(4.27f, "heavy_swing", "heavy_hit"); Cue(4.53f, "body_fall", true); break;
                case "Heavy_8":
                    Hit(1.07f, "blade_swing", "light_hit"); Hit(1.33f, "blade_swing", "light_hit");
                    Hit(1.90f, "blade_swing", "heavy_hit"); Hit(2.67f, "thrust_swing", "stab_hit");
                    Hit(3.17f, "blade_swing", "light_hit"); Hit(3.53f, "thrust_swing", "stab_hit");
                    Hit(3.83f, "light_swing", "heavy_hit"); Cue(4.13f, "body_fall", true);
                    Cue(5.43f, "body_fall"); break;
                case "Heavy_Katana":
                    Hit(1.57f, "blade_swing", "heavy_hit"); Hit(2.27f, "blade_swing", "heavy_hit");
                    Hit(3.13f, "blade_swing", "heavy_hit"); Cue(4.07f, "body_fall", true); break;
                case "Heavy_Assassin":
                    Hit(.87f, "blade_swing", "light_hit"); Hit(1.33f, "thrust_swing", "stab_hit");
                    Cue(3.33f, "light_swing"); Cue(4.10f, "body_fall", true); break;
                default: throw new Exception("Review SFX timing for new move: " + move.moveName);
            }
            float duration = Mathf.Max(move.sourcePair.attack.length, move.sourcePair.reaction.length);
            if (cues.Any(c => c.seconds > duration)) throw new Exception("SFX cue exceeds motion duration " + move.moveName);
            return cues.OrderBy(c => c.seconds).ToArray();
        }

        public static void SurveyBattleSfx()
        {
            const string folder = "Temp/FrankRetarget/SfxReview";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var csv = new StringBuilder("move,time,attackerY,receiverY,receiverX,rightHandX,rightHandY,leftHandX,leftHandY\n");
            GameObject cameraObject = null;
            RenderTexture rt = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var attacker = game.leftCombat;
                var receiver = game.rightCombat;
                cameraObject = new GameObject("SFX review camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.14f, .17f, .21f);
                camera.orthographic = true;
                rt = RenderTexture.GetTemporary(480, 320, 24);
                camera.targetTexture = rt;
                var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                {
                    attacker.ResetCombat(); receiver.ResetCombat();
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = Vector3.right * move.attackRange;
                    if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Cannot sample " + move.moveName);
                    var pair = attacker.SourcePlayback;
                    for (int frame = 0; frame <= Mathf.CeilToInt(pair.Duration * 30); frame++)
                    {
                        float seconds = Mathf.Min(frame / 30f, pair.Duration);
                        pair.EvaluateAt(seconds);
                        var ap = attacker.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        var rp = receiver.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        var rh = attacker.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                        var lh = attacker.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                        csv.AppendLine(FormattableString.Invariant($"{move.moveName},{seconds:F4},{ap.y:F4},{rp.y:F4},{rp.x:F4},{rh.x:F4},{rh.y:F4},{lh.x:F4},{lh.y:F4}"));
                    }
                    var sheet = new Texture2D(480 * 4, 320 * 5, TextureFormat.RGB24, false);
                    var times = new List<string>();
                    for (int frame = 0; frame < 20; frame++)
                    {
                        float seconds = pair.Duration * frame / 19f;
                        pair.EvaluateAt(seconds);
                        var bones = new[] { attacker, receiver }.SelectMany(f =>
                            Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                                .Where(b => b < HumanBodyBones.LastBone).Select(b => f.Animator.GetBoneTransform(b))).Where(b => b).ToArray();
                        Bounds bounds = new Bounds(bones[0].position, Vector3.zero);
                        foreach (var bone in bones) bounds.Encapsulate(bone.position);
                        camera.transform.position = bounds.center + new Vector3(0, .3f, -15);
                        camera.transform.LookAt(bounds.center);
                        camera.orthographicSize = Mathf.Max(1.6f, bounds.extents.y + .7f, (bounds.extents.x + .9f) / 1.5f);
                        camera.Render();
                        var old = RenderTexture.active;
                        RenderTexture.active = rt;
                        stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0); stamp.Apply();
                        RenderTexture.active = old;
                        sheet.SetPixels((frame % 4) * 480, (4 - frame / 4) * 320, 480, 320, stamp.GetPixels());
                        times.Add(seconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    sheet.Apply();
                    File.WriteAllBytes(folder + "/" + move.moveName + ".png", sheet.EncodeToPNG());
                    File.WriteAllText(folder + "/" + move.moveName + "-times.txt", string.Join(", ", times));
                    Object.DestroyImmediate(sheet);
                    pair.Cancel();
                }
                Object.DestroyImmediate(stamp);
                File.WriteAllText(folder + "/motion.csv", csv.ToString());
            }
            finally
            {
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                if (rt) RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
