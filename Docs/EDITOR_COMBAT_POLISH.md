# Editor combat presentation

## Direction and baseline

The working scene is `Assets/Scenes/BattleScene.unity`. The inspected Editor is Unity **6000.5.9f1**, OpenGL Core, RTX 4090, i9-13900K, 62,970 MB reported RAM. The repository version file originally said 6000.4.0f1. No engine installation was changed. Contrary to the brief's pipeline assumption, both quality tiers and GraphicsSettings had **no pipeline assigned**, and URP was absent from the manifest. Baseline captures are actual Game view images at 1853×780, High Quality, VSync off, no frame cap, focused Editor, domain reload enabled. Performance is not inferred from these screenshots.

Art direction: preserve Mankey's warm fur and Meme's simple green identity; separate their forms with colored shadows and restrained rim light. Use desaturated slate pavement, blue-gray metal, warm masonry, and selective amber lamps. Reserve near-white for small contact cores. Keep the detailed alley background quieter than faces, blades and contact silhouettes. Use existing gold/ink slash shapes and comic text, shorten emphasis, and move special titles into upper negative space. Optional bloom and shake must not carry the image.

The existing paired animation harness is the representative sequence: `Light_1`, `Heavy_6` (greatsword), then knockdown and recovery, replayed from both sides. No new combat mechanics or input bindings are introduced. Baseline sources are `GeneratedAssets/EditorPolishReview/Before_*.png`; supplied-recording samples are in its `Recording` folder. The MP4 is 145.3 seconds, 1920×1080, 30 FPS, with 48 kHz stereo AAC. Sampling the recording is distinct from testing the current scene.

The open old slash demo had unsaved edits. A copy was preserved under `GeneratedAssets/EditorPolishReview/UnsavedScenes` before BattleScene was opened additively. Imported assets and pre-existing working-tree edits are preserved. Use **Tools > Battle > Presentation > Restore isolated scene roots** after a review to reactivate other open scenes.

## Ownership

`GameManager` queues resolved exchanges and owns HP updates. `CharacterCombat` selects existing move data. `FrankBattlePairPlayback` samples both source animation rigs on one pair clock; its timeline advances damage and presentation. This is a queued, server-driven battle with a local animation preview, not a free-running collision fighter. There is no Cinemachine package or rollback simulation. `FrankCinematicCamera` owns framing; `BattleCameraShake` owns additive contact impulses. `BattleVfxPlayer`, `BattleWeaponTrails`, `BattleSfxPlayer`, and the existing HUD receive the same authored exchange. Keep these owners instead of adding parallel managers.

## Rendering and asset repair

Use the running Unity **6000.5.9f1** Editor for this pass. The repository still records
6000.4.0f1 in ProjectVersion.txt; that file was not used to install or upgrade an engine.
URP **17.5.0** comes from the installed Editor. The manifest now includes it. The obsolete
`com.unity.modules.vr` manifest entry was removed after it caused package resolution failure.
No WebGL build or public deployment was produced.

The white floor had two concrete causes: the battle FlatKit copies had lost their source
texture slots, and seven source alley textures were unresolved Git LFS pointer files.
The original 12 MB of texture content was restored from the repository, preserving GUIDs.
The converter reads original serialized texture properties even when a legacy shader is
unavailable. It preserves UV tiling and normal maps. Stage geometry uses tinted URP Lit;
characters and project-owned weapons use `Battle/URP/Toon Surface`; textured building
backdrops preserve their authored UV2 lightmaps through the URP backdrop shader.
Selected battle VFX use project-owned variants under `Assets/Rendering/BattleURP/Effects`.
Imported vendor scenes, prefabs, and materials are not blanket-converted.

The camera bounds audit also found doubled scale: Meme's baked mesh was transformed by
its tenfold renderer scale a second time. The corrected fitting transform uses the baked
geometry with position and rotation, avoiding a 27-unit unnecessary safety dolly.

| Asset / control | Authored setting | Purpose |
| --- | --- | --- |
| `Assets/Rendering/BattleURP/BattleHigh.asset` | Render scale 1, 4x MSAA, HDR, 2048 main shadows, 2 cascades, 32 m shadow range | Primary desktop Editor profile |
| `BattleReduced.asset` in the same folder | Render scale 0.85, 2x MSAA, 1024 main shadows | Lower GPU cost without changing combat |
| `BattleRenderer.asset` | Forward 3D renderer, SRP batcher, up to 4 additional lights, no additional-light shadows | Mesh fighters and bounded contact lights |
| `BattleGrade.asset` | Neutral tonemapping, bloom intensity 0.12 / threshold 1.2, contrast +5, saturation -4 | Restrained highlights; no exposure animation |
| Main key light | Warm tint, intensity 1.05, soft shadow, bias 0.035 / normal bias 0.12 | Character form and floor contact |
| `BattleLightingRig` | Combat dimming off; impact intensity 0.65; existing two-light pool | Localized contact emphasis |

