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
        static readonly Regex NamePattern = new Regex(@"\A[A-Za-z_][A-Za-z0-9_-]*\z");
        static readonly Regex HeaderPattern = new Regex(@"\A--- !u!([0-9]+) &([0-9]+)( stripped)?\z");
        static readonly Regex FieldPattern = new Regex(@"\A  [A-Za-z_][A-Za-z0-9_]*:.*\z");

        sealed class Span
        {
            public int Start;
            public int End;
            public string Text;
        }

        sealed class Registry
        {
            public int End;
            public readonly Dictionary<string, Span> Entries = new Dictionary<string, Span>(StringComparer.Ordinal);
        }

        static Dictionary<ulong, Span> Documents(string text)
        {
            var documents = new Dictionary<ulong, Span>();
            Span previous = null;
            foreach (Match line in Regex.Matches(text, @"(?m)^---[^\r\n]*"))
            {
                var match = HeaderPattern.Match(line.Value);
                ulong id;
                if (!match.Success || !ulong.TryParse(match.Groups[2].Value, out id) || id == 0 ||
                    documents.ContainsKey(id))
                    throw new InvalidDataException("Malformed or duplicate Unity document ID.");
                if (previous != null)
                {
                    previous.End = line.Index;
                    previous.Text = text.Substring(previous.Start, previous.End - previous.Start);
                }
                previous = new Span { Start = line.Index, End = text.Length };
                documents.Add(id, previous);
            }
            if (previous == null)
                throw new InvalidDataException("No Unity YAML documents found.");
            previous.Text = text.Substring(previous.Start);
            return documents;
        }

        static Registry ReadList(Span document)
        {
            string newline = Newline(document.Text);
            var lines = document.Text.Split(new[] { newline }, StringSplitOptions.None);
            var header = HeaderPattern.Match(lines[0]);
            if (header.Groups[1].Value != "114" || header.Groups[3].Success || lines[1] != "MonoBehaviour:" ||
                !lines.Contains("  m_PrefabInstance: {fileID: 0}") ||
                !lines.Contains("  m_CorrespondingSourceObject: {fileID: 0}") ||
                !lines.Contains("  m_EditorClassIdentifier: Assembly-CSharp::CharacterCombat"))
                throw new InvalidDataException("Expected an inline, non-prefab CharacterCombat component.");
            var result = new Registry();
            bool found = false;
            bool inside = false;
            int offset = document.Start;
            Span entry = null;
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in lines)
            {
                if (line.StartsWith("  heavyCombatMoves:", StringComparison.Ordinal))
                {
                    if (found || line != "  heavyCombatMoves:")
                        throw new InvalidDataException("Ambiguous or unsupported heavy combat list boundary.");
                    found = true;
                    inside = true;
                }
                else if (inside && line.StartsWith("  - moveName: ", StringComparison.Ordinal))
                {
                    FinishEntry(entry, offset, document, fields);
                    string name = line.Substring("  - moveName: ".Length);
                    if (!NamePattern.IsMatch(name) || result.Entries.ContainsKey(name))
                        throw new InvalidDataException("Unknown action name encoding or duplicate heavy action: " + name);
                    entry = new Span { Start = offset };
                    result.Entries.Add(name, entry);
                    fields.Clear();
                }
                else if (inside && FieldPattern.IsMatch(line))
                {
                    FinishEntry(entry, offset, document, fields);
                    result.End = offset;
                    inside = false;
                }
                else if (inside)
                {
                    if (entry == null || !line.StartsWith("    ", StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(line) || line.Contains("\t"))
                        throw new InvalidDataException("Malformed heavy action entry or missing array boundary.");
                    var field = Regex.Match(line, @"\A    ([A-Za-z_][A-Za-z0-9_]*):(?: |$)");
                    if (field.Success && (field.Groups[1].Value == "moveName" ||
                        !fields.Add(field.Groups[1].Value)))
                        throw new InvalidDataException("Duplicate field in heavy action entry.");
                }
                offset += line.Length + newline.Length;
            }
            if (!found || inside || result.Entries.Count == 0)
                throw new InvalidDataException("Missing or unterminated inline heavy combat registry.");
            return result;
        }

        static void FinishEntry(Span entry, int end, Span document, HashSet<string> fields)
        {
            if (entry == null)
                return;
            if (!fields.Contains("actionDefinition") || !fields.Contains("attackAnim") || !fields.Contains("hitAnim"))
                throw new InvalidDataException("Incomplete serialized combat action.");
            entry.End = end;
            entry.Text = document.Text.Substring(entry.Start - document.Start, end - entry.Start);
        }

        static string Merge(string saved, string source, ulong[] componentIds, string[] actionIds)
        {
            ValidateIds(actionIds);
            if (componentIds.Length == 0 || componentIds.Distinct().Count() != componentIds.Length)
                throw new InvalidDataException("Component IDs must be nonempty and distinct.");
            string newline = Newline(saved);
            string sourceNewline = Newline(source);
            var originals = Documents(saved);
            var snapshots = Documents(source);
            var edits = new List<Span>();
            foreach (ulong id in componentIds)
            {
                if (!originals.ContainsKey(id) || !snapshots.ContainsKey(id))
                    throw new InvalidDataException("Missing component in saved scene or snapshot: " + id);
                var target = ReadList(originals[id]);
                var current = ReadList(snapshots[id]);
                var added = new StringBuilder();
                foreach (string action in actionIds)
                {
                    Span replacement;
                    if (!current.Entries.TryGetValue(action, out replacement))
                        throw new InvalidDataException("Missing snapshot action " + action + " on " + id);
                    string value = replacement.Text.Replace(sourceNewline, newline);
                    Span existing;
                    if (target.Entries.TryGetValue(action, out existing))
                        edits.Add(new Span { Start = existing.Start, End = existing.End, Text = value });
                    else
                        added.Append(value);
                }
                if (added.Length > 0)
                    edits.Add(new Span { Start = target.End, End = target.End, Text = added.ToString() });
            }
            edits = edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.End).ToList();
            var output = new StringBuilder(saved.Length);
            int cursor = 0;
            foreach (var edit in edits)
            {
                if (edit.Start < cursor || edit.End < edit.Start)
                    throw new InvalidDataException("Overlapping action edits.");
                output.Append(saved, cursor, edit.Start - cursor);
                output.Append(edit.Text);
                cursor = edit.End;
            }
            output.Append(saved, cursor, saved.Length - cursor);
            string merged = output.ToString();
            AssertUnownedPreserved(saved, merged, edits);
            var mergedDocuments = Documents(merged);
            foreach (ulong id in componentIds)
            {
                var target = ReadList(mergedDocuments[id]);
                var current = ReadList(snapshots[id]);
                foreach (string action in actionIds)
                    if (!target.Entries.ContainsKey(action) ||
                        target.Entries[action].Text != current.Entries[action].Text.Replace(sourceNewline, newline))
                        throw new InvalidDataException("Merged action did not round trip exactly: " + action);
            }
            return merged;
        }

        static void AssertUnownedPreserved(string before, string after, List<Span> edits)
        {
            int oldOffset = 0;
            int newOffset = 0;
            foreach (var edit in edits)
            {
                int count = edit.Start - oldOffset;
                if (string.CompareOrdinal(before, oldOffset, after, newOffset, count) != 0)
                    throw new InvalidDataException("Merge changed content outside owned actions.");
                oldOffset = edit.End;
                newOffset += count + edit.Text.Length;
            }
            if (before.Length - oldOffset != after.Length - newOffset ||
                string.CompareOrdinal(before, oldOffset, after, newOffset, before.Length - oldOffset) != 0)
                throw new InvalidDataException("Merge changed the scene suffix.");
        }
    }
}
