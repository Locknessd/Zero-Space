# Battle audio and VFX polish

This pass reuses the existing `BattleSfxPlayer`, `BattleVfxPlayer`, `BattleSfxBank`, and weapon ribbons.
It adds no gameplay manager and does not change attacks, contact times, collision, damage, or weapon endpoints.
The broader visual/Editor report owns demo screenshots, live profiling, and final scene verification.

## Setup and validation

After compilation, run `BattlePresentationAudioSetup.Configure()` outside Play Mode, or use
**Tools → Battle → Presentation → Configure battle audio**. This imports the project-owned
`Assets/Audio/Battle/BattleMixer.mixer`, assigns the existing bank's routes/layers, ensures playback clips are
preloaded, and saves assets. It never opens or saves a scene. Re-running applies the initial mix preset,
including resetting start offsets to zero; do not re-run it over later hand-tuned values without review.

Setup now resolves the bank's saved clip GUIDs in their existing variant order, forces synchronous native
imports of the selected project-owned WAV copies, and only then reloads/rebinds AudioClip objects. This
repairs unresolved imported object references without substituting sounds or rewriting source audio.
It validates before saving. Importer type, asset path, actual clip GUID/fileID, and frame counts are recorded
in `GeneratedAssets/BattlePresentationReview/AudioClipImports.txt`; failures name the exact variant/path.

`BattlePresentationAudioSetup.ValidateAssets()` checks serialized mixer routes, clip/gain/offset arrays,
preload flags, unique groups, and layer references. It writes
`GeneratedAssets/BattlePresentationReview/AudioAssets.txt`.

`FrankRetarget.Editor.FrankRetargetBuilder.ValidateBattleSfx()` now drives the actual pair's `AdvanceTo`,
including its contact events. It checks both fighter bindings, ordered cues/layers, reverse/repeated sample
suppression, living/death recovery, cancellation, announcements, and the existing selected clips.
`ValidateAlignedWeaponVfx()` remains the endpoint/contact/trail validator.

Run the existing Play Mode validation and presentation workbench after setup. Verify contact audio alongside
pose holds, repeated heavies, both fighter sides, cancellation, match reset, and a fresh scene reload.
Watch `ActiveVoiceCount`, `PooledVoiceCount`, and `DroppedCueCount` on the player. Audition on actual audio
hardware: this subtask measured WAVs but did not listen to them or measure output latency.

## Contact and time ownership

`FrankBattlePairPlayback.AdvanceTo` crosses confirmed source-pair contact times in order. Gameplay damage
remains on the existing `TimelineAdvanced`/damage sequence path. `BattleVfxPlayer.ContactOccurred` now carries
the original bank cue plus a sequence/cue `eventId`; these are presentation identities, not network IDs.
The constructor's new arguments are optional for existing users. Contact publication does not depend on
successfully spawning a particle prefab.

The same-player audio subscriber handles hit and floor-contact cues exactly once per identity. It no longer
independently emits those cues from its timeline when the shared VFX publisher is available. Windup, swing,
comic accent, and recovery stay on the existing cue timeline. A scene without that publisher retains the
original accepted-pair audio timeline fallback. This project uses accepted scripted pairs; no new block/parry
outcomes or hurtbox mechanics were invented.

Particles use unscaled presentation time and can decay while the pair's local contact hold freezes its pose.
Flying skill effects still follow the source pair's sample time. Weapon ribbons remain on the pair clock.
The battle mixer uses `AudioMixerUpdateMode.UnscaledTime`, audio pitch is only each cue's authored variation,
and hit-stop never edits source pitch. Completed transient tails finish naturally; interrupted attacks,
match reset, disable, and scene destruction stop them. There are no delayed layer coroutines or scheduled
orphan voices. `SetMenuPaused(bool)` explicitly suspends/resumes battle sources; it does not own global time.
UI clicks can still play while this explicit audio pause is active. No menu-pause controller was added.

## Mix and bounds

- `BattleMixer` owns Master, Impacts, Weapons, Voices, and UI routes. Master defaults to −1.5 dB;
  Weapons and UI receive another −1 dB. Other buses start at 0 dB.
- The existing scene's master scalar remains 0.95. Changes update active sources immediately.
- The scene prewarms 12 SFX sources plus one reserved announcer source. Inspector range is 4–24 SFX voices.
- Each cue defaults to three simultaneous voices; heavy body weight is capped at two.
- Lower numeric source priority wins: announcer 32, KO 48, hurt voice 56, primary contact 64,
  optional weight 96, swings 100, remaining foley/UI 160.
- At saturation, a lower-priority cue cannot steal a more important active voice. Existing same-family
  voices are replaced oldest first at the family cap. Optional layers have one nesting level.
- Existing no-immediate-repeat selection is preserved and does not consume gameplay randomness.
  `reproducibleVariation` resets the separate audio random stream to `variationSeed` on match reset.
- Initial gain variation is ±0.65 dB (announcements 0); existing pitch ranges remain in the bank.
- `startOffsets` are seconds per clip, applied before playback, initially 0. Existing processed sources
  already have trim/fades. No destructive source-file edit or sample-accurate output claim is made.
- `heavy_hit` adds one quiet `heavy_weight` variant at the same requested onset, volume 0.18,
  pitch 0.97–1.02. Toggle `enableContactLayers` to compare. Listen before final mix approval.
- There is no battle music/ambience source to duck in this scene's current audio path. No unused ducking
  manager or imported template mixer was added. Clips use 2D output and Doppler 0 so camera zoom cannot
  change loudness or pitch.

## Actual audio sources retained