`BattleUrpPolishSetup.Apply()` is a repeatable initial preset, not a runtime manager.
It intentionally resets its listed tuning values; do not run it over later hand tuning.

## Contact, clock, and accessibility controls

Existing gameplay damage advances on `FrankBattlePairPlayback.TimelineAdvanced`.
`BattleVfxPlayer.ContactOccurred` carries the accepted pair, fighters, move, contact kind,
world point, source seconds, finishing flag, cue, and sequence/cue identity. Contact audio
subscribes to this same presentation event; missing or exhausted particle prefabs cannot
suppress it. Swing/recovery cues remain on the authored pair timeline. Presentation IDs
are not network deduplication IDs. The existing GameManager owns received-message handling.

| Owner | Control and units | Initial value / cleanup |
| --- | --- | --- |
| `BattleImpactFeedback` | Light / heavy / ground / finishing hold, real seconds | 3 / 6 / 4 / 8 ticks at 60 Hz (50 / 100 / 66.7 / 133.3 ms) |
| Same component | KO playback rate / duration / recovery | 0.32 rate, 0.30 s hold, 0.16 s recovery; existing enable flag retained |
| Same component | Flash scale; material highlight strength / duration | 0.65; 0.55 / 65 ms; set flash scale to zero for reduced flashes |
| `BattleCameraShake` | Reference displacement at 1080-pixel screen height | 2 / 6 / 4 / 9 px for light / heavy / ground / finishing; capped at 9 px |
| Same component | Motion preference / roll / envelope | motionScale 0–1, roll 0 degrees, 90 ms light / 180 ms other contacts |
| `FrankCinematicCamera` | Authored angle/radius limits | pitch 12 degrees, yaw ±12 degrees, radius 6.8 m before 0.82 scene scale; body/weapon fitting can expand |
| Same component | Action-center blend / shot transition | 0.75 / 0.36 s; existing move-specific takes retained |
| `BattleComicCutIn` | Compact title timing and layout | 0.72 s, width 48%, anchor Y 81%, reference height 110 px |
| `BattleWeaponTrails` | Existing weapon-specific life and discontinuity distance | Typically 75–150 ms, tapered tail; clear on a root jump over 2.5 m or sequence change |
| `BattleSfxPlayer` | Voice cap / optional contact layer | 12 SFX + 1 reserved announcer; weight layer at gain 0.18; explicit menu-pause API |

Hit-stop and KO slow motion change only the accepted pair's playback rate. They do not
write `Time.timeScale` or `Time.fixedDeltaTime`. Menu pause remains externally owned and
cannot be canceled by hit-stop cleanup. Disable, interruption, reset, and changed playback
identity release the local hold. Surface flashes use restored MaterialPropertyBlocks;
there is no per-hit material instance. Camera impulse restores its base pose and keeps
fitted bodies inside the safe viewport. The development panel remains Editor-only (F8).

Audio routes, exact retained source paths, variants, trims, priorities, and waveform-only
measurements are documented in [BATTLE_AUDIO_VFX_POLISH.md](BATTLE_AUDIO_VFX_POLISH.md).
The Linux Editor's AAC importer initially lacked ffmpeg. A persistent ffmpeg executable and
Git LFS utility were made available under the user's local bin directory; selected WAVs
were synchronously reimported. No sound source file was replaced. There is no active music
or ambience owner in this battle path, so no unused ducking system was added.

## Replay and verification

1. Open `Assets/Scenes/BattleScene.unity` in the current Editor. If preserving other open
   dirty scenes, **Tools > Battle > Presentation > Open battle safely** saves copies and
   isolates their active roots without saving their originals.
2. Enter Play Mode. F8 opens the existing attack list. Replay `Light_1`, `Heavy_6`,
   `Heavy_Katana`, and `Heavy_Assassin`, then switch the attacker. These are real battle
   sequences with reaction, damage preview, contact feedback, and recovery.
3. **Tools > Battle > Presentation > Run two minute combat review** cycles real moves
   and writes Game view captures, source contact timestamps, and Editor counters under
   `GeneratedAssets/EditorPolishReview`. It requires an idle battle and Play Mode.
4. For reduced motion/flashes, set `motionScale` and `flashScale` to zero. For a bloom
   comparison, disable only the Bloom override in the shared Volume or its runtime copy.
