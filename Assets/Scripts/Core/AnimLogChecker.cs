using UnityEngine;

/// <summary>
/// SINGLE ON/OFF FLAG FOR ALL ANIMATION + LATENCY LOGGING.
///
/// WHY THIS EXISTS
/// The animation pipeline logs from several places (GameManager, CombatPositioningController,
/// CharacterAnimatorBridge, AnimationEndAction). They used to carry three different prefixes
/// ("Animation", "LATENCY", "[TurnStack]"), so collecting a useful trace meant searching the Console
/// several times and pasting the results together.
///
/// This class puts ONE tag on every line -- "[AnimLogChecker]" -- so the whole trace can be gathered
/// with a single Console filter and sent on as-is.
///
/// HOW TO USE
///   * Tick <see cref="Enabled"/> in the Inspector (it is a static flag, so it only needs setting once),
///     or flip the toggle at runtime via <see cref="SetEnabled"/>.
///   * In the Unity Console, type:  AnimLogChecker
///     and every animation / latency line is listed together, in order.
///   * Optionally set <see cref="Filter"/> (e.g. a turnId) to narrow the trace to one turn.
///
/// Each line also keeps its CATEGORY (BE-GAP / FLUSH / QUEUE / ...) so the sections stay readable:
///     [AnimLogChecker][BE-GAP] turn 'abc' first event arrived 2.340s after the previous turn's last event
///
/// NOTE: This is diagnostics only. Turn <see cref="Enabled"/> off for a normal build; the calls are
/// cheap when disabled (a single bool test before the string is built).
/// </summary>
public static class AnimLogChecker
{
    /// <summary>Master switch. When false, every Log call in this class is a no-op.</summary>
    public static bool Enabled = true;

    /// <summary>
    /// The one tag every line carries. Filter the Console with this exact string to collect the whole
    /// trace. Kept as a const so callers can print it in instructions.
    /// </summary>
    public const string Tag = "[AnimLogChecker]";

    /// <summary>
    /// Optional substring filter. When non-empty, only lines whose message CONTAINS this text are
    /// emitted. Handy for isolating one turn (set it to that turnId). Empty = log everything.
    /// </summary>
    public static string Filter = "";

    /// <summary>Set the master switch at runtime (e.g. from a debug key or the Inspector).</summary>
    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled) Debug.Log($"{Tag} logging ENABLED. Filter the console with \"{Tag}\" to collect the full trace.");
    }

    /// <summary>
    /// Logs one line tagged with <see cref="Tag"/> and a category, e.g.
    /// "[AnimLogChecker][QUEUE] ...". No-op when logging is off or the line does not match the filter.
    /// </summary>
    public static void Log(string category, string message)
    {
        if (!Enabled) return;

        string line = string.IsNullOrEmpty(category)
            ? $"{Tag} {message}"
            : $"{Tag}[{category}] {message}";

        // Filter is checked against the FULL line so a turnId or a category can both be used to narrow.
        if (!string.IsNullOrEmpty(Filter) && line.IndexOf(Filter, System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        Debug.Log(line);
    }

    /// <summary>Logs a warning line with the same tag, so problems are not lost in the trace.</summary>
    public static void LogWarning(string category, string message)
    {
        if (!Enabled) return;

        string line = string.IsNullOrEmpty(category)
            ? $"{Tag} {message}"
            : $"{Tag}[{category}] {message}";

        if (!string.IsNullOrEmpty(Filter) && line.IndexOf(Filter, System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        Debug.LogWarning(line);
    }
}/// <summary>
/// Drop-in MonoBehaviour wrapper for <see cref="AnimLogChecker"/>, so the single logging flag can be
/// toggled straight from the Inspector (and survives a domain reload) instead of from code.
///
/// USAGE
///   1. Add this component to any object in the scene (e.g. the GameManager object).
///   2. Tick "Enabled", press Play, reproduce the slow turn.
///   3. In the Unity Console, filter with:  AnimLogChecker
///      -> every animation + latency line is listed together in order. Copy them and send the trace.
///   4. Optionally put a turnId in "Filter" to isolate a single turn.
///
/// The values are pushed into the static <see cref="AnimLogChecker"/> on Awake and whenever they change
/// in the Inspector, so no other code needs to reference this component.
/// </summary>
[DisallowMultipleComponent]
public class AnimLogCheckerBehaviour : MonoBehaviour
{
    [Tooltip("Master switch for all animation + latency logging. Tick this, reproduce the issue, then filter the Console with 'AnimLogChecker'.")]
    public bool enabled_ = true;

    [Tooltip("Optional substring filter. Only lines containing this text are logged (e.g. one turnId). Leave empty to log everything.")]
    public string filter = "";

    private void Awake()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    /// <summary>Pushes the Inspector values into the static flag.</summary>
    private void Apply()
    {
        AnimLogChecker.Enabled = enabled_;
        AnimLogChecker.Filter = filter;
    }
}