using System;
using System.IO;
using System.Linq;
using System.Text;
using MemeArena;
using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    public static class MemeArenaValidation
    {
        public static void ValidatePrefab(GameObject prefab)
        {
            var report = new StringBuilder("MEME COLOSSEUM — prefab validation\n");
            if (!prefab || !prefab.GetComponent<MemeArenaStage>()) throw new Exception("Select a stage prefab.");
            var stage = prefab.GetComponent<MemeArenaStage>();
            if (prefab.GetComponentsInChildren<Collider>(true).Any(value => value.enabled))
                throw new Exception("The visual prefab contains an unexpected collider.");
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    throw new Exception("Missing script: " + transform.name);
            int triangles = 0;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) throw new Exception("Missing mesh: " + filter.name);
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    if (!material || !material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                        throw new Exception("Invalid or unsupported material: " + renderer.name);
            var manager = prefab.GetComponentInChildren<MemeCrowdReactionManager>(true);
            if (!manager || !manager.settings || manager.settings.spectators.Length < 1)
                throw new Exception("Missing crowd settings.");
            var groups = prefab.GetComponentsInChildren<MemeCrowdGroup>(true);
            if (groups.Length != 3) throw new Exception("Expected three independent supporter groups.");
            int animated = prefab.GetComponentsInChildren<MemeCrowdFan>(true).Length;
            if (animated > stage.settings.EffectiveAnimatedCount) throw new Exception("Animated crowd budget exceeded.");
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh);
            report.AppendLine("PASS saved prefab with valid mesh/material/script references and no colliders.");
            report.AppendLine("PASS three groups; spectator types=" + manager.settings.spectators.Length +
                "; atlas-backed GPU crowd.");
            report.AppendLine("Animated fans=" + animated + "; renderers=" + renderers.Length +
                "; triangles=" + triangles + "; shared mesh assets=" + meshes.Distinct().Count());
            report.AppendLine("PASS pooled confetti=" + manager.celebration.confetti.Length + "; independent stage settings.");
            if (stage.settings.enableFloorFracture && stage.settings.enableVfx && stage.settings.includeVisualFloor)
            {
                var fracture = stage.GetComponent<MemeArenaFloorFracture>();
                if (!fracture || fracture.pool.Length == 0 || fracture.pool.Any(slot =>
                    slot == null || !slot.root || !slot.scar || !slot.fragments))
                    throw new Exception("Missing pooled floor destruction references.");
                report.AppendLine("PASS pooled ground-contact floor fractures=" + fracture.pool.Length);
            }
            report.AppendLine("Performance target is 60 FPS; a running-build profile is required to verify it.");
            File.WriteAllText(MemeArenaBuild.Root + "/Documentation/PrefabValidation.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