5. Restore isolated scene roots with the matching menu when returning to the prior scene.

The local file-job helper is Editor-only: a JSON request in
`Library/BattlePresentationRequest.json` is answered in `Library/BattlePresentationResponse.txt`.
Actions include status, safe battle opening, play/stop, replay, capture, seek/resume,
refresh, and a restricted set of project Editor validation methods. There is no network
endpoint and no replacement runtime singleton. Seek is diagnostic and pauses the Editor.

Verified evidence currently preserved in `GeneratedAssets/EditorPolishReview`:

- `LiveIntegration.txt`: all 18 nonlethal local exchanges plus repeated lethal damage and
  winner messages; 226 AudioSource starts, 209 VFX starts, 92 impact-light cues; audible
  sample output present, mixed peak 0.310, pause/reset/cancel/KO cleanup passed. This is
  output measurement, not a subjective listening test.
- `CameraValidation.txt`: 72 cases / 5,824 sampled frames, both fighters, both directions,
  landscape and portrait, cancel and return to neutral. The live integration also checked
  32,512 camera frames through recovery and KO.
- `GeneratedAssets/CombatFeelVideoReview/PlayModeValidation.txt`: exact participant contact
  holds even when one frame crosses a combo, overlapping requests, unchanged physics step,
  external pause, cancel/reset/disable cleanup, and queued light/heavy completion.
- Banner validation covers both sides, five title types, and three aspect ratios. Compact
  titles keep their reserved upper strip rather than crossing the main fighting band.
- `SustainedReview.txt`: final 120-second desktop Editor run, 18 moves, 27,534 measured
  frames after a 10-second warmup. Median 3.90 ms, p95 5.37 ms, p99 7.77 ms; 3 frames over
  16.67 ms, maximum 27.00 ms. Peak active effects 4, voices 3 of 13, particles 1,472.
  Unity allocated memory grew 4.71 MB overall and 2.13 MB after warmup. These totals
  include Editor work and do not establish a leak or allocation-free battle code.
- `WeaponHoldRenderValidation.txt`: 56 native-weapon contacts, 1,014 held render frames;
  every sampled world bone matrix remained at its emitted contact pose.
- `TrailValidation.txt`: 36 lethal/nonlethal cases, 116 sweeps, 536 blade endpoint comparisons,
  zero measured endpoint error; skipped-combo, deduplication, cancellation and pool reuse passed.
- `DamageIntegration.txt`: all 18 server-damage exchanges plus duplicated lethal and result
  messages; 71 damage-contact frames, 226 audio starts, 209 VFX starts, 92 lights,
  32,184 safe camera frames; HP snapshots and duplicate-message behavior passed.
- `ReloadPersistence.txt`: saved BattleScene reopened with its pipeline and 14 authored roots;
  missing-script/material and shader-support audit clean. Original dirty scene preserved.

Performance conditions: RTX 4090 / i9-13900K, OpenGL Core, 1920×1080, High Quality,
render scale 1, 4x MSAA, VSync off, uncapped, no Deep Profile. All 27,534 measured frames
reported focused. SetPass averaged 47.55, peaked 66; triangles averaged 99,510, peaked 131,027.
GPU recorder values were zero (unusable), draw-call recorder unavailable, transparent
overdraw not measured. GC counters include Editor and validation work: average 13,939 bytes,
peak 368,451 bytes per recorded frame. This is not a shipping-build benchmark. Baseline FPS
was not measured; before screenshots are visual evidence only. The earlier run remains
in `FirstSustainedReview.txt`; its 207.74 ms outlier and partial focus are not concealed.

## Footage and remaining review limits

The full exact reference video was recovered and decoded at 1280×720 / 60 FPS, 336.28 s.
[EDITOR_REFERENCE_TIMING.md](EDITOR_REFERENCE_TIMING.md) contains nine observed combat
beats, cited source frames, and separate inferences/proposals. These do not establish
proprietary engine behavior or exact game frame data. The 25 cited stills are retained;
raw downloaded footage and extraction cache are ignored by Git. No reference art or audio
was incorporated into game assets, and no external assets were purchased.

The supplied recording was sampled across its full duration and around its slam, heavy
sword, Blade Fury, and Phantom Strike sequences. Its 30 FPS frames cannot resolve every
60 Hz gameplay tick. Independent audio audition, hardware latency, final speaker/headphone
mix, custom facial animation, and new hand-authored pose work are not claimed. Existing
clips and humorous character identity are retained. No new free movement, guard/parry,
aerial combat, character swapping, cloth, or destruction mechanics were invented.

