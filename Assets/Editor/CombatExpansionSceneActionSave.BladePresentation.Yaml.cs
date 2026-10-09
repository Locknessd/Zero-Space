using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSceneActionSave
    {
        const string BladeClass = "  m_EditorClassIdentifier: Assembly-CSharp::BattleWeaponTrails";
        const string BladeScript =
            "  m_Script: {fileID: 11500000, guid: 556185c146228424cb6b5e5693fb0ab9, type: 3}";
        const string BladeNumber = @"[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][-+]?[0-9]+)?";
        const string BladeReference = @"\{fileID: (?:0|-?[1-9][0-9]*, guid: [0-9a-f]{32}, type: [23])\}";

        static Span BladeDocument(string yaml, ulong id)
        {
            if (id == 0 || !Documents(yaml).TryGetValue(id, out var document))
                throw new InvalidDataException("Missing BattleWeaponTrails component: " + id);
            return document;
        }

        static Dictionary<string, Span> ReadBladePresentation(Span document, bool requireExcluded)
        {
            string newline = Newline(document.Text);
            string[] lines = document.Text.Split(new[] { newline }, StringSplitOptions.None);
            var header = HeaderPattern.Match(lines[0]);
            if (!header.Success || header.Groups[1].Value != "114" || header.Groups[3].Success ||
                lines.Length < 3 || lines[1] != "MonoBehaviour:")
                throw new InvalidDataException("Expected an inline, non-prefab BattleWeaponTrails document.");
            var fields = new Dictionary<string, Span>(StringComparer.Ordinal);
            Span current = null;
            int offset = document.Start + lines[0].Length + lines[1].Length + 2 * newline.Length;
            for (int index = 2; index < lines.Length - 1; index++)
            {
                string line = lines[index];
                var match = Regex.Match(line, @"\A  ([A-Za-z_][A-Za-z0-9_]*):(?: |$)");
                if (match.Success)
                {
                    FinishBladeField(current, offset, document);
                    string name = match.Groups[1].Value;
                    if (fields.ContainsKey(name))
                        throw new InvalidDataException("Duplicate BattleWeaponTrails field: " + name);
                    current = new Span { Start = offset };
                    fields.Add(name, current);
                }
                else if (current == null || line.Contains("\t") || string.IsNullOrWhiteSpace(line) ||
                    !(line.StartsWith("    ", StringComparison.Ordinal) ||
                    line.StartsWith("  - ", StringComparison.Ordinal)))
                    throw new InvalidDataException("Malformed or unsupported BattleWeaponTrails field boundary.");
                offset += line.Length + newline.Length;
            }
            FinishBladeField(current, document.End, document);
            foreach (string name in new[] { "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset" })
                RequireBladeField(fields, name, "  " + name + ": {fileID: 0}" + newline);
            RequireBladeField(fields, "m_EditorClassIdentifier", BladeClass + newline);
            RequireBladeField(fields, "m_Script", BladeScript + newline);
            if (!fields.ContainsKey("staticBlades") || requireExcluded && !fields.ContainsKey("excludedMeshes"))
                throw new InvalidDataException("Missing required blade presentation field.");
            ValidateBladeField(fields["staticBlades"].Text.Replace(newline, "\n"), false);
            if (fields.TryGetValue("excludedMeshes", out var excluded))
                ValidateBladeField(excluded.Text.Replace(newline, "\n"), true);
            return fields;
        }

        static void FinishBladeField(Span field, int end, Span document)
        {
            if (field == null)
                return;
            field.End = end;
            field.Text = document.Text.Substring(field.Start - document.Start, end - field.Start);
        }

        static void RequireBladeField(Dictionary<string, Span> fields, string name, string expected)
        {
            if (!fields.TryGetValue(name, out var value) || value.Text != expected)
                throw new InvalidDataException("Unexpected or missing BattleWeaponTrails metadata: " + name);
        }

        static void ValidateBladeField(string text, bool excluded)
        {
            string vector = @"\{x: " + BladeNumber + ", y: " + BladeNumber + ", z: " + BladeNumber + @"\}";
            string bone = "    - bone: [0-9]+\n      point: " + vector + "\n      weight: " + BladeNumber + "\n";
            string bones = @"(?: \[\]\n|\n(?:" + bone + ")+)";
            string blade = "  - mesh: " + BladeReference + "\n    bladeBase: " + vector +
                "\n    bladeTip: " + vector + "\n    baseBones:" + bones + "    tipBones:" + bones;
            string entry = excluded ? "  - " + BladeReference + "\n" : blade;
            string name = excluded ? "excludedMeshes" : "staticBlades";
            string pattern = @"\A  " + name + @":(?: \[\]\n|\n(?:" + entry + @")+)\z";
            if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
                throw new InvalidDataException("Malformed or unsupported serialized field: " + name);
        }

        static string MergeBladePresentation(string saved, string source, ulong id)
        {
            string newline = Newline(saved);
            string sourceNewline = Newline(source);
            var target = ReadBladePresentation(BladeDocument(saved, id), false);
            var current = ReadBladePresentation(BladeDocument(source, id), true);
            var edits = new List<Span>();
            foreach (string name in new[] { "staticBlades", "excludedMeshes" })
            {
                bool exists = target.TryGetValue(name, out var field);
                int start = exists ? field.Start : target["staticBlades"].End;
                edits.Add(new Span
                {
                    Start = start,
                    End = exists ? field.End : start,
                    Text = current[name].Text.Replace(sourceNewline, newline)
                });
            }
            edits = edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.End).ToList();
            var output = new StringBuilder(saved.Length);
            int cursor = 0;
            foreach (var edit in edits)
            {
                if (edit.Start < cursor || edit.End < edit.Start)
                    throw new InvalidDataException("Overlapping blade presentation edits.");
                output.Append(saved, cursor, edit.Start - cursor);
                output.Append(edit.Text);
                cursor = edit.End;
            }
            output.Append(saved, cursor, saved.Length - cursor);
            string merged = output.ToString();
            AssertUnownedPreserved(saved, merged, edits);
            var verified = ReadBladePresentation(BladeDocument(merged, id), true);
            foreach (string name in new[] { "staticBlades", "excludedMeshes" })
                if (verified[name].Text != current[name].Text.Replace(sourceNewline, newline))
                    throw new InvalidDataException("Blade presentation field failed round trip: " + name);
            return merged;
        }
    }
}
