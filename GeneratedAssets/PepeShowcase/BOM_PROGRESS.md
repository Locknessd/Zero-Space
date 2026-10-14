# BOM / Battle of meme — Pepe character trailer

Updated: 2026-10-13, Asia/Saigon. Meme Plaza map, stronger blade audio, cinematic camera and darker FlatKit tone.

Final video: `BOM_Pepe_MemePlaza_PowerSlashes.mp4`.
`BOM_Pepe_CharacterTrailer.mp4` contains the same updated export.
Approximately 33.76 seconds, 1920×1080 at 60 FPS, H.264/AAC stereo at 48 kHz.
File size: 20,963,333 bytes. Recorded from actual isolated Unity Play mode.

## Format

- 0–4 s: BOM / Battle of meme opening card.
- 4–11.4 s: Pepe startup animation and character camera reveal.
- 11.4–15.8 s: frozen pose, frozen camera and PEPE character card.
- 15.8–17 s: transition to combat.
- Critical Dual → gun/sword Combo03 → Assassin Combo04 → Assassin Combo03 → Assassin Skill05.
- Five unique attacks with carried motion and 120 ms rig entry blending.
- Final 2.2 s: ending card.

## Current camera

Reference: https://www.youtube.com/watch?v=y8dA1CIABAk — Cinematic Attack Animation, Rizky Sanjaya.
Local footage and analysis sheets are in `References/CinematicAttack*`.
`References/CinematicAttackCameraNotes.md` records observations and approximate reference timestamps.

The first four attacks use directed three-quarter viewing angles, fitted dolly movement and body tracking.
The camera elevates with airborne movement and lowers for the attack's recovery.
The follow target is filtered over 140 ms; the endpoints preserve carried pose continuity.
Posed weapon geometry is fitted at each sampled camera angle with conservative look-ahead for zoom changes.
Adjacent shots share distance and orientation at their boundary, with 650 ms distance easing.
The final Skill05 blink shot stays fixed and frames its full trajectory.

Pepe's actual fitted distance range is 6.318–11.541 m, including the final wider fixed shot.
The desired following distance is 6.5 m; fitting increases it when required by the weapon/body pose.
Camera positions and rotations are calculated from animation time, so seeking does not change the shot.

## VFX and materials

- The new Meme Plaza map is installed in both BattleScene and BOMPepeTrailer.
- Market facades display generated Pepe/Doge/banana/rocket graffiti; props include a frog statue and coin monuments.
- The 50 × 50 m floor covers the whole animation sequence; each map has 263 renderers and 100 floor colliders.
- Native FlatKit URP surface materials remain applied to the character/weapons.
- Material RGB gains are 0.62 for the base color and 0.56 for shaded areas, preserving alpha and textures.
- Source color tags make repeated preset application stable; VFX/trail brightness remains independent.
- Thirty slash cues and three gun cues remain synchronized with the animations.
- Sword ribbons retain 170% reach, soft gradients and a bright white/cyan core.
- World-space glow lasts 320 ms; suspended air cuts and glints remain for 720 ms.
- Native Assassin effects, motion wakes, takeoff rings and landing rings remain present.
- Gun flashes retain white/gold cores, flame jets, pressure rings, sparks and soft smoke.
- Muzzle cores follow the barrel for the first 75 ms; smoke/sparks fade over up to 380 ms.
- Existing synthesized score and game SFX remain in the export. No reference footage/audio is included.
- All 30 slash cues use four layered power-slash variants; blade contact groups also use two weightier impacts.
- The combat segment's average level is -21.2 dBFS, up 5.8 dB from the prior export, with peaks at -5.0 dBFS.
- Original generic/unarmed swings and all three gun cues keep their existing sounds.

## Reopen, tune and record

Scene: `Assets/Scenes/BOMPepeTrailer.unity`.
Library: `Assets/PepeShowcase/BomPepeTrailerLibrary.asset`.
On the scene camera's PepeShowcaseCamera component, use **Cinematic attack reference (BOM trailer)**.
The reference camera is enabled in BOM; earlier showcase libraries keep their existing camera behavior.

Unity menu commands under **Tools / Pepe Showcase**:

1. **Apply cinematic attack reference camera** updates the existing BOM camera configuration.
2. **Validate BOM Pepe trailer** checks animations, VFX, framing and camera continuity.
3. **Record BOM Pepe trailer** exports the complete trailer from isolated Play mode.

Automation: `powershell -NoProfile -ExecutionPolicy Bypass -File GeneratedAssets/PepeShowcase/RunBomPipeline.ps1`.
`Create BOM Pepe trailer` rebuilds the generated scene and may replace manual edits in that scene.

## Verification

- Unity C# compilation succeeded; no shader compilation errors were reported.
- `BomValidation.txt`: 2,640 poses across Pepe, Mankey and custom Humanoid cases.
- Head, hips, feet and hands remain in the action frame. Dolly and deterministic seeking pass.
- All 30 slash cues, suspended air cuts and 3 barrel-aligned muzzle bursts pass for all character cases.
- Camera rig boundary deltas stay below 0.5 mm.
- `BomRecordedCamera.txt`: 1,006 rendered action frames; maximum camera turn 1.869 degrees/frame.
- Final fixed shot: 297 rendered frames, zero camera drift.
- Character reveal: 246 rendered freeze frames, zero pose/camera drift.
- `CompleteValidation.txt`: the earlier complete-combo showcase passed its existing regression checks.
- `BomDecode.txt`: full audio/video decode exit code 0. Actual exported review sheets were inspected.
- BattleScene, BOM scene/library and the Battle SFX bank were updated for the requested map/audio revision.
- The earlier named video exports remain available; the pre-map scene/bank/library backup is in MemeArena/BackupPath.txt.
- The existing Unity Editor was retained and returned to Edit mode in BattleScene after the darker-tone recording.
- FlatKit material support and defender flash/restoration passed again after the brightness adjustment.

Previous camera source, scene, library, main video and reports were preserved under
`Revisions/20261013_204411_BeforeReferenceCamera/`. The prior named muzzle-flash video remains unchanged.
The named `BOM_Pepe_CinematicCamera.mp4` export preserves the previous brighter version.
The named `BOM_Pepe_DarkerShader.mp4` export preserves the previous map and quieter blade sound mix.
The brightness backup location is in `GeneratedAssets/FlatKitIntegration/DarkerToneBackupPath.txt`.
The before/after comparison is `GeneratedAssets/FlatKitIntegration/DarkerToneComparison.png` (old left, new right).
Map installation, texture-generation prompt, audio recipe and review files: `GeneratedAssets/MemeArena/README.md`.