Final validation and contact results follow below. Historical Console errors include resolved setup failures
and an Editor-native graphicsApiMask dependency mismatch; fresh battle errors are checked
separately. Unity service/licensing network messages are not battle exceptions.

## Final demo and contact review

All five supplied vendor demos were opened independently in Play Mode, triggered twice,
and captured. `DemosPlay/Report.txt` ends with completion and an exact restored scene/root
snapshot. Both slash scenes render white streaks but their legacy stage is magenta in URP.
CFX1 double flame, CFX2 bats and CFX3 debris/smoke visibly render over a magenta legacy floor.
This is partial effect compatibility, not a claim that the vendor scenes are URP-ready.
Original imported scenes/materials are preserved. The battle uses the project-owned URP variants.

| Demo | Actual trigger | Captured result / selection |
| --- | --- | --- |
| Old mesh-path slash | Restart authored 78-system gallery | Thin white streaks; incompatible stage; retain shape reference |
| New slash | Native `Counter(0)` / `Counter(1)`, Slash 1 / Slash 2 | Directional white streaks; incompatible stage; retain reference |
| CFX1 | Native spawn, CircularLightWall / DoubleFlameA | Flame visible; first 180 ms sample reports no live particles; avoid wall-sized coverage |
| CFX2 | Native spawn, BatsCloud / BatsCloudHeavy | Bat silhouettes visible; rejected as unrelated to this battle's contact roles |
| CFX3 | Native spawn, Beetles / Broken_Machine_Clouds_Black | Debris/smoke visible; selected battle landing variants remain more localized |

The generic particle conversion initially omitted CFXR font channel colors. The battle
particle shader now respects the existing custom face/outline streams; the imported font
source is unchanged. Clean slam captures show readable light WOOSH lettering.

A first heavy screenshot exposed a real hold bug: the fighter pose froze but the source
Animator refreshed its native weapon bones, moving the visible blade away from the spark.
Both explicit bind-pose skinning and event-time diagnostics confirmed that the authored
first `Heavy_6` anchor is a low blade contact. `FrankBattlePairPlayback` now reapplies the
same sampled pose during participant hold/menu pause without advancing the timeline or
emitting additional events. The final screenshot shows blade, ribbon and spark together.
This was not repaired by moving the effect to a visually drifting weapon.

The calibration tool saved one evaluated right-foot anchor improvement; the other 59 melee
anchors were already close. Its reports separate source/receiver distances, both directions,
and rejected proposals. No cue time or damage value changed. Render-time weapon-bone
validation supplements the existing timeline tests so a held clock alone cannot mask drift.

Play Mode exit also exposed a destroyed-Animator cleanup call. Initialization/reset and
preview restoration now guard Unity object lifetime; a destroyed Animator is not treated
as an initialized live actor. Fresh exit/re-entry is part of the final check.

## Review images and final state

These are captured Game view images, not mockups. Neutral before/after images share
1853×780 resolution; the after image includes the intentionally revised camera framing.
Idle animation phases are not frame-identical. Contact captures use the real `Heavy_6`
and `Light_1` path at 1920×1080, approximately 45 ms after the contact event.

- [Before neutral](../GeneratedAssets/EditorPolishReview/Before_Neutral.png)
- [After neutral, matching resolution](../GeneratedAssets/EditorPolishReview/Final_Neutral_BaselineResolution.png)
- [Final neutral at 1080p](../GeneratedAssets/EditorPolishReview/Final_Neutral_1080.png)
- [Mankey heavy contact](../GeneratedAssets/EditorPolishReview/Final_Mankey_Heavy_6_Heavy_1.png)
- [Meme heavy contact](../GeneratedAssets/EditorPolishReview/Final_Pepe_Heavy_6_Heavy_1.png)
- [Heavy without bloom, shake or flash](../GeneratedAssets/EditorPolishReview/Reduced_Mankey_Heavy_6_Heavy_1.png)
- [Slam without bloom, shake or flash](../GeneratedAssets/EditorPolishReview/Reduced_Mankey_Light_1_Ground_1.png)

`FinalConsoleCheck.txt` records zero project exception/shader-error blocks after the final
clean Console checkpoint, fresh Play Mode entry, scene audit, captures and exit. Four
Editor-native dependency mismatch blocks remain. `FinalEditorState.txt` records Edit Mode,
BattleScene not dirty, no busy queue or preview, time scale 1, original fixed step,
High URP profile and 1920×1080 Game view. The pre-existing dirty old slash scene remains
loaded with its isolated roots and saved recovery copies; its source asset was not overwritten.
Normal feedback preferences were restored before the final session. No build or deployment ran.
