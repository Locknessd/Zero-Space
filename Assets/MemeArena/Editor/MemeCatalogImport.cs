using System;
using System.IO;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static class MemeCatalogImport
    {
        public static void Apply()
        {
            var definition = MemeCatalogDefinition.Load();
            if (definition.entries.Select(entry => entry.id).Distinct().Count() != definition.entries.Length ||
                definition.entries.Select(entry => entry.name.ToLowerInvariant()).Distinct().Count() != definition.entries.Length)
                throw new InvalidDataException("Catalog character IDs and display names must be unique.");
            string previousPath = File.ReadAllText(MemeArenaBuild.Root + "/Documentation/LatestGenerated.txt").Trim();
            var previous = AssetDatabase.LoadAssetAtPath<GameObject>(previousPath).GetComponent<MemeArenaStage>();
            string directory = AssetDatabase.GenerateUniqueAssetPath(MemeArenaBuild.Generated +
                "/WorkbookRoster_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            var sprites = MemeCatalogAtlas.Build(definition, previous.settings.crowdSettings, directory);
            var roster = Object.Instantiate(previous.settings.crowdSettings);
            roster.name = "Meme spectators from user workbook";
            roster.rosterId = definition.revision;
            roster.importedSourceRows = definition.sourceRows;
            roster.spectators = definition.entries.Select((entry, index) => new MemeCrowdSettings.Spectator
                { name = entry.name, sprite = sprites[index], weight = 1 }).ToArray();
            AssetDatabase.CreateAsset(roster, directory + "/CrowdSettings.asset");
            var settings = Object.Instantiate(previous.settings);
            try
            {
                settings.crowdSettings = roster;
                var prefab = MemeArenaBuild.Generate(settings);
                MemeArenaValidation.ValidatePrefab(prefab);
                var generated = prefab.GetComponent<MemeArenaStage>();
                var placed = JsonUtility.FromJson<PlacementReport>(File.ReadAllText(
                    generated.generatedAssetFolder + "/CrowdPlacement.json"));
                if (!new System.Collections.Generic.HashSet<string>(placed.names)
                    .SetEquals(roster.spectators.Select(spectator => spectator.name)))
                    throw new InvalidOperationException("Not every catalog character has a seat in the generated arena.");
                MemeArenaProduction.Apply();
                File.WriteAllText(MemeArenaBuild.Root + "/Documentation/LatestBattleScene.txt", MemeArenaProduction.BattlePath);
                File.WriteAllText(MemeArenaBuild.Root + "/Documentation/LatestWorkbookRoster.txt", directory);
                File.WriteAllText(MemeArenaBuild.Root + "/Documentation/Review/WorkbookImport.txt",
                    "PASS source rows=" + definition.sourceRows + "; unique crowd types=" + roster.spectators.Length +
                    "; all characters seated; retained existing sixteen portraits.\nAtlas: " + directory +
                    "/SpectatorAtlas.png\nPrefab: " + AssetDatabase.GetAssetPath(prefab) + "\n");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Serializable]
        public sealed class PlacementReport
        {
            public string[] names;
        }

        public static void InspectSheets()
        {
            var report = new System.Text.StringBuilder();
            var definition = MemeCatalogDefinition.Load();
            foreach (var group in definition.entries.Where(entry => string.IsNullOrEmpty(entry.existingName))
                .GroupBy(entry => entry.sheet))
            {
                string path = definition.SheetPath(group.Key);
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
                try
                {
                    ImageConversion.LoadImage(image, File.ReadAllBytes(path));
                    MemeCatalogSpriteRegions.Detect(image, group.Select(entry => entry.cell));
                    report.AppendLine("PASS " + image.name + "; subjects=" + group.Count());
                }
                catch (Exception error)
                {
                    report.AppendLine("FAIL " + error.Message);
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }
            File.WriteAllText(MemeArenaBuild.Root + "/Documentation/Review/WorkbookSpriteLayout.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
