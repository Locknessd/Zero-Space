# Contact placement repair contract

Goal: correct visible combat contact placement without altering confirmed hit times, damage,
move rules, source clips, source rigs, or contact event multiplicity. Root owns all Unity jobs.

Observed in real Play Mode at 1920x1080: After_Heavy_6_Heavy_2.png in
GeneratedAssets/EditorPolishReview shows first greatsword hit at source seconds 1.43667 with
blade crossing Meme at waist height but contact spark near foot. Bank cue Mankey Heavy_6
uses hasContactPoint true, contactBone 5, offset 0.49216086 -0.44914293 1.1783123.
BattleVfxPlayer.ContactPoint currently returns target bone TransformPoint authored offset.

Runtime sources are Assets/Scripts/Vfx/BattleVfxPlayer.cs and BattleWeaponTrails.cs;
source retargeting is Assets/DemoSence/Runtime/FrankBattlePairPlayback.cs. Existing Editor
calibration is Assets/DemoSence/Editor/FrankBattleHeavyContactSetup.cs. Do not change clocks.
The runtime camera recently required BakeMesh false plus position and rotation ONLY because
applying renderer scale again expanded Meme tenfold. Actual diagnostics are in
GeneratedAssets/EditorPolishReview/CameraBounds.txt. Do not assume mesh bake space.

Create a targeted project Editor setup/validation file Assets/Editor/BattlePresentationContactSetup.cs
and optional partials under 300 lines. Root can extend the request allowed-type list to invoke it.
You may edit only these new setup files and a concise Docs/CONTACT_PLACEMENT_REPAIR.md.
Prefer persistently correcting the existing per-cue anchors for both fighter profiles from
current evaluated weapon and target geometry, retaining artistic contact sources such as
Shield or limb hits. Preserve projectile and ground contacts. Check true rendered geometry,
not stale imported bounds. Report before/after distance to source and receiver, chosen bone,
local offset and exact source seconds for each changed cue. The runtime remains unchanged.

Self-check compile using project Unity references only; do not invoke Unity or save scene/bank
outside root operations. State exact callable method and root validation/capture steps. No
new imported assets, no destructive actions, no subagents. If recalibration cannot improve
placement safely, report concrete issue and leave it unchanged rather than masking the test.
