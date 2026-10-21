using UnityEngine;

namespace MemeArena
{
    public enum MemeArenaQuality { WebGL, Balanced, High }
    public enum MemeArenaLighting { WarmSunset, SoftDaylight }

    [CreateAssetMenu(menuName = "Meme Arena/Stage settings")]
    public sealed class MemeArenaSettings : ScriptableObject
    {
        [Header("Combat space — visual floor only")]
        [Min(16)] public float arenaWidth = 36;
        [Min(12)] public float arenaDepth = 28;
        public bool includeVisualFloor = true;
        [Header("Roman architecture")]
        [Min(12)] public float colosseumRadius = 19;
        [Range(3, 10)] public int seatingTiers = 6;
        [Range(24, 64)] public int arcSegments = 48;
        [Range(160, 230)] public float arcDegrees = 210;
        public bool cameraOnPositiveZ = true;
        [Header("Art overrides — preserved when regenerating")]
        public Material stoneMaterial;
        public Material floorMaterial;
        public Material goldMaterial;
        public Material bannerMaterial;
        public Texture2D skyTexture;
        public Texture2D distantCrowdTexture;
        public GameObject statuePrefab;
        public GameObject bannerPrefab;
        [Header("Crowd / performance")]
        public MemeArenaQuality quality = MemeArenaQuality.Balanced;
        public MemeCrowdSettings crowdSettings;
        [Range(0, 1)] public float crowdDensity = .85f;
        [Range(0, 96)] public int animatedCrowdCount = 48;
        [Range(0, 2)] public float reactionIntensity = 1;
        [Header("Lighting / effects")]
        public MemeArenaLighting lighting = MemeArenaLighting.WarmSunset;
        public bool includeLighting = true;
        public bool enableOutlines = true;
        [Range(.005f, .05f)] public float outlineWidth = .025f;
        public bool enableVfx = true;
        public bool enableConfetti = true;
        public bool enableFloorFracture = true;
        [Range(.5f, 3)] public float fractureRadius = 1.5f;
        [Range(0, 4)] public int torchLights = 2;
        public int seed = 16018;
        public float RearSign => cameraOnPositiveZ ? -1 : 1;
        public int EffectiveAnimatedCount => quality == MemeArenaQuality.WebGL ?
            Mathf.Min(24, animatedCrowdCount) : animatedCrowdCount;

        public void Sanitize()
        {
            arenaWidth = Mathf.Clamp(arenaWidth, 16, 80);
            arenaDepth = Mathf.Clamp(arenaDepth, 12, 70);
            colosseumRadius = Mathf.Clamp(colosseumRadius, Mathf.Max(12, arenaWidth * .5f + 1), 60);
            seatingTiers = Mathf.Clamp(seatingTiers, 3, 10);
            arcSegments = Mathf.Clamp(arcSegments, 24, 64);
            animatedCrowdCount = Mathf.Clamp(animatedCrowdCount, 0, 96);
            crowdDensity = Mathf.Clamp01(crowdDensity);
        }
    }
}
