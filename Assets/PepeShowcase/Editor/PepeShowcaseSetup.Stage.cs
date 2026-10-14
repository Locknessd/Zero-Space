using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        static void BuildStage(Scene scene, PepeAttackShowcase showcase)
        {
            var white = Material("Porcelain", new Color(.69f, .77f, .79f), .3f);
            var floorWhite = Material("Floor pearl", new Color(.5f, .6f, .61f), .22f);
            var grey = Material("Floor graphite", new Color(.28f, .35f, .37f), .36f);
            var panels = Material("Cyan panels", new Color(.6f, .85f, .88f), .25f, new Color(.3f, .8f, .9f) * .7f);
            var wall = Material("Pale concrete", new Color(.46f, .62f, .65f), .15f);
            var room = new GameObject("Checker room and luminous panels").transform;
            for (int x = -5; x <= 5; x++)
                for (int z = -5; z <= 5; z++)
                    Box("Floor tile", room, new Vector3(x * 2, -.055f, z * 2), new Vector3(2, .1f, 2),
                        (x + z) % 2 == 0 ? floorWhite : grey);
            Box("Backdrop", room, new Vector3(0, 4, -9), new Vector3(22, 8, .4f), wall);
            for (int x = -5; x <= 5; x++)
                for (int y = 0; y < 5; y++)
                {
                    float depth = (x + y) % 2 == 0 ? .4f : .1f;
                    Box("Back wall module", room, new Vector3(x * 1.65f, .8f + y * 1.55f, -8.6f + depth),
                        new Vector3(1.58f, 1.48f, depth), (x + y) % 3 == 0 ? panels : white);
                }
            for (int side = -1; side <= 1; side += 2)
                for (int z = -4; z <= 3; z++)
                    Box("Side monolith", room, new Vector3(side * 9, 2.5f, z * 2),
                        new Vector3(.7f, 5, 1.3f), z % 2 == 0 ? panels : wall);
            Light("Soft daylight", LightType.Directional, new Vector3(0, 7, 0),
                Quaternion.Euler(43, -35, 0), Color.white, 1.05f, 0);
            Light("Cyan fill", LightType.Point, new Vector3(-3, 3, 1), Quaternion.identity,
                new Color(.42f, .8f, 1), 1.1f, 12);
            Light("Warm edge", LightType.Point, new Vector3(3, 4, -3), Quaternion.identity,
                new Color(1, .82f, .65f), 1.2f, 12);
            Light("Backdrop wash", LightType.Point, new Vector3(0, 4, -5), Quaternion.identity,
                new Color(.75f, .95f, 1), 1.4f, 12);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.62f, .74f, .77f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.55f, .76f, .8f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .035f;
            var cameraRoot = new GameObject("Showcase Camera");
            var camera = cameraRoot.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 80;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            cameraRoot.AddComponent<AudioListener>();
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.volumeLayerMask = 1 << 30;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            showcase.director = cameraRoot.AddComponent<PepeShowcaseCamera>();
            showcase.director.output = camera;
            camera.transform.position = new Vector3(-2, 1.1f, 3.5f);
            camera.transform.LookAt(new Vector3(0, .9f, 0));
            var volume = new GameObject("Showcase grade").AddComponent<Volume>();
            volume.gameObject.layer = 30;
            volume.isGlobal = true;
            volume.sharedProfile = Profile();
            var trail = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Blade trail.mat");
            if (!trail)
            {
                trail = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                trail.SetColor("_BaseColor", new Color(.4f, .95f, 1, .45f));
                trail.SetFloat("_Surface", 1);
                trail.SetFloat("_Blend", 0);
                trail.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                trail.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                trail.SetFloat("_ZWrite", 0);
                trail.renderQueue = 3000;
                AssetDatabase.CreateAsset(trail, Root + "/Materials/Blade trail.mat");
            }
            showcase.presentation.trailMaterial = trail;
        }

        static Material Material(string name, Color color, float smoothness, Color emission = default)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (emission.maxColorComponent > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static VolumeProfile Profile()
        {
            string path = Root + "/ShowcaseGrade.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (!profile.TryGet<Bloom>(out var bloom)) bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(.24f);
            bloom.threshold.Override(1.2f);
            if (!profile.TryGet<Tonemapping>(out var tone)) tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            if (!profile.TryGet<ColorAdjustments>(out var color)) color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-.15f);
            color.contrast.Override(9);
            color.saturation.Override(-6);
            if (!profile.TryGet<DepthOfField>(out var depth)) depth = profile.Add<DepthOfField>(true);
            depth.mode.Override(DepthOfFieldMode.Gaussian);
            depth.gaussianStart.Override(6);
            depth.gaussianEnd.Override(13);
            depth.gaussianMaxRadius.Override(1.35f);
            depth.highQualitySampling.Override(true);
            foreach (var component in profile.components)
            {
                if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = name;
            node.transform.SetParent(parent);
            node.transform.position = position;
            node.transform.localScale = scale;
            node.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(node.GetComponent<Collider>());
        }

        static void Light(string name, LightType type, Vector3 position, Quaternion rotation,
            Color color, float intensity, float range)
        {
            var node = new GameObject(name);
            node.transform.SetPositionAndRotation(position, rotation);
            var light = node.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = type == LightType.Directional ? LightShadows.Soft : LightShadows.None;
            light.shadowBias = .035f;
        }
    }
}
