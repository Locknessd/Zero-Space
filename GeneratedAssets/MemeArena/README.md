# Meme Plaza and power slashes

Installed in `Assets/Scenes/BattleScene.unity` and `Assets/Scenes/BOMPepeTrailer.unity`.

## Arena

- Matte lavender combat court with a mint boundary, surrounding pavement and BOM floor lettering.
- Meme market facades, Pepe/Doge/banana/rocket graffiti, pastel skyline, pennants and shop signs.
- Frog sculpture, coin monuments, benches, planters and street lanterns outside the main combat area.
- 50 × 50 m continuous floor, with 100 floor colliders and 263 map renderers in each scene.
- Shared materials/primitive meshes, GPU instancing and one compressed, mipmapped graffiti atlas.
- The previous alley and checker-room roots were replaced; cameras, fighters, HUD and managers are retained.
- The darker FlatKit character materials and the cinematic camera, including the fixed blink ending, remain active.

Generated raster asset: `Assets/Rendering/MemeArena/Textures/MemeGraffitiAtlas.png`.
It was generated with the built-in **image_gen** tool, then copied into the project and used by four mural materials.
The complete generation prompt is in [GraffitiPrompt.txt](GraffitiPrompt.txt).
The original generated image remains under the Codex generated-images directory.

## Stronger blade audio

Generated clips: `Assets/Audio/Battle/MemeSlashes/`.
Four slash variants combine the existing sword/knife whooshes with a low air layer and metallic edge.
Two weighted cut-impact variants reinforce the blade contact groups.
Low-frequency EQ, short compression and a peak limiter add weight while preserving headroom.
The generated assets are short, mono, 48 kHz PCM clips and are preloaded in Unity.

The Battle bank retains its existing timelines, contact routing and mixer buses.
Only blade-specific groups use the new slash sounds; the generic unarmed-compatible swings are retained.
All 30 BOM slash cues use the new variants, with lower gain for overlapping dual-blade cuts and stronger finishers.
The three gun cues keep their existing sounds. The existing score is included in the new video.

`GenerateHeavySlashes.ps1` contains the source clip paths and reproducible FFmpeg processing recipe.
Measured combat segment: average volume -21.2 dBFS versus -27.0 dBFS before; peak -5.0 dBFS after AAC encoding.
This is an objective level comparison, not a speaker/headphone listening assessment.

## Validation and review

- `Validation.txt`: both scenes retain gameplay/camera/shader bindings and complete audio cue references.
- `GeneratedAssets/PepeShowcase/BomValidation.txt`: 2,640 sampled poses, floor coverage, framing and VFX checks pass.
- Actual Play recording: 1,006 camera action frames; final 297 fixed-camera frames have zero drift.
- Intro freeze: 246 rendered frames, zero pose/camera drift.
- `GeneratedAssets/PepeShowcase/BomDecode.txt`: full audio/video decode exit code 0.
- Actual exported intro, combat, gun and blink frames were reviewed.

Battle preview: `BattleMemePlaza.png`.
Combat sheet: `MemePlazaCombatFrames.png`.
Video: `GeneratedAssets/PepeShowcase/BOM_Pepe_MemePlaza_PowerSlashes.mp4`.
The standard `BOM_Pepe_CharacterTrailer.mp4` path contains the same new export.

The pre-change scene, bank and library files are preserved at the location in `BackupPath.txt`.
`SceneBackup_*` directories additionally preserve loaded scene state, including unsaved edits, before replacement.
The first scene backup contains the prior map; later copies preserve subsequent generated revisions.
The named previous `BOM_Pepe_DarkerShader.mp4` export remains available.

Rebuild menu: **Tools > Battle > Build meme plaza and powerful slashes**.
Creating the BOM trailer also rebuilds its meme arena and reapplies the stronger showcase slash cues.
