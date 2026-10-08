Frank canonical verification is a focused supplement to the 126-identity coverage audit. Run
`FrankRetarget.Editor.CombatExpansionFrankCanonicalCheck.Validate()` in the existing Unity 6000.5 editor,
or use **Tools → Frank Retarget → Validate canonical clip relationships**. It writes only
`GeneratedAssets/CombatExpansion/FrankCanonicalValidation.tsv` and `FrankCanonicalValidation.txt`.
It does not open scenes, instantiate actors, sample scene objects, change importers, modify assets, refresh the
AssetDatabase, save assets, or register actions. The operation ran successfully in Unity 6000.5.9f1 on
2026-10-08. It reported 20 exposed-content matches, 43 content differences, 34 representation differences,
16 rejected recorded-offset slices, and 12 uncertified slices, with no error or unresolved-asset classifications.
These are comparison counts, not distinct gameplay-action counts; canonical pose equivalence remains unproven.
The TSV identifies every compared object by GUID and local file ID, with its current asset path.

The seven rig clips all use local ID `1827226128182048838`. Their exact original controller source references
and both saved Sets references are embedded in `CombatExpansionFrankCanonicalCheck.Data.cs`, captured from
metadata and controller motion fields. Names are descriptive labels only. The verifier compares each rig to its
original selected mesh clip, each rig to both saved attacker clips, and the original clip to both saved clips.
It also checks actual state-machine membership and records the referencing states' stable IDs.

| Weapon | Rig GUID | Original controller clip GUID |
| --- | --- | --- |
| 2Handed | 0ecbaf72cc6563b3788f1d0796e10828 | 23f690a146a2b8e418e1ab3e0aea5064 |
| Assassin | 887c136fb6189f3bf8919955b7a6ead6 | 2c73e3dd92553e942b71dcb39237c52c |
| Dual | 1d628956d53416971b3e2753725ecfb4 | 3f4878697c191ea46886ad1a0c45435e |
| GreatSword | f9d4896795dbdf2eaa74e2b782a33753 | 60816012dd2c96449be704f9ba3e7bdc |
| Katana | a3b4748d4a8e7dfd4b510849d3ea6340 | 7e2666062ee2d55429beea6899bf89dc |
| Spear | a74c88880dbb6d6a5b23708a0ca7d294 | 378caf935aaa27a48a2ce063a9b9f60c |
| Warrior | 2cc53f050cdcab3c99304b15928061e4 | d8dd30cce5c56764b847fc7ea75a2ff3 |

`FrankRetargetBuilder.Prepare` copies selected mesh FBXs to Rigs and configures their Humanoid imports.
`ReplaceMotions` reads the actual original controller motion. When that motion is Humanoid, it substitutes the
Generic SourceMotion FBX as curve source, while preserving the original events and clip settings with the
start/stop interval reset. Existing saved clips are reused, so this construction code alone proves no current
content equality. The validator reports FBX byte equality separately from imported clip equality and includes
import rig/compression settings. A Humanoid/Generic representation mismatch never receives an equality label.

The selected Warrior take is `0650c1aa78a175348929f66c5076a6fa:1827226128182048838`.
The original Warrior reaction controller is `c6349909da1f24c499267270030f7bda:9100000`:
its disconnected `Hit2` state points to that selected first take, while its default state references
`7de125855c1d397418fdcfc954ba3fa3:7400000` (`hit_combo_weapon(7).anim`). The derived builder explicitly
corrects and attaches the alternate state. Therefore the selected FBX must not be identified as the alternate
action from the state label alone. Verification covers both saved default reactions, their original controller
motion, Generic first-take source `6df2ef8fe0d0d2e6cb763cef597ca470:1827226128182048838`, and corrected
second-take source `e64b262abd6656593aeeb26d8e275590:1827226128182048838`. It also compares the two saved
takes and reports disconnected serialized references separately from state-machine membership. State membership
alone does not prove a gameplay transition or battle registration.

Insane verification loads library GUID `2fdca4303a3f85a1088fa56820e55f52` and uses its actual clip references
and numeric group/step metadata. Twenty explicitly recorded source GUID/local-ID pairs cover 17 attacks and
three full-combo reactions. `CopyInsaneClip` copies separate authored Generic FBXs for both full combos and
steps, then clears legacy and loop settings. It does not construct step attacks by slicing the full clip.
`MatchStep` estimates a starting offset using selected bone rotations at 20%, 35%, and 50% of the step;
its metadata is an approximate correspondence and provides no equality proof.

For each of the 14 steps the operation compares source versus saved content, full versus step content, and
entry/recovery curves against the full clip at the recorded `sourceMatchTime`. It checks all 136 attack pairs
and all 136 reaction pairs for exact exposed-content duplicates. Missing assets reduce comparison counts and
produce unresolved rows. No findings automatically create or remove battle registrations.

`GenerateStepReaction` builds a new idle entry, impact/recoil blend, translated recovery tail, vertical fall,
smoothed root track, and final hold. Those generated reactions are derivatives, not simple source slices by
construction. Current saved content is still compared to the full reaction and checked at the recorded offset;
the operation does not rerun the generator or claim the current saved output matches a fresh generation.

The report classifications have deliberately narrow meanings:

- `EXPOSED_CONTENT_EQUAL`: all exposed float/object bindings and keys, events, settings, and metadata agree.
- `CONTENT_DIFFERS`: at least one compared component differs; the report identifies the first differing part.
- `REPRESENTATION_DIFFERS_NO_EQUIVALENCE`: Humanoid/Generic representations differ; no pose equality is asserted.
- `STRICT_KEY_SLICE_AT_RECORDED_OFFSET`: all boundary-aligned, shifted keys, object keys, and events agree.
  Settings remain a separate comparison. This conservative check can reject equivalent curves with different keys.
- `NOT_EXACT_SLICE_AT_RECORDED_OFFSET`: the interval exceeds the full clip or entry/recovery scalar values differ
  by more than `0.00001` in a 25-sample window. This proves a difference at that offset only.
- `SLICE_NOT_CERTIFIED`: strict slice equality was not established and the sampled windows did not disprove it.
- `UNRESOLVED_*`, `REFERENCE_MISSING`, `UNEXPECTED_SCOPE`, or `ERROR`: investigate before closing the related link.

Float-key comparison includes times, values, in/out tangents, weights, weighted modes, tangent modes, broken
flags, and curve wrap modes. Object keys and event object arguments use persistent identities. Events include
ordered timestamps, function/string/float/integer arguments, and message options. All reflected clip-settings
fields are compared exactly; an unsupported field produces an error rather than being omitted. Clip length,
frame rate, legacy/wrap modes, Humanoid flag, and root/motion-curve flags are also compared. SHA-256 fingerprints
are accompanied by binding counts and separate curve/event/settings equality fields. Normalized settings are
reported as additional evidence only; the full settings comparison remains authoritative.

The verification operates on imported curves exposed by Unity's editor APIs. It does not certify hidden native
Humanoid data, avatar retargeting, quaternion pose equivalence, contact quality, camera presentation, damage,
recovery readability, or gameplay distinctness. Scalar quaternion sign differences can represent identical
orientations. Slice classification does not search every possible offset or time warp, and sampled agreement
alone never receives an exact-equality label. The earlier coverage report and inventory are intentionally
unchanged; consume this operation's runtime results before resolving their pending canonical links.
