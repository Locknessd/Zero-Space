# Combat expansion work in progress

The full requested expansion is **not complete**. This record separates saved integrations from pending
source studies and final quality gates. The authoritative request is the supplied combat expansion brief.

## Architecture and baseline

- Main integration scene: `Assets/Scenes/BattleScene.unity`.
- Running Editor: Unity 6000.5.9f1; saved ProjectVersion still 6000.4.0f1. URP 17.5.0, BattleHigh profile.
- The game presents server-resolved turn exchanges; it is not a free-movement fighting game.
- `GameManager` accepts/queues local Q/E or server outcomes, chooses the move, approaches range with
  `CombatPositioningController`, and calls `CharacterCombat.ExecuteAttack`.
- `FrankBattlePairPlayback` owns both source actors and one manually sampled animation clock.
- `BattleHitDamageSequence` distributes the accepted server total over contact cues. Client animation
  must not invent additional authoritative damage.
- `BattleVfxPlayer.ContactOccurred` is the shared impact event consumed by audio, hit-stop, flash,
  lighting, camera impulse, and combo feedback. Cosmetic callbacks do not independently deal damage.
- Source actor cleanup restores the visible Animator and existing recovery controller.
- Before expansion, both fighters had two light and seven heavy move registrations. Those are retained.

The live baseline Heavy_6 replay completed before changes. Its source-clock contact events and Game-view
presentation were inspected. Existing unsaved Samurai sample scene changes were preserved by the review
workbench before opening BattleScene additively.

## Coverage evidence

Run `FrankRetarget.Editor.CombatExpansionInventory.Audit` through the existing local Editor bridge,
or use Tools > Battle > Combat Expansion > Audit imported source coverage.

`GeneratedAssets/CombatExpansion/SourceInventory.json` and `SourceCoverage.tsv` contain exact imported
GUID/local-fileID identities, paths, names, durations, rig type, curve/event information, scope membership,
and current gameplay references. These are inventory evidence, not motion-quality approval.

| Source scope | Imported clip subassets |
|---|---:|
| Frank/Mankey/Pepe scene dependency graph | 126 |
| Double-axe Humanoid folder | 180 |
| KB_Hits | 37 |
| Samurai sample scene dependency graph | 22 |
| SlapFace | 4 |
| Rough Pack Demo | 2 |
| KB_Punches | 51 |
| KB_Kicks | 39 |

The union contains 461 identities. The counts include bind poses, support clips and source variants.
They do not imply 461 distinct combat actions. Canonical source equivalence and all final coverage
outcomes remain to be completed after motion inspection.

`CombatExpansionHumanoidStudy` provides separate punch, kick, axe-combo and reaction study commands.
`PrepareDrivers` completed in the Editor and generated 72 calibrated source drivers for both fighters.
All four study commands have now run: 102 punch records, 78 kick records, 66 axe-combo records and
74 reaction records, covering 160 exact clip identities on both fighters. Each record has a 60 Hz
trajectory and a chronological two-view image sheet. These remain source studies; they do not establish
victim contact, grip approval or gameplay coverage. Run `PrepareDrivers` before the study commands.

The original multi-pose sheets exposed stale skinned-body rendering within a single Editor frame.
`CombatExpansionPreviewSkin` now creates fresh CPU skin snapshots for each image sample; both the Vol10
and humanoid sheets were regenerated. The facial-bone audit found no local eye/jaw drift, and corrected
captures show the body and facial geometry together. No runtime facial rig change was made.

## Saved first integrations: Atemi1–3

All three source pairs are in `Assets/DemoSence/Vol10/Clips/`. `_N` and `_Y` are the paired performers.
They were previewed on actual Mankey and Pepe rigs using the existing calibrated Vol10 drivers.

| Gameplay ID | Source attacker / receiver | Contact clock | Response |
|---|---|---:|---|
| Vol10_CmnAtemi | CmnAtemi_N / CmnAtemi_Y | 0.500000 s | Authored recoil/knockdown, survivor GetUp |
| Vol10_CmnAtemi2 | CmnAtemi2_N / CmnAtemi2_Y | 0.516667 s | Authored close-range recoil |
| Vol10_CmnAtemi3 | CmnAtemi3_N / CmnAtemi3_Y | 0.400000 s | Authored turning response/separation |

These are coordinated interceptions with an approaching receiver, not generic jab animations.
The pairs share the current exchange clock; the receiver is not restarted when the contact cue fires.
Their choreography and spacing still need final visual review, especially during acquisition and exit.

Reachability:

- Registered in both fighters' light move pools; existing light input and server selection can choose them.
- Exact server `animationId` now resolves a registered move ID before the existing category fallback.
- `GameManager.EnqueueCombatAction(side, actionId)` uses the normal queue and positioning path.
- `PlaySingleAnimation(side, actionId)` recognizes exact registered IDs.
- The existing F8 animation panel also lists the new light moves.

`Assets/CombatExpansion/Actions/Vol10_*.asset` records source/reaction references, strike identity,
timing/window in seconds, region, continuation policy, result, trigger, and presentation. The installer
validates required reaction and routed audio references and matching positioned contact cues.

Presentation uses the existing light swing/hit groups, pooled light contact effect, lighting, flash,
camera impulse and owned hit-stop. Atemi1 adds an authored landing cue at 1.2 seconds and existing GetUp
audio. No weapon trails or new cinematic override are assigned to these unarmed actions. The normal
scene lighting, URP materials, grade and mixer setup are retained.

