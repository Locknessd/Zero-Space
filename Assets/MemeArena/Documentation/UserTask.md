# TASK: Complete Meme Colosseum Stage — Unity 6 URP

You are a Senior Unity 6 Gameplay Engineer, Technical Artist, and Environment Tools Developer.

I have an existing 3D meme fighting game built with Unity 6 and Universal Render Pipeline (URP).

I have imported the **MemeColosseum Unity 6 URP v0.2 package** into my project.

Your task is to inspect, repair, complete, and visually improve this package into a functional, high-quality, comic-style 3D fighting arena.

**IMPORTANT: Work directly in my Unity project. Do not create an unrelated demo project. Preserve my existing fighting gameplay.**

## PHASE 1 — Inspect and Fix the Project

1. Read `ProjectSettings/ProjectVersion.txt`.
2. Inspect `Assets/MemeArena/`, including all Editor scripts, runtime scripts, materials, and textures.
3. Read `README_VI.md` and `CODEX_PROMPT.md` if present.
4. Inspect the existing fighting scenes, character controllers, camera system, combat logic, and URP configuration.
5. Identify compilation errors, missing references, deprecated Unity APIs, incorrect shaders, and asset import issues.
6. Fix issues within the MemeArena package first.
7. Preserve existing game systems and public APIs.

If a Unity Editor executable is available, run an actual Unity compilation and report the results. Otherwise, clearly report what could not be verified.

## PHASE 2 — Complete the Procedural Roman Colosseum

Build a reusable Unity Editor tool accessible from:

`Tools > Meme Arena > Generate Test Arena`

The generated arena must include:

- A 3D Roman Colosseum with curved spectator tiers.
- Multiple rows of stone seating.
- Roman arches, pillars, railings, and decorative walls.
- A large Doge Emperor statue or replaceable placeholder.
- Red and gold banners.
- A central 3D fighting floor.
- Torches, decorative objects, and optional crowd props.
- Proper material assignment and mesh normals.
- Configurable stage dimensions in the Inspector.

Generate a prefab under:

`Assets/MemeArena/Generated/`

Do not overwrite an existing scene or prefab without permission.

## PHASE 3 — Build the Hybrid 2.5D Environment

Use the following architecture:

- **Foreground:** Real 3D combat floor and important stage decorations.
- **Midground:** Real 3D Colosseum architecture and nearby spectators.
- **Crowd layer:** Animated meme characters using lightweight 2D sprites or 3D billboards.
- **Far background:** Static scenic textures and distant crowd layers.
- **Sky:** Skybox or world-space background image.

Do not use GIF as the primary background.

Use SpriteRenderer, Sprite Atlas, shader animation, and world-space background planes where appropriate.

The result must support the existing fighting camera without changing combat movement.

## PHASE 4 — Meme Spectator System

Create or complete these systems:

- `MemeCrowdSpawner`
- `MemeCrowdFan`
- `MemeCrowdReactionManager`
- `MemeCrowdGroup`
- `MemeCrowdSettings`

Support meme spectator types including:

- Pepe
- Doge
- Wojak
- Popcat
- Ape
- Other configurable meme sprites or prefabs

Each spectator can have:

- Idle animation
- Jump animation
- Cheering animation
- Booing animation
- Victory reaction
- Defeat reaction

Allow randomized movement timing, rotation, scale, and animation phase.

Support multiple cheering groups.

Crowd animations should not be synchronized unnaturally.

Use the included placeholder assets until production-quality sprites or models are supplied. Do not invent missing production assets or claim they exist.

## PHASE 5 — Connect Crowd Reactions to Combat

Inspect my existing combat event architecture.

Connect spectators to these gameplay events where matching events exist:

- Fighter hit
- Combo attack
- Critical hit
- Low health
- Knockout
- Round start
- Round victory

Examples:

When Fighter A performs a powerful combo, supporters of Fighter A cheer.

When Fighter B loses, supporters of Fighter B react with sadness or disappointment.

When a KO occurs, trigger a large crowd reaction, camera-safe confetti effects, and optional celebratory signage.

**Do not rewrite combat logic. Use event adapters or subscriptions.**

If an event does not exist, suggest the smallest safe integration point before changing gameplay scripts.

## PHASE 6 — Comic-Style URP Visuals

Create a stylized look inspired by colorful modern anime/comic fighting games.

Use:

- URP-compatible materials
- Toon or cel-shaded lighting
- Strong but controlled outlines
- Vibrant color palettes
- Warm arena lighting
- Soft shadows
- Ambient Occlusion where supported
- Subtle Bloom
- Optional color grading
- Stylized dust, fire, and confetti

The two playable characters must remain visually readable against the background.

Avoid excessive bloom, visual clutter, and overexposed lighting.

Do not change render pipelines or install new packages without permission.

## PHASE 7 — Optimize for 60 FPS

Optimize the crowd and environment for a stable 60 FPS target.

Use:

- Static geometry batching when suitable
- GPU instancing when suitable
- Texture atlases
- Shared materials
- Distance-based crowd detail
- Limited MonoBehaviour Update calls
- Reusable meshes
- Object pooling for transient VFX

Keep the stage compatible with the existing camera and gameplay.

Measure performance using Unity profiling tools when available. Do not claim a verified 60 FPS without profiling a running build.

## PHASE 8 — Editor Tool and Inspector Controls

Make the system configurable without code changes.

Expose:

- Arena width and depth
- Colosseum radius
- Number of seating tiers
- Crowd density
- Animated crowd count
- Crowd reaction intensity
- Background texture references
- Arena material references
- Banner and statue prefab references
- Lighting presets
- VFX enable/disable settings
- Quality/performance presets

Provide buttons to:

1. Generate Arena
2. Regenerate Arena
3. Populate Meme Crowd
4. Preview Crowd Reactions
5. Validate Generated Stage

Preserve user-assigned assets when regenerating. Require confirmation before destructive regeneration.

## PHASE 9 — Validation

Verify, where technically possible:

- C# compilation succeeds.
- Editor menu works.
- Prefab generation works.
- Generated assets have valid references.
- URP materials render correctly.
- No unintended colliders interfere with fighters.
- The camera can see the arena.
- Crowd reaction methods work.
- Stage generation does not overwrite gameplay assets.
- No missing scripts or null references occur during the tested gameplay path.

Run tests using Unity Editor when accessible. Report any tests that could not be run.

## FINAL DELIVERABLES

Provide:

1. A functional Meme Colosseum stage prefab.
2. Reusable and configurable crowd systems.
3. Clean, organized C# code.
4. A working Unity Editor generation tool.
5. URP-compatible materials and lighting setup.
6. Example gameplay event adapters.
7. Clear setup documentation.
8. A list of remaining art assets required.
9. A validation report.
10. A summary of all files created or modified.

## WORKFLOW RULES

Start by inspecting the existing project.

Then provide a concise implementation plan.

Work in small phases, completing and validating each phase before continuing.

You may create or modify files within `Assets/MemeArena/`.

Do not modify existing combat, player, camera, UI, or production scene files without first explaining the required change and receiving my approval.

Never delete existing assets without permission.

Prioritize a working arena and correct camera composition before visual polish.

**START NOW: Inspect the project, repair the imported MemeArena package, and implement Phase 1 and Phase 2 first. Then continue through the remaining phases once the foundation is verified.**