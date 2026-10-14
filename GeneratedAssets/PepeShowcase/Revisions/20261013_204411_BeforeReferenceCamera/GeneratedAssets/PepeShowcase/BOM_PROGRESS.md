# BOM / Battle of meme — Pepe character trailer

Updated: 2026-10-13, Asia/Saigon. Nearer camera and enhanced gun muzzle explosions.

Final video: `BOM_Pepe_NearCamera_MuzzleFlash.mp4`.
The standard `BOM_Pepe_CharacterTrailer.mp4` path also contains this updated export.
Duration: approximately 33.76 seconds. 1920×1080, 60 FPS, H.264, AAC stereo at 48 kHz.
File size: 13,204,301 bytes. Actual Play mode rendering; original synthesized score and game SFX.

## Format

- 0–4 s: BOM / Battle of meme title card, cyan/magenta accents, short opening fade and title zoom.
- 4–11.4 s: Pepe's Humanoid Taunt startup animation, moving from a full shot into the character reveal.
- 11.4–15.8 s: genuinely frozen Pepe pose and camera, large slanted PEPE name and colored graphic strokes.
- 15.8–17 s: transition to combat.
- From 17 s: Critical Dual → gun/sword Combo03 → Assassin Combo04 → Assassin Combo03 → Assassin Skill05.
- Final 2.2 s: BOM ending card fades in.

Five unique attack clips remain connected with carried motion and the existing 0.12 s entry blend.
The first four shots follow the character. The final Skill05 blink shot remains fixed, with a smooth lead-in.
Following shots now use 9.600 m instead of the previous 10.400 m, approximately 7.7% closer.
The final fixed shot uses 11.541 m instead of 11.793 m, with a smooth 650 ms lead-in before it begins.
Camera fitting uses posed weapon vertices in view space rather than inflated world-axis bounding-box corners.

## VFX and lighting

- Additional blade ribbons follow sampled weapon positions at 120 Hz, with 170% reach and a bright cyan/white core.
- Ribbons have soft color gradients and a white/cyan edge instead of flat color bands.
- Ribbons leave a fading world-space glow for 320 ms; discontinuous blink segments start new ribbons.
- Thin energy cuts and small glints remain suspended in the air for 720 ms after the cut ends.
- Air-cut snapshots remain at their world positions across attack/weapon rig changes, then fade away.
- Procedural slash particles are scaled to 165%; native Assassin weapon effects remain present.
- Blade glints, gun flashes, motion wakes, takeoff rings and landing rings remain synchronized.
- All three shots have larger white/gold muzzle cores, short flame jets, pressure rings, sparks and soft smoke.
- The enhanced flash follows the barrel tip during its initial 75 ms; sparks and smoke fade over up to 380 ms.
- Existing muzzle particles are scaled to 220% in the BOM trailer; the other showcase modes keep their scale.
- Calibrated blade points avoid unreadable static-mesh vertex access in Play mode.
- A separate BOM stage profile and front key light improve Pepe visibility against the blue arena.
- Reference videos informed the presentation and effect style; no reference footage or audio is in the export.

## Reopen and record

Scene: `Assets/Scenes/BOMPepeTrailer.unity`.
Library: `Assets/PepeShowcase/BomPepeTrailerLibrary.asset`.

Unity menu commands under **Tools / Pepe Showcase**:

1. **Validate BOM Pepe trailer** checks the intro, animations, weapons, effects and camera.
2. **Record BOM Pepe trailer** records the complete video in an isolated Play mode scene.

`Create BOM Pepe trailer` rebuilds the generated scene and settings from the preserved complete-combo source.
Close the generated BOM scene before rebuilding it. Rebuilding replaces changes made to that generated scene.

Automation from the project directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File GeneratedAssets/PepeShowcase/RunBomPipeline.ps1
```

## Verification

- Unity compilation succeeded after the final changes.
- `BomValidation.txt`: 2,640 sampled poses across Pepe, Mankey and custom Humanoid cases.
- All 30 slash cues and 3 gun cues pass; ribbons, afterglows and suspended air cuts pass for every slash.
- Each cue's suspended air cut is checked 400 ms after its emission ends, beyond the main ribbon glow.
- All three muzzle explosions pass on Pepe, Mankey and custom Humanoid cases: no early flash,
  less than 1 cm barrel alignment error, visible sparks/smoke at 200 ms, and no remaining burst at 400 ms.
- Seam checks and deterministic seeking pass for all three character cases.
- `BomRecordedCamera.txt`: 246 rendered freeze frames with zero pose/camera drift.
- Final fixed shot: 297 rendered frames, zero camera drift; follow distance 9.600 m, fixed distance 11.541 m.
- `BomDecode.txt`: full audio/video decode exit code 0; final container has 2,025 decoded video frames.
- `BomRecording.txt`: 2,026 frames rendered; final mux ends at the audio duration during the ending card.
- Actual exported intro, combat, air-cut, gunshot and blink frames were reviewed in five `Bom*Frames.png` sheets.
- All 12 protected original files remain byte-identical; see `MuzzlePreservation.Before.json` and `.After.json`.
- Existing Unity process 10636 was retained during this revision; no Editor restart was performed.
- Editor returned to Edit mode in `Assets/Frank_Slash_Pack/Demo/Frank_Slash_Pack.unity`.

The generated BOM scene/library/profile/materials are separate from BattleScene and the previous showcase scenes.
The prior `PepeCompleteCombo_StableCameraVFX.mp4` export remains unchanged.
The immediately previous BOM video/scene/library/camera report and notes are preserved in
`Revisions/20261013_190357_BeforeMuzzle/`.
The prior `BOM_Pepe_CloseCamera_AirTrails.mp4` named export remains unchanged.