The existing `Assets/Audio/Battle/BattleSfxSelection.json` provides all 37 original-to-playback path mappings.
The following primary families are retained. Each source prefix is `Assets/Universal Sound FX/`;
each playback copy is under `Assets/Audio/Battle/Clips/Processed/` with its cue ID prefixed to the filename.

| Cue | Source path relative to the collection | Variants | Bus / role |
| --- | --- | --- | --- |
| `light_swing` | `WHOOSHES/Martial_Arts/MARTIAL_ARTS_Kick_Punch_RR1_mono.wav` (RR1–3) | 3 | Weapons / unarmed motion |
| `blade_swing` | `WHOOSHES/Air/WHOOSH_Air_Blade_RR1_mono.wav` (RR1–3) | 3 | Weapons / blade motion |
| `light_hit` | `IMPACTS/Generic/IMPACT_Generic_07_mono.wav`, `08`, `09_Short` | 3 | Impacts / contact transient |
| `heavy_hit` | `IMPACTS/Generic/IMPACT_Generic_13_mono.wav`, `14` | 2 | Impacts / heavy transient |
| `stab_hit` | `WEAPONS/Bow_Arrow/ARROW_Hit_Body_stereo.wav` | 1 | Impacts / retained piercing substitute |
| `body_fall`, `heavy_weight` | `THUDS_THUMPS/THUD_Dark_03_Short_mono.wav`, `THUD_Medium_01_mono.wav`, `THUD_Medium_02_mono.wav` | 3 | Impacts / floor and quiet body layer |
| `knockout_fall` | `THUDS_THUMPS/THUD_Dark_01_mono.wav` | 1 | Impacts / KO floor beat |

No external assets were acquired and no imported source assets were modified. Installed vendor assets remain
under their existing project license/provenance; this pass does not establish new redistribution rights.
Metal/shield cue families remain available in the bank but are not falsely triggered as defensive blocks.
Footsteps remain reserved: there is no new unverified foot-contact detector.

Waveform-only measurements of processed clips: primary light hits are 0.324–0.417 s long with threshold
onset 0.43–1.09 ms; heavy hits are 0.469–0.500 s with onset 0.23–0.66 ms. Body falls are 0.151–0.338 s with
onset 1.00–1.63 ms. All these measured peaks are −6 dBFS. The retained stab has a stronger onset at 16.92 ms;
blade whoosh RR3 at 15.49 ms. Threshold was 2% of each clip peak, not a perceptual latency measurement.
No offset has been forced on those two clips without listening.

## Actual VFX sources and roles

At audit time the saved battle scene binds `Assets/Vfx/Battle/Strong/StrongPunch.prefab`,
`StrongHeavy.prefab`, `StrongGroundImpact.prefab`, and `StrongSpreadingSmoke.prefab` for its core roles.
These are project-owned adaptations; earlier READMEs describe older bindings. Weapon-specific contact
prefabs and the calibrated `BattleWeaponTrails` system remain active. Final integration adapts these into project-owned variants under
`Assets/Rendering/BattleURP/Effects`; imported vendor shaders are preserved.

| Asset or workflow | Role / observation | Decision and remaining visual check |
| --- | --- | --- |
| `Assets/ErbGameArt/Sword slash FX/Prefabs/Slash4 orange.prefab` | Old path mesh; `Materials/SlashOneSide4.mat` uses `ERB/Particles/SlashOld` | Keep as reference; surface-shader/GrabPass dependency requires explicit URP adaptation |
| `Assets/ErbGameArt/Sword slash FX/Prefabs/New/Slash 4.prefab` | New particles; `Materials/Slash3cg.mat` and `Noise50cg.mat` | Candidate reference; primary material uses `Hovl/Particles/Add_CenterGlow`, optional depth sampling |
| `Assets/Vfx/Battle/CombatAccent/GreatSwordHeavyContact.prefab` | Existing adapted ring, dark/light sparks, and contact core | Retain asset for comparison; scene currently selects Strong family |
| `Assets/Vfx/Battle/WeaponTrails/WeaponRibbon.mat` | Existing shared ribbon material; actual calibrated blade endpoints | Retained; tail width/opacity now tapers while sampled tip remains attached |

All five requested demo paths were verified on disk. Static dependency audit: old slash scene references
53 prefabs; new slash scene references six prefabs and has 18 serialized particle systems; CFX1/2/3 scenes
reference 65/74/75 prefabs respectively. These counts are dependencies, not simultaneous runtime cost.
The new slash demo uses `DemoToonVFX` replay/previous/next controls and optional animation-timed activation.
Cartoon FX demos use `CFX_Demo_New` with arrows or UI selection, click-ground spawning, and delete cleanup.
Actual open/render results belong in the main Editor report; this subtask did not control the Editor.

## Ribbon tuning and cleanup

Existing styles keep their authored blade start and lifetime (typically 75–150 ms). Opacity now falls
quadratically over that life, with tail width collapsing toward each historical tip. The leading edge and
recorded weapon tip remain on their source path; no attack pose or hitbox moves to fit the effect.
`discontinuityDistance` defaults to 2.5 metres per sampled update; a larger root jump or changed playback ID
clears cached geometry. Tune relative to character scale after teleport/corner testing. Match reset,
interruption, disable, and a new sequence clear active meshes and source references. Missing hand bones
now have a safe renderer-centre fallback. Active trail/effect counters no longer allocate temporary lists.

Root validation passed shader checks, both-side captures, 36 endpoint cases, fresh-session mixer loading,
and repeated combat voice/particle/frame measurements. See `EDITOR_COMBAT_POLISH.md` and its evidence files.
Remaining external review: independent listening and hardware audio latency. A future menu owner must use
the explicit audio pause API; this scene currently has no separate menu pause owner.
