using System;
using System.IO;
using UnityEngine;

namespace MemeArenaEditor
{
    [Serializable]
    public sealed class MemeCatalogDefinition
    {
        public string revision;
        public string sourceFile;
        public int sourceRows;
        public string[] sourceSheets;
        public Entry[] entries;

        public string SheetPath(int index)
        {
            if (sourceSheets == null || index < 0 || index >= sourceSheets.Length)
                throw new InvalidDataException("Missing source sheet path for index " + index);
            string path = sourceSheets[index];
            string root = Path.GetFullPath("Assets/MemeArena/Art") + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source sheets must be PNG files inside MemeArena/Art.");
            return path;
        }

        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string name;
            public string existingName;
            public int sheet;
            public int cell;
        }

        public static MemeCatalogDefinition Load()
        {
            const string path = "Assets/MemeArena/Documentation/RobinhoodMemeCatalog.json";
            var definition = JsonUtility.FromJson<MemeCatalogDefinition>(File.ReadAllText(path));
            if (definition == null || definition.entries == null || definition.entries.Length == 0)
                throw new InvalidDataException("The character catalog has no entries.");
            return definition;
        }
    }
}
