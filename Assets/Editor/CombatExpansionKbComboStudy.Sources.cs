using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        const string AttackGuid = "7934f1476fe2c6e458c230f491769c34";
        const string ReactionGuid = "932279eb1c22db24385eb04c03856eb1";
        [Serializable]
        sealed class SourceRecord
        {
            public string guid, path, name, fileHash, metaHash;
            public long localId;
            public float length;
            [NonSerialized] public AnimationClip clip;
        }

        static SourceRecord[] ResolveSources()
        {
            long[] ids = { 7400004, 7400006, 7400014, 7400018, 7400004 };
            string[] names = { "KB_p_Jab_L_1", "KB_p_Jab_R_1", "KB_p_Hook_R",
                "KB_Hit_p_MidFront_Weak", "KB_Hit_p_HighFront_Weak" };
            float[] lengths = { .95f, .7666667f, 1.0166667f, .8333334f, .8333334f };
            return Enumerable.Range(0, ids.Length).Select(i =>
            {
                string guid = i < 3 ? AttackGuid : ReactionGuid;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().SingleOrDefault(c =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string g, out long id) &&
                    g == guid && id == ids[i]);
                if (!clip || clip.name != names[i] || !clip.isHumanMotion || clip.legacy ||
                    !float.IsFinite(clip.length) || Mathf.Abs(clip.length - lengths[i]) > .00001f)
                    throw new InvalidOperationException("KB source identity/length mismatch " + guid + ":" + ids[i]);
                return new SourceRecord { guid = guid, path = path, localId = ids[i], name = clip.name,
                    length = clip.length, clip = clip, fileHash = Hash(path), metaHash = Hash(path + ".meta") };
            }).ToArray();
        }

        static string Hash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        static void RequireSourcesUnchanged(SourceRecord[] sources)
        {
            foreach (var source in sources)
                if (Hash(source.path) != source.fileHash || Hash(source.path + ".meta") != source.metaHash)
                    throw new InvalidOperationException("Source changed during study: " + source.path);
        }

        static FrankTestDriver Driver(string fighter, SourceRecord[] sources)
        {
            string path = "Assets/CombatExpansion/HumanoidStudy/Drivers/" + fighter + "_" + sources[0].guid + ".prefab";
            var driver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path);
            if (!driver || !driver.pose || !driver.pose.driver || !driver.pose.driver.avatar ||
                !driver.pose.driver.avatar.isValid || !driver.pose.driver.avatar.isHuman ||
                AssetDatabase.GetAssetPath(driver.pose.driver.avatar) != sources[0].path ||
                driver.pose.weaponRenderers.Length != 0 ||
                sources.Any(s => !driver.pose.sourceClips.Contains(s.clip)) ||
                driver.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeSelf) ||
                driver.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeSelf))
                throw new InvalidOperationException("Invalid/unarmed native source driver: " + path);
            return driver;
        }
    }
}
