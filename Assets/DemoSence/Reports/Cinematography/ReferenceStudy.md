# Whip camera reference and showcase shot directions

Study date: 2026-10-02. Repository: Zero-Space. This is a source study and shot-direction brief, not a claim that final cameras have been rendered or approved. Runtime framing validation and visual review are separate implementation outputs.

## Measured reference

`Assets/Frank_Slash_Pack/Demo/Frank_Whip_Demo.unity` has one perspective Main Camera. It is a child of the imported `SK_Frank_Whip_WithCam` transform `root/pelvis/Main_Camera`. The camera's local position is `(0, 0.24, 0)` and local rotation is 180 degrees around Y; scale is one. The rig scene root is `(0.018, 0.000000119, -1.0317508)` with identity rotation. The receiver root is `(-0.002, 0, 0.288)` with 180-degree yaw.

Camera settings: vertical FOV **31 degrees**, perspective, full viewport, near/far **0.3/1000**, lens shift zero, stored focal length 50 and sensor 36x24, HDR and MSAA enabled, skybox clear, occlusion culling enabled. The active projection is FOV-driven, so the stored physical lens fields should not be interpreted as an additional animated 50 mm lens. The camera has an AudioListener and a child title TextMesh. There is no camera MonoBehaviour, Cinemachine rig, Timeline, look-at constraint, spring follow, runtime damping, postprocessing volume, or dedicated depth-of-field component in the scene.

The actor uses `Controller/Ctrl_Whip_Motion_Sample.controller`. Its action camera tracks are imported from `Anim_FBX_Pack/08_Frank_Whip_Combo_Skill_All.FBX`, GUID `72b53cbc8461bff46aadf52e39c95364`. The model mesh FBX also contains named clips, but it is not the source of the active controller's action clips. The idle comes from `01_Frank_Whip_Equip_All.FBX`.

The camera bone is directly animated in translation, rotation and effectively constant scale. The binary FBX contains **2,233 keys per axis** across a **37.2 second** master take, spaced at **60 Hz**. All three position and rotation axes are keyed. Follow motion comes from inheriting the animated pelvis and root, while local camera tracks compose the shot around that motion. This is animation-synchronized authored motion, not an automatic follower. There is no animated FOV curve attached to the Unity Camera.

