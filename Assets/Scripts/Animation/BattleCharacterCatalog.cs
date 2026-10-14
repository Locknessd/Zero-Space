using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Battle/Character catalog")]
public sealed class BattleCharacterCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string displayName;
        public GameObject prefab;
        public Sprite portrait;
        [Min(.001f)] public float referenceLegLength = 1;
        [Min(0)] public float referenceHeight;
    }

    [Min(.1f)] public float characterHeight = 2;
    [HideInInspector, Min(.01f)] public float sourceMotionScale = .88f;
    public Entry[] characters = Array.Empty<Entry>();

    public Entry Find(string id) => Array.Find(characters, entry => entry != null &&
        string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase));
}
