# Optional lethal pair infrastructure

Unassigned infrastructure; neither provisional Slap receiver is accepted gameplay.

`CombatActionDefinition.lethalPairVariant` optionally supplies a full receiver
clip, full pair grounding, complete presentation profile and explicit contact
mappings. Selection occurs once before playback takes ownership. Absent data and
nonlethal playback preserve the native path. Invalid assigned lethal data rejects
playback before ownership or equipment changes. Grapple variants are unsupported.

The variant retains the original attacker and all primary damage identities,
groups, times and counts. Additional landing cues must be explicitly mapped and
nondamaging. Both avatar grounding tracks cover the full pair duration. Selected
receiver duration, sampling, grounding, presentation, contact timing and knockback
use the selected data without modifying shared base assets.

The in-memory `FrankRetarget.Editor.CombatExpansionLethalVariantCheck.Validate`
fixture passed in Unity on 2026-10-09 (local date): absent/nonlethal compatibility,
full receiver selection and duration, unchanged primary damage and total damage,
nondamaging landing, slow-frame/duplicate advance handling, and rejection of
15 invalid configurations. Runtime and Editor private compilation passed with
zero errors. This fixture does not establish real-rig variant acceptance.

Before assignment, both Slap receiver candidates still require measured grounding,
contact and landing authoring, final visual/audio review and lifecycle validation
on both playable avatars and directions.
