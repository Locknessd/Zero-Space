using UnityEngine;

namespace MemeArena
{
    public sealed class MemeCrowdSpawner : MonoBehaviour
    {
        public MemeCrowdSettings settings;
        public Transform[] seats = System.Array.Empty<Transform>();
        public GameObject[] generatedFans = System.Array.Empty<GameObject>();
        public int seed = 16018;

        // Scene/prefab crowds are baked by the editor tool. Runtime population is explicit.
        // Reuse the generated objects instead of allocating a fresh crowd each round.
        public void SetVisible(bool visible)
        {
            foreach (var fan in generatedFans) if (fan) fan.SetActive(visible);
            foreach (var group in GetComponentsInChildren<MemeCrowdGroup>(true))
                foreach (var batch in group.staticBatches)
                    if (batch) batch.enabled = visible;
        }

        public void Populate()
        {
            if (generatedFans.Length > 0)
            {
                SetVisible(true);
                return;
            }
            if (!settings || settings.spectators.Length == 0) return;
            var random = new System.Random(seed);
            var spawned = new System.Collections.Generic.List<GameObject>();
            foreach (var seat in seats)
            {
                if (!seat) continue;
                var type = ChooseSpectator(random);
                GameObject fan;
                if (type.prefab) fan = Instantiate(type.prefab, seat, false);
                else
                {
                    fan = new GameObject(type.name);
                    fan.transform.SetParent(seat, false);
                    fan.AddComponent<SpriteRenderer>().sprite = type.sprite;
                }
                foreach (var collider in fan.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                var animation = fan.GetComponent<MemeCrowdFan>();
                if (!animation) animation = fan.AddComponent<MemeCrowdFan>();
                animation.phase = (float)random.NextDouble() * Mathf.PI * 2;
                animation.speed = Mathf.Lerp(2, 3.1f, (float)random.NextDouble());
                spawned.Add(fan);
            }
            generatedFans = spawned.ToArray();
            foreach (var group in GetComponentsInChildren<MemeCrowdGroup>(true))
                group.animatedFans = group.GetComponentsInChildren<MemeCrowdFan>(true);
            GetComponentInParent<MemeCrowdReactionManager>()?.RebuildCache();
        }

        MemeCrowdSettings.Spectator ChooseSpectator(System.Random random)
        {
            float total = 0;
            foreach (var spectator in settings.spectators) total += Mathf.Max(.01f, spectator.weight);
            float remaining = (float)random.NextDouble() * total;
            foreach (var spectator in settings.spectators)
            {
                remaining -= Mathf.Max(.01f, spectator.weight);
                if (remaining <= 0) return spectator;
            }
            return settings.spectators[settings.spectators.Length - 1];
        }
    }
}