Raw camera-bone distance from its pelvis parent, after converting FBX centimetres to metres (excludes the Camera child's 0.24 m offset):

| Imported action | Source frames at 60 Hz | Duration | Camera-bone radius min/max | Start/end radius |
|---|---:|---:|---:|---:|
| Critical attack | 100–290 | 3.1667 s | 3.999 / 13.189 m | 4.773 / 13.189 m |
| Combo 1 | 400–560 | 2.6667 s | 3.206 / 7.000 m | 4.567 / 7.000 m |
| Combo 2 | 630–790 | 2.6667 s | 3.721 / 5.207 m | 4.322 / 3.989 m |
| Combo 3 | 880–1080 | 3.3333 s | 3.888 / 6.648 m | 4.571 / 5.707 m |
| Combo 4 | 1180–1331 | 2.5167 s | 2.702 / 5.821 m | 4.451 / 4.242 m |
| Combo 5 | 1420–1660 | 4.0000 s | 3.637 / 6.200 m | 4.604 / 4.398 m |
| Skill 1 | 1770–1920 | 2.5000 s | 4.689 / 8.805 m | 6.393 / 8.805 m |
| Skill 2 | 2000–2190 | 3.1667 s | 3.816 / 10.418 m | 5.356 / 10.418 m |

These values demonstrate distinct dolly envelopes, rather than one orbit replayed for every animation. Raw local FBX rotation is relative to the moving, rotating pelvis and includes multi-turn Euler values; it must not be copied directly as a world-space camera path for differently proportioned characters.

The active controller defaults to Equip Idle, then goes to Critical Attack, Combo 1 and Combo 5. Equip Idle is 360–630 source frames (4.5 s). Idle to Critical uses normalized exit time **0.8831209**, fixed blend **0.5259557 s**, destination offset **0.019931095**. Critical to Combo 1 and Combo 1 to Combo 5 use exit time **1**, zero offset and **zero-duration transitions**. Combo 5 has no outgoing transition. Receiver idle to hit has exit **0.887796**, blend **0.50491786 s**, offset **0.0066435174**. These are Animator transitions, not separately authored camera blends.

## Adaptation rules

Use the reference's fixed-lens character, authored dolly variation, and exact synchronization. Do not copy its pelvis parenting literally: Mankey and Pepe have different silhouettes, the UI consumes horizontal space, and paired actions require the receiver to remain visible. Track a smoothed pair/action center with per-entry anticipation and hand/weapon emphasis. Fit visible character and equipped-weapon bounds, excluding hidden calibration sources. Keep the ground contact region in view during throws and landings.

Keep the usual vertical FOV near the reference's 31 degrees, using restrained widening for large leaps or swings. Get most shot-size variation from camera distance. Use a readable action-line angle; avoid head-on foreshortening for spear thrusts and avoid a fast axis crossing during contact. Distinct camera designs may share smooth interpolation and framing guards while differing in their authored azimuth, elevation, dolly, aim weight, and timing curves.

Use action time for camera evaluation so pause, scrub, speed changes, and role swap remain synchronized. Anticipate the full next action envelope before a throw or launch. Blend manual library changes and loop restarts; the reference's zero-duration action transitions are not a useful behavior to copy into an interactive showcase. Avoid fake impact by aggressive roll, repeated shake, or FOV pumping. A short restrained positional accent may support a measured impact, but action visibility is the priority.

## Individual shot direction brief

47 selectable motion entries exist: 8 Frank, 18 Vol10, 17 gun/sword, and 4 GreatSword execution entries. Both attacker roles need review for every shot. The direction below is a distinct design brief per entry. Frank observations were checked against existing five-frame source review sequences in `Reports/Frames`; bare-hand direction derives from the named paired action; combo timing derives from `ComboLibrary.asset` and `Reports/InsaneCombos/generation.csv`/`jumps.txt`. These are starting directions to refine using newly sampled poses, not a substitute for visual review of final output.

### Frank weapons

| Entry | Individual direction and readability requirement |
|---|---|
| 2-Handed | Low three-quarter medium-wide; ease upward/back for the overhead axe silhouette, descend aim toward grounded receiver after the hit, settle with the full raised weapon visible. |
| Assassin | Closer offset side view; short lateral slide reveals blade/contact separation; modest upward anticipation before the late hop, then follow the receiver's collapse without losing the attacker. |
| Dual | Ascending three-quarter tracking shot; widen before the clearly airborne middle phase, gently arc around the suspended pair, lower aim toward the landing and settle on both swords and receiver. |
| Greatsword | Wide off-axis blade-side view; slight inward anticipation, then a deliberate pull-back along the action direction as the receiver travels far away; preserve the complete blade and distant receiver. |
| Katana | Tighter clean side view with restrained lateral truck; keep the camera on one side of the combat axis as the attacker passes across the receiver; end with both the finishing stance and fall visible. |
| Spear | Oblique profile showing shaft length and extension; ease out before the leap/rotation, track the spear tip enough to retain the full arc, return to a grounded medium-wide recovery. |
| Warrior | Low shield-side three-quarter establishment, measured rising arc for the jumping strike; settle downward after impact while ensuring the shield never hides the receiver's reaction. |
| Warrior alt hit | Opposite oblique emphasis on the receiver, slightly higher coverage than the standard Warrior shot; hold the reaction silhouette through the alternative fall, then drift toward the attacker finish. |

### Vol10 bare hands

| Entry | Individual direction and readability requirement |
|---|---|
| Idle | Calm medium two-shot; very slow shallow lateral drift with breathing room, no impact pulse or arbitrary zoom. |
| Shoulder throw / SEOI | Side-to-front shallow arc revealing the grip; rise during shoulder loading, pull back as receiver rotates over, lower aim for floor contact. |
| Brainbuster / BRAIN | Low three-quarter start emphasizing vertical lift; widen and elevate before inversion, keep both feet and head visible, settle after the vertical drop. |
| Atemi 1 / CmnAtemi | Compact near-profile push toward hand-to-torso contact; minimal azimuth change during the short strike, slight receiver-weighted recovery. |
| Atemi 2 / CmnAtemi2 | Opposite three-quarter lateral slide, slightly higher than Atemi 1; expose arm and chest separation, arrest movement at contact then release outward. |
| Atemi 3 / CmnAtemi3 | Lower oblique medium shot with a short inward arc timed to the wind-up; retain full recoil path, use longer settling than Atemi 1/2. |
| Fisherman suplex / FISH | Open-side medium-wide showing the leg hook; crane back/up for the bridge, end lower with both shoulders and landing area in frame. |
| Flank throw / FLANK | Lateral tracking on the throw's open side; lead the receiver across the frame, widen only during release, retain floor and follow-through. |
| Hold / HOLD | Restrained close-medium three-quarter orbit of small amplitude; reveal hand placement and both faces without letting one torso obscure the other. |
| Hold + lariat / HOLD_LALI | Begin tighter on restraint, lead the separation with a lateral dolly, widen just before the lariat and shift aim toward recoil. |
| Powerbomb / JPBOM | Low frontal-oblique establishment; high headroom during lift, controlled crane/pullback at the apex, lower aim as the receiver lands. |
| Dragon screw / LC_DSCREW | Higher oblique angle exposing the captured leg and rotational footwork; shallow rotation following the twist, settle with both bodies and feet readable. |
| Throw escape / NAGE_ESC | Medium side coverage emphasizing the space opening between bodies; restrained outward dolly through the break, neutral balanced finish. |
| Neck breaker / NECK_BREAK | Open-side shoulder-height view showing neck/arm relationship; small lateral lead into the fall, lower and widen to show the completed landing. |
| Four-way throw / SIHO | Elevated three-quarter angle to expose arm control and turn; gradual quarter-arc during preparation, hold camera direction through the release. |
| Windmill throw / B_FUSHA | Wider opposite-side orbit matching the circular preparation without matching its angular speed; keep the rotating body in silhouette, ease outward for release. |
| German suplex / GS1 | Side profile opening to a slight rear three-quarter; crane backward during the arch, keep the receiver's head and attacker's planted feet visible at landing. |
| Giant swing / GSWING | Wide elevated shot around the spin center; slow counter-orbit distinct from the fast body rotation, anticipate full radius, then lead release with a deliberate pullout. |

### Gun and sword combinations

Impact/ground times below are seconds in each selectable clip; they are measured asset metadata. Full-combo `impactTime=0` is a placeholder, not an actual opening impact. Their important trajectory times come from the existing motion report and should be refined from pose sampling.

| Entry | Timing | Individual direction |
|---|---|---|
| Combo 1 full | Receiver surges at 0.88–0.93 and 1.65–1.67; ground 2.80 | Shallow lateral tracking progressing toward a wider finish; anticipate forward displacement while retaining both weapon and recoil silhouette. |
| Combo 1 step 1 | Impact .280; ground 1.680 | Near-side compact push into the first contact, brief compositional hold, slow reaction-weighted pullout. |
| Combo 1 step 2 | Impact .380; ground 1.780 | Opposite three-quarter lateral glide, slightly higher than step 1 to reveal the second beat and hand separation. |
| Combo 1 step 3 | Impact .413; ground 1.813 | Lower angle with longer forward tracking; widen in anticipation of the receiver surge around .73, settle after the finisher. |
| Combo 2 full | Launch around 1.85–1.93; descent 3.9–4.08; ground 4.967 | Long tracking crane: modest opening, expand vertically before launch, maintain airborne pair, controlled descent to ground rather than chasing the pelvis. |
| Combo 2 step 1 | Impact .163; ground 1.563 | Ready medium framing with a short pre-impact inward move; stable impact composition because the beat arrives quickly. |
| Combo 2 step 2 | Impact .413; ground 1.813 | Slightly raised three-quarter arc revealing the longer preparation; release into a wider recoil frame. |
| Combo 2 step 3 | Impact .347; ground 1.747 | Wide oblique distance shot because initial pair spacing is about 3.5 m; track down the line while preserving lateral separation. |
| Combo 2 step 4 | Impact .347; ground 1.747 | Mid-height side truck, compact dolly-in after preparation, reaction-following settle with more lateral lead than step 3. |
| Combo 2 step 5 | Impact .247; ground 1.647 | Low opposite-side push emphasizing close contact; subdued orbit and strong separation of pistol/sword from the target. |
| Combo 2 step 6 | Impact .147; ground 1.547 | Immediate clear medium-wide opening; brief contact emphasis followed by the longest outward settling path in this sequence. |
| Combo 3 full | Launch .42–.48; descent 2.95–3.02; ground 3.90 | Anticipatory vertical pullout almost immediately, shallow arc through airborne section, downward tracking with a ground-revealing finish. |
| Combo 3 step 1 | Impact .380; ground 1.780 | Wide side-biased opener for 3.64 m separation; slight rising push preserves the launch corridor. |
| Combo 3 step 2 | Impact .313; ground 1.713 | Closer blade-side three-quarter drift, short travel and restrained elevation to contrast the wide first step. |
| Combo 3 step 3 | Impact .380; ground 1.780 | Slight high angle with an inward diagonal truck, keeping weapon-hand and reaction silhouettes separate at close range. |
| Combo 3 step 4 | Impact .080; ground 1.480 | Pre-composed contact shot from frame zero; no late rushing camera move, then measured outward release toward the receiver. |
| Combo 3 step 5 | Impact .713; ground 2.113 | Long anticipation dolly with low-to-high progression, peak framing at the later impact, then ease downward for the terminal fall. |

### GreatSword executions

| Entry | Individual direction and readability requirement |
|---|---|
| Ambush | Tighter open-side oblique reveal, slow push through approach, widen before the blade arc; retain clear attacker/receiver silhouettes throughout the surprise strike. |
| Execution 1 | Wider low three-quarter start for the 4 m setup; track approach with a controlled inward dolly, rise slightly around the decisive action and settle toward the fallen receiver. |
| Execution 2 | Higher opposite-side view for the 3.5 m pairing; shallow descending arc toward contact, maintain ground visibility and complete weapon path, softer pullout after the finish. |
| Execution 3 | Mid-height lateral tracking for the 2.8 m setup; deliberate wind-up push, a small directionally consistent orbit through follow-through, wider balanced final tableau. |

## Review requirements for implementation

Review every entry with both attacker roles and at least anticipation, contact, apex when present, follow-through, and end frames. Sample continuously for finite transforms, near-plane clearance, visible mesh/weapon bounds, and camera speed/acceleration. Test pause, arbitrary scrubbing, speed changes, loop boundaries, rapid library changes, role swap, and narrow game-view aspect ratios. Distinguish geometric containment from artistic approval: projected bounds passing a test does not establish strong composition, clear silhouettes, or polished cinematography.
