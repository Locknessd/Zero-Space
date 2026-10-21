using UnityEngine;

namespace MemeArena
{
    [DisallowMultipleComponent]
    public sealed class MemeArenaStage : MonoBehaviour
    {
        public MemeArenaSettings settings;
        [HideInInspector] public Transform geometry;
        [HideInInspector] public Transform crowd;
        [HideInInspector] public string generatedAssetFolder;
        public Bounds CombatBounds => new Bounds(transform.position,
            new Vector3(settings ? settings.arenaWidth : 36, 12, settings ? settings.arenaDepth : 28));

        void OnDrawGizmosSelected()
        {
            if (!settings) return;
            Gizmos.color = new Color(.2f, 1, .7f, .65f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * .025f,
                new Vector3(settings.arenaWidth, .05f, settings.arenaDepth));
        }
    }
}
