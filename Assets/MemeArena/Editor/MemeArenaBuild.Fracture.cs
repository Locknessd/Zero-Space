using MemeArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void BuildFloorFracture(MemeArenaStage stage)
        {
            if (!settings.enableVfx || !settings.includeVisualFloor || !settings.enableFloorFracture) return;
            var fracture = stage.gameObject.AddComponent<MemeArenaFloorFracture>();
            fracture.stage = stage;
            fracture.radius = settings.fractureRadius;
            var material = new Material(Shader.Find("MemeArena/Floor Fracture")) { name = "Cracked arena stone" };
            AssetDatabase.CreateAsset(material, folder + "/Materials/Floor fracture.mat");
            var mesh = FractureMesh();
            AssetDatabase.CreateAsset(mesh, folder + "/Meshes/Floor cracks.asset");
            var shard = ShardMesh();
            AssetDatabase.CreateAsset(shard, folder + "/Meshes/Broken stone shard.asset");
            int count = settings.quality == MemeArenaQuality.WebGL ? 3 : 4;
            fracture.pool = new MemeArenaFloorFracture.Slot[count];
            var pool = Group(stage.transform, "Pooled floor destruction");
            for (int i = 0; i < count; i++)
            {
                var root = Group(pool, "Floor fracture " + i);
                var scar = Group(root, "Radial broken floor");
                scar.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = scar.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var fragments = CreateParticles(root, "Flying stone fragments", stone, false);
                fragments.transform.localPosition = Vector3.up * .06f;
                var main = fragments.main;
                main.duration = 1.4f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.55f, 1.15f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 4.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(.07f, .2f);
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = 1.5f;
                main.maxParticles = settings.quality == MemeArenaQuality.WebGL ? 14 : 22;
                fragments.useAutoRandomSeed = false;
                fragments.randomSeed = (uint)(19019 + i * 31);
                var shape = fragments.shape;
                shape.angle = 58;
                shape.radius = .28f;
                var emission = fragments.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)main.maxParticles) });
                var spin = fragments.rotationOverLifetime;
                spin.enabled = true;
                spin.separateAxes = true;
                spin.x = 4;
                spin.y = 6;
                var scale = fragments.sizeOverLifetime;
                scale.enabled = true;
                scale.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                    new Keyframe(0, .7f), new Keyframe(.12f, 1), new Keyframe(.65f, 1), new Keyframe(1, 0)));
                var fragmentRenderer = fragments.GetComponent<ParticleSystemRenderer>();
                fragmentRenderer.renderMode = ParticleSystemRenderMode.Mesh;
                fragmentRenderer.mesh = shard;
                fracture.pool[i] = new MemeArenaFloorFracture.Slot
                    { root = root.gameObject, scar = renderer, fragments = fragments };
                root.gameObject.SetActive(false);
            }
        }
    }
}