The shared contact cue now supports per-victim Avatar anchors. This avoids applying one skeletal offset
to two fighters with different scale and proportions. Existing cues keep their existing default anchor.
`AtemiContactCalibration.csv` records the measured triangle-surface gap for each role.

## Actual checks so far

- Runtime and Editor Roslyn compilation passed after the new code was imported.
- Compilation was rechecked before committing, including every new partial file: zero errors, with
  39 existing runtime warnings and 13 existing Editor warnings.
- `CombatExpansionVol10Study.StudyRequired`: six required paired motions, both fighter roles, 60 Hz
  trajectories, twelve-frame chronological contact sheets. Editor preview evidence only.
- `MeasureAtemiContacts`: actual skinned triangle surfaces, both hands near the observed motion beats.
- `CombatExpansionAtemiPlayCheck.Begin`: **20 normal gameplay queue cases passed**, including 12 Atemi
  cases across both avatars and both screen directions, bracketed by existing Light_1 and Heavy_6.
- Queue validation checked unique contact IDs, exact expected contact counts, per-avatar anchor use,
  live audio/VFX activity, lighting/hit-stop/camera counters and recovery of Animator/time ownership.
- Live contact captures were inspected. This does not yet verify every transition or abnormal exit.
- Current fresh Console output contains the pre-existing native graphicsApiMask dependency error;
  final project-error accounting remains part of the final regression pass.

The first queue test intentionally used local gameplay actions without server health changes. The
subsequent `FrankRetargetBuilder.BattleDamagePlayCheck` passed all 24 registered nonlethal exchanges
plus the lethal case, using exact action IDs for the new Atemis and the existing category route for
older moves. The isolated scene was copied from the saved BattleScene and restored after the test.

- 77 native damage contact frames checked against actual health text and slider values.
- Duplicate damage/HP events, late HP snapshots, lethal damage and winner announcements checked.
- 242 AudioSource starts, 227 VFX starts, 100 synchronized impact-light cues and 1157 held-pose frames.
- Camera safe-frame checks, KO slow motion, UI, pause/cancel/reset cleanup and bounded pools passed.
- Separate damage arithmetic checks passed 95 profile/total combinations, including tiny and 64-bit totals.

Evidence: `AtemiAuthoritativeDamagePlayMode.txt` and `AuthoritativeDamageValidation.txt` under
`GeneratedAssets/CombatExpansion`. This does not replace the remaining per-action abnormal-state and
normal-speed choreography review.

## Owned equipment state for paired actions

`TrumpWeaponManager` now supports overlapping temporary unarmed owners. A lease deactivates both weapon
sets and loose katana props, including collider/script hierarchies, and clears trails/particles. Explicit
gear changes during a lease update the requested equipment while keeping the visible hierarchy inactive.
Cleanup restores the latest request; stale tokens cannot restore an older weapon. Nonrestoring exits
remain unarmed until reset or a new authoritative equip request.

The shared paired player acquires this state on both base equipment managers: its source rig supplies
the attacker's authored weapon, and the receiver's source rig remains unarmed. Completion, cancellation,
disable and death release ownership according to actor state. Round reset invalidates previous tokens.
An unexpected actor death now cancels the owned pair, while its accepted lethal receiver outcome retains
the authored final pose. The isolated Editor equipment lifecycle validator passed. The actual-fighter
Play Mode check also passed all 20 cases: ordinary and lethal completion, newer equipment, overlapping
ownership, cancellation, either actor disabled, either actor dying, and round reset, on both fighter
roles. It uses temporary equipment fixtures and the registered Atemi2 pair. Evidence is recorded in
`EquipmentValidation.txt` and `EquipmentPlayMode.txt`.

The final source/Editor compile returned zero errors and the same 39 runtime/13 Editor warnings.
The lifecycle checks ran after an explicit script refresh; the test allows the deliberate disabled
Animator on an accepted lethal receiver, which remains driven by its retained terminal source pose.

This is shared interaction support. The Samurai execution clips still need their own integration and
verification; equipment support alone does not satisfy that collection.

## Outstanding scope and gates

- Complete source-role/canonical-duplicate coverage, original scene pair/controller traces and all
  required final coverage classifications.
- Integrate remaining distinct Frank demo content without duplicate registrations.
- Implement Hold acquisition/maintenance/release and real throw-escape windows, success/failure and
  synchronized cleanup; their clips are currently previewed only.
- Integrate every applicable double-axe combo, correct dual attachments, inspect every contact, and
  configure explicit reactions and complete feedback. No double-axe combo is yet integrated here.
- Integrate actual Samurai pairs with owned unarmed-victim equipment state and all required lifecycle
  cases. No new Samurai execution is yet integrated here.
- Integrate distinct SlapFace and Rough Pack actions with their required transitions/pairings.
- Author at least 20 distinct new punch/kick combos with full usable-source coverage. Current qualifying
  new punch/kick combo count: **0**. Atemi and existing combos do not count toward this requirement.
- Implement/verify strike confirmation, supported misses/blocks, reaction continuation, legal links,
  early KO, and the applicable existing rules; do not turn guaranteed animation queues into unsupported
  claims of fighting-game guaranteed combos.
- Validate all actions for invalid range/target, interruption, repetition, death/reset/disable, and
  restored equipment, trails, controls, camera, materials, lighting and mixer state.
- Complete every per-action presentation record, final normal-speed/contact-frame visual checks with
  optional shake/effects reduced, performance/pool checks, scene reload and fresh Play Mode verification.

No completion claim is supported until all of these requirements and the original brief are verified.
