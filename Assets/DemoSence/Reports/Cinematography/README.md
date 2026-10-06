# Directed camera showcase

Saved in `Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity` in Zero-Space. Open the scene, enter Play mode, and use **Camera: Cinematic** (the default). The same button cycles through the three manual inspection views. Role swapping, speed, pause, restart, scrubbing and weapon selection remain available.

## Coverage and design

The scene has 47 individually authored motion plans, 361 timed direction beats, and 94 independently baked character-role takes. The library contains 7,966 samples at approximately 30 Hz with continuous Hermite interpolation. All eight Frank entries, 18 bare-hand entries, 17 gun/sword entries, and four executions have distinct direction curves and written shot rationale.

The reference study is in [ReferenceStudy.md](ReferenceStudy.md). The Whip demo uses a 31-degree perspective lens and an animated camera bone under the pelvis. Its movement is synchronized with the authored attacks. This adaptation uses a restrained 30.5–37 degree lens range, independently directed angle/elevation curves, anticipated pair and weapon framing, and smooth distance envelopes. The final angles retain floor coverage; the enlarged stage avoids revealing its edges.

Long knockbacks and large airborne separation deliberately use wider views to keep both performers visible. Their full-pair coverage trades close facial detail for readable travel and complete silhouettes. Manual views remain useful for close pose inspection.

## Playback behavior

The director evaluates after both characters. Camera tracks use animation time, including speed changes and deterministic arbitrary seeks. Selections and loop restarts match the incoming action center while interpolating viewing direction, distance and FOV with a quintic blend. The incoming framing envelope is retained during the blend, including compensation for lens width, so a wider new attack does not force an abrupt safety pull-back. The existing tester controls source-pose resets; the camera does not blend character animations.

Projection uses the actual content viewport. Narrow windows scale the pre-smoothed shot distance instead of reacting to each limb. A final visible-mesh and weapon guard retains a minimum five percent border. Baked meshes explicitly compensate transform scale. The exact-endpoint wrap of looping execution clips is clamped just before the loop boundary so paused endpoints hold the recovery pose.

## Verification and visual review

Unity 6000.5.9f1 compiled the runtime/editor scripts and loaded the saved scene and all 94 take references. [Validation.md](Validation.md) and [Validation.csv](Validation.csv) record 26,842 camera samples across three viewport aspects, including 282 repeat-seek checks and selection/loop blends. All framing, finite-value, depth and height checks passed. The six overview phases produced 564 captures; every authored beat produced 722 additional captures.

Mankey: all 47 entries were inspected individually in the overview pass and in the final authored-beat sheets. Full character and weapon coverage was retained in the reviewed frames. Frank Dual was raised through the aerial apex; Frank Greatsword and Execution1 were turned toward a view along the travel direction for the knockback; Combo2 full was raised through the receiver launch. Atemi3 has a 40-42 degree grip view and a separate Pepe-attacker yaw curve to handle the different silhouettes. The Atemi3, Flank, Neckbreaker, Windmill and German throw views were adjusted to expose the contact silhouettes. Detailed Pepe review coverage and remaining source-motion limitations are recorded in [ShotDirections.md](ShotDirections.md).

The rendered sheets assess composition at the labeled times. Continuous projection and interpolation were checked numerically; the reports distinguish those checks from subjective cinematography judgments.

## Review sheets

| Entries | Mankey overview | Mankey action beats | Pepe overview | Pepe action beats |
|---|---|---|---|---|
| frank/0 to frank/5 | [View](ReviewSheets~/overview-mankey-0.jpg) | [View](ReviewSheets~/beats-mankey-0.jpg) | [View](ReviewSheets~/overview-pepe-0.jpg) | [View](ReviewSheets~/beats-pepe-0.jpg) |
| frank/6 to vol10/3 | [View](ReviewSheets~/overview-mankey-1.jpg) | [View](ReviewSheets~/beats-mankey-1.jpg) | [View](ReviewSheets~/overview-pepe-1.jpg) | [View](ReviewSheets~/beats-pepe-1.jpg) |
| vol10/4 to vol10/9 | [View](ReviewSheets~/overview-mankey-2.jpg) | [View](ReviewSheets~/beats-mankey-2.jpg) | [View](ReviewSheets~/overview-pepe-2.jpg) | [View](ReviewSheets~/beats-pepe-2.jpg) |
| vol10/10 to vol10/15 | [View](ReviewSheets~/overview-mankey-3.jpg) | [View](ReviewSheets~/beats-mankey-3.jpg) | [View](ReviewSheets~/overview-pepe-3.jpg) | [View](ReviewSheets~/beats-pepe-3.jpg) |
| vol10/16 to combo/3 | [View](ReviewSheets~/overview-mankey-4.jpg) | [View](ReviewSheets~/beats-mankey-4.jpg) | [View](ReviewSheets~/overview-pepe-4.jpg) | [View](ReviewSheets~/beats-pepe-4.jpg) |
| combo/4 to combo/9 | [View](ReviewSheets~/overview-mankey-5.jpg) | [View](ReviewSheets~/beats-mankey-5.jpg) | [View](ReviewSheets~/overview-pepe-5.jpg) | [View](ReviewSheets~/beats-pepe-5.jpg) |
| combo/10 to combo/15 | [View](ReviewSheets~/overview-mankey-6.jpg) | [View](ReviewSheets~/beats-mankey-6.jpg) | [View](ReviewSheets~/overview-pepe-6.jpg) | [View](ReviewSheets~/beats-pepe-6.jpg) |
| combo/16 to execution/3 | [View](ReviewSheets~/overview-mankey-7.jpg) | [View](ReviewSheets~/beats-mankey-7.jpg) | [View](ReviewSheets~/overview-pepe-7.jpg) | [View](ReviewSheets~/beats-pepe-7.jpg) |

## Rebuilding and reviewing

Edit individual beat arrays and direction notes in `Assets/DemoSence/Editor/FrankCameraDirections.json`. Use **Tools > Frank Retarget > Cameras > Bake all directed camera takes** to rebuild the linked library, **Validate and capture all cinematic shots** to check the saved scene, and **Capture authored action beats** to capture exact beat times. Editor entry points are `CameraBuild`, `CameraValidate` and `CameraCaptureBeats`; `CameraBuildAndValidate` runs all three in batch. The survey uses preview scenes and does not replace the open editing scene. Raw review PNGs and their time/FOV manifests are written to `/tmp/frank-camera-review`; the contact sheets here are retained in a Unity-ignored folder.

Direction source SHA-256: `28599be65ba5e98ccd0c499666a26f73ffc01a5c44488e4db0105787a9041423`.

## Existing character limitation

The captures show Mankey eye and mouth meshes separating from the head in several poses, including Atemi3. This is an existing character or retarget-rig issue, not a camera projection failure; it remains visible and limits the overall presentation. The camera changes do not repair that rig.
