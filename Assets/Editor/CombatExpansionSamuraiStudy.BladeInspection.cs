using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string BladeOutput = "GeneratedAssets/CombatExpansion/SamuraiStudy/BladeContacts";

        public static void InspectBladeMesh()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(BladeOutput);
            using var session = new SourceSession();
            InitializePairStudy(session.Fighters);
            var source = session.Fighters[0];
            var target = session.Fighters[1];
            var pair = BeginBladeStudy(session.Fighters, source, target, 1, 1);
            try
            {
                pair.EvaluateAt(0);
                var renderer = SwordRenderer(pair);
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices;
                var rows = new StringBuilder("vertex,x,y,z\n");
                for (int index = 0; index < vertices.Length; index++)
                {
                    var point = vertices[index];
                    rows.AppendLine(FormattableString.Invariant($"{index},{point.x:R},{point.y:R},{point.z:R}"));
                }
                File.WriteAllText(BladeOutput + "/BladeLocalVertices.csv", rows.ToString());
                File.WriteAllLines(BladeOutput + "/BladeTriangles.csv",
                    new[] { "a,b,c" }.Concat(Enumerable.Range(0, mesh.triangles.Length / 3)
                        .Select(face => string.Join(",", mesh.triangles.Skip(face * 3).Take(3)))));
                var hand = pair.AttackerActor.Pose.driver.GetBoneTransform(HumanBodyBones.RightHand);
                var grip = renderer.transform.InverseTransformPoint(hand.position);
                File.WriteAllText(BladeOutput + "/MeshInspection.txt",
                    "Native BladeR only; Sword_Hold sheath explicitly excluded.\n" +
                    "Mesh=" + CombatExpansionInventory.Identity(mesh) + "; path=" + AssetDatabase.GetAssetPath(mesh) +
                    "\nRenderer=" + AnimationUtility.CalculateTransformPath(renderer.transform,
                        pair.AttackerActor.activeDriver.transform) + "\n" +
                    FormattableString.Invariant($"Vertices={vertices.Length}; triangles={mesh.triangles.Length / 3}; ") +
                    FormattableString.Invariant($"submeshes={mesh.subMeshCount}; localBounds={mesh.bounds}\n") +
                    FormattableString.Invariant($"Native hand pivot in mesh local space=({grip.x:R},{grip.y:R},{grip.z:R})\n") +
                    "No blade region or contact timing is accepted by this inspection.\n");
            }
            finally
            {
                pair.Cancel();
            }
        }

        static void InitializePairStudy(CharacterCombat[] fighters)
        {
            foreach (var type in new[] { typeof(GameManager), typeof(MemeBattleUI), typeof(WebSocketManager),
                typeof(MortalKombatCamera), typeof(CombatPositioningController) })
                type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = null;
                fighter.battleVfx = null;
                fighter.hitEffect = null;
                if (!fighter.Initialize())
                    throw new InvalidOperationException("Cannot initialize Samurai fighter " + fighter.name);
            }
        }

        static FrankBattlePairPlayback BeginBladeStudy(CharacterCombat[] fighters, CharacterCombat source,
            CharacterCombat target, int execution, int direction, FrankPairGrounding grounding = null)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .85f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .85f,
                Quaternion.LookRotation(Vector3.left * direction));
            var move = MakePairMove(source, target, execution);
            move.grounding = grounding;
            if (!source.ExecuteAttack(move, target) || source.SourcePlayback == null)
                throw new InvalidOperationException("Samurai blade study pair was rejected.");
            source.SourcePlayback.AttackerActor.Pose.transferFingers = true;
            source.SourcePlayback.ReceiverActor.Pose.transferFingers = true;
            return source.SourcePlayback;
        }

        static Renderer SwordRenderer(FrankBattlePairPlayback pair)
        {
            var renderer = pair.AttackerActor.Pose.weaponRenderers.Single(r => r && r.name == "BladeR");
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Samurai BladeR needs its active native static mesh.");
            return renderer;
        }
    }
}
