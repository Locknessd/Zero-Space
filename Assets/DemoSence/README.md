# Mankey + Pepe — animation and weapon tester

Open `Frank_Damages_Mankey_Pepe.unity` and press Play.

The scene contains **two persistent visible characters**, Mankey and Pepe. The old rows of character pairs have been removed. Both characters can perform every attack and reaction.

## Controls

- **Library tabs:** choose **Frank weapons**, **Vol10 · bare hands**, or **Gun + sword · combos**. All three libraries use the same two characters. Weapon selections are remembered when switching back.
- **Animation pair:** seven original paired sequences, plus Warrior's alternate reaction.
- **Attacker weapon / receiver weapon:** choose any of the seven weapon sets independently, or None. Changing animation keeps your equipment choices.
- **Swap character roles:** exchange attacker and receiver while retaining the selected animation and equipment.
- **Play / pause, restart, loop, speed, timeline:** inspect a particular frame or watch synchronized playback. Scrubbing pauses playback. Each selection change resets both actors together.
- **Camera / − / +:** change viewing angle and zoom to inspect the hands.

There are 1,024 selectable combinations: eight animation pairs × eight attacker equipment choices × eight receiver equipment choices × two role assignments. Each pair preserves its original spacing. The shorter take holds its final pose until the longer take ends.

## Vol10 bare-hand library

All **35 unique clips** used by `Assets/Asstes/Scenes/Vol10_EF-12 AnimationPreview.unity` are available as **17 active/passive action pairs plus idle**. This includes the extra Giant Swing pair. Select **Vol10 · bare hands** and choose a move; both characters are always unarmed in this library. The original eight Frank animation choices and all weapon combinations remain available in the other tab.

The added actions are Shoulder throw (SEOI), Brainbuster (BRAIN), Atemi 1–3 (CmnAtemi, CmnAtemi2, CmnAtemi3), Fisherman suplex (FISH), Flank throw (FLANK), Hold (HOLD), Hold + lariat (HOLD_LALI), Powerbomb (JPBOM), Dragon screw (LC_DSCREW), Throw escape (NAGE_ESC), Neck breaker (NECK_BREAK), Four-way throw (SIHO), Windmill throw (B_FUSHA), German suplex (GS1), and Giant swing (GSWING). Source IDs are stored in the library asset.

`Vol10/Clips` contains copies of the original Generic transform tracks. `Vol10/Calibration.fbx` supplies a separately imported Humanoid calibration avatar, while `Vol10/Drivers` contains two invisible skeleton profiles. `Vol10/UnarmedLibrary.asset` stores the pairs and their starting placement. Both roles use one common source scale (1.13203) to retain the existing visible character sizes. The original 0.9 m pair spacing scales with the motion; German suplex and Windmill throw retain the original back-facing setup. Clip duration, pelvis travel, jumps, and relative pair timing are preserved. The existing neck, foot, and toe corrections also apply to these motions.

The bare-hand tab has a **Body spacing** slider (default 100%). A new per-pose correction separates the visible torso/head volumes during close holds to accommodate Mankey's larger body. At 0% you can compare the previous spacing. Clearance fades with the take instead of permanently moving the actors apart. Original source tracks, timing, jumps, and the Frank weapon library remain unchanged.

`Vol10/BodySpacing.asset` stores corrections for all 18 pairs in both role assignments. Its torso/head volumes are fitted from the visible models, and an offline path search avoids sudden changes of separation direction during inverted throws. The same correction is obtained when seeking backward or forward. Planted ankles stay at their authored positions; airborne legs and gripping hands follow the adjusted pose. Clearance is limited when an extreme hold would otherwise move a hand beyond its reach. This reduces body overlap; it is not a full mesh collision simulation, so brief limb/surface intersections can remain in wrestling moves.

`Reports/Spacing/validation.txt` checks 3,392 paired frames at 60 Hz: average penetration of the fitted body volumes drops by 98.59%, planted ankle drift stays below 0.001 mm, hand/ankle solve error stays below 3.01 cm, and reverse seeking has no pelvis drift. `Reports/Spacing/play-mode.txt` and PNGs show the updated throws with both characters. The earlier `Reports/Vol10` images show the poses before this spacing adjustment. Rebuild the corrections with **Tools > Frank Retarget > Build body spacing for Vol10** after changing characters or source takes.

`Reports/Vol10/validation.txt` covers 6,784 actor poses at 60 Hz, both role assignments, original source trajectory comparisons, reverse seeking, bare-hand enforcement, and restoration of the original library selections. Maximum source-trajectory error is below 0.005 mm, maximum wrist/ankle contact error is 2.17 cm, and maximum softened neck bend is 43.16 degrees. `Reports/Vol10/play-mode.txt` and screenshots cover live throws on both characters, restarting, speed, looping, end holds, and switching libraries. The original 1,024 weapon combinations were rechecked after integration.

Use **Tools > Frank Retarget > Add or rebuild Vol10 bare-hand library** to regenerate only the added library and append its references to the demo. The full two-character tester rebuild also restores Vol10 when its library asset exists. Original Vol10 assets and the legacy `Sets` assets are verified against pre-change hashes in `Reports/Vol10/source-integrity.json`.

## Gun and sword combos

The **Gun + sword · combos** tab adds every `combo_` attack in `Assets/InsaneGun_Sword_Set/InsaneGun_Sword_Set.unity`: three full combos and all 14 numbered steps (Combo 01: 3 steps; Combo 02: 6; Combo 03: 5). Each group has a **Full combo** button and individual **Step** buttons. Role swap, play/pause, restart, loop, speed, scrubbing, and camera controls continue to work. The attacker carries the pack's original gun in the left hand and sword in the right. The receiver is unarmed.

The full combos use copies of the pack's authored attack/reaction pairs and original pair spacing. Each standalone step gets its own generated reaction asset. Generation matches the step's limb poses to its full combo, estimates impact from weapon-hand movement, blends into the matching reaction section, then blends into that combo's falling/ground pose. Standalone launches are bounded and the root transition is smoothed to avoid inheriting an already-airborne receiver or snapping between sections. The shorter attack holds its last pose while the receiver completes the knockdown. Generated clips include a final ground hold, and both characters receive separately sampled mesh-based floor clearance so the larger model's head/body does not sink into the floor.

`InsaneCombos/Clips` contains 17 copied attacks and three original hit reactions; `InsaneCombos/Reactions` contains 14 generated hit clips. `InsaneCombos/Drivers` contains the two calibrated, body-free source skeleton profiles and original weapon meshes. `InsaneCombos/ComboLibrary.asset` stores grouping, impact/ground times, placement, and each character's landing-height correction. Source FBXs, controllers, scene, and materials are left unchanged. These derived Generic clips use the calibrated drivers, rather than being standalone Humanoid clips for unrelated rigs.

The generated step reactions are automatically derived choreography; the original pack supplies no individual step reactions. Full combos retain their authored reactions. The existing Vol10 spacing correction remains specific to its wrestling library.

Use **Tools > Frank Retarget > Add gun and sword combos** to regenerate this added library and attach it to the existing demo. The full tester rebuild restores both added libraries if their library assets exist. `Reports/InsaneCombos` contains the step-generation manifest, validation results, live impact/ground screenshots, and source integrity audit.

## GreatSword execution sample

The **GreatSword · executions** tab adds the four synchronized pairs authored by
`Assets/GreatSword_Animset/Scene/Execution_Sample.unity`: Ambush, Execution 1,
Execution 2, and Execution 3. Mankey and Pepe use the existing calibrated
GreatSword source drivers built from the sample's `WM_Master_Unity` hierarchy,
with the original `GreatSword_01` mesh attached to the sample's
`root/ik_hand_root/ik_hand_gun/ik_hand_r` socket. Use **Swap character roles** to
run every pair with Pepe as the attacker. The library references the imported
FBX clips directly; their Generic transform tracks, root travel, socket motion,
and source timing remain uncompressed and unchanged. The source takes stay
Generic; a separate explicitly mapped Humanoid avatar is used only by
`HumanPoseHandler` to transfer the authored WM pose to each visible character.
The four invisible profiles are in `GreatSwordExecution/Drivers`, and their
calibration avatar is `Rigs/WM_Master_Unity_GreatSword_Humanoid.FBX`.
`Reports/GreatSword-validation.txt` records the numeric curve, contact, stretch,
and role-swap audit; `Reports/GreatSwordFrames` contains representative GPU
renders of all four pairs at three timeline positions.

## Assets and playback

`Tester/Mankey.prefab` and `Tester/Pepe.prefab` supply the two visible models. `Tester/Drivers` contains invisible source skeletons and calibration data; these contain no original body/clothing renderers or duplicate visible characters. `Tester/Weapons` contains the independent equipment rigs.

`GreatSwordExecution/Drivers` contains the exact WM source hierarchy used by the
execution takes; it is separate from the Frank weapon drivers so no GreatSword
curve is retargeted through an unrelated skeleton.

The test scene uses one manually evaluated animation graph per actor and a shared clock for synchronized playback, pausing, speed changes, and seeking. `FrankPoseRetarget` transfers the selected source pose to the existing visible model and preserves authored pelvis travel, including airborne motion. Character instances remain unchanged when animations, weapons, or roles switch.

Selecting the original matching attack and weapon preserves its authored weapon animation, including segmented weapon tracks and Katana sheath/blade behavior. Mixed combinations use the selected weapon's calibrated grasp and attach the equipment to the moving hands. Mixing a weapon with a different attack changes the prop, not the authored attack into a newly choreographed move. Two-handed weapons can consequently be swung one-handed in motions authored for one hand.

The earlier reusable full actor prefabs and copied controllers/clips remain in `Sets/Mankey/Hit`, `Sets/Mankey/Being Hit`, `Sets/Pepe/Hit`, and `Sets/Pepe/Being Hit`. These assets are not extra characters in the test scene. The derived Generic clips require their calibrated source skeleton and retarget component; they are not standalone Humanoid clips to place directly on an unrelated Animator.

The source names are counterintuitive: `Damage_Critical_<weapon>` is the attack, and `Damage_Critical_<weapon>_Hit` is the reaction.

## Neck and foot posture correction

`FrankPostureCalibration` derives neck, head, ankle, and toe rotation offsets from the avatars' reference skeletons. This avoids extrapolating the source's out-of-range neck/head muscle values into the visible character and replaces the previous inaccurate ankle offsets. Toe orientation is applied after ankle IK so it follows the authored toe bend without inheriting a second rotation from the corrected foot.

The neck refinement distributes 45% of the head look rotation through the neck and leaves the remaining rotation to the head joint. It uses a soft bend limit and fades the neck contribution near a reversed look direction to prevent quaternion-arc snapping. The authored head direction is preserved. The result is calculated from the current pose, so pauses and reverse scrubbing give the same result without accumulated smoothing lag.

The change is entirely in Unity; the original FBXs and animation tracks were not edited. Pelvis travel, hand contacts, weapon selection, and animation timing remain intact.

`Reports/Posture/validation.txt` and `.csv` cover all 30 character/clip combinations at 60 Hz (11,230 poses), including reverse scrubbing and equipped receivers. Head and foot rotation errors remain below 0.00005 degrees. The refined neck stays below 48 degrees of bend across this set (the previous direct source transfer reached nearly 180 degrees), with no extra snapping beyond the authored torso/head movement and no reverse-seek drift. `Reports/Posture/play-mode.txt` and the four PNGs verify the reported Assassin 5.29 s and Spear 6.64 s poses on both characters in live Play mode.

## Source corrections retained

- Original reactions used Humanoid clips on a Generic FS3 rig. Copied, uncompressed Generic FBX tracks retain the full authored motion.
- Katana's copied calibration avatar explicitly maps Hips to pelvis.
- Warrior's alternate reaction uses the real `Warrior_Hit2` take.
- Palm orientation uses anatomical knuckle directions instead of unrelated FBX bone axes. Grip alignment accounts for each character's hand proportions.
- The supplied scene contains no ParticleSystems, TrailRenderers, AudioSources, or animation events. Its animated weapon meshes are retained.

## Verification and rebuilding

Use **Tools > Frank Retarget > Build two-character combination tester** to regenerate the scene from the existing derived actor assets. This replaces the generated scene; save manual scene changes elsewhere first. **Validate all weapon combinations** evaluates all 1,024 combinations, including reverse seeking, endpoints, role assignment, stable character identity, finite poses, and grip contact. Results are in `Reports/combinations.txt` and `.csv`: all 1,024 combinations passed across 10,240 sampled poses. Grip errors stayed below 0.003 mm.

**Run combination tester in Play mode** checks real playback, pause, seek, equipment changes, roles, alternate Warrior, restart, speed, loop, and final hold. The live result is in `Reports/tester-play-mode.txt`; `tester-ui.png` and `tester-mixed-ui.png` show the interface.

The older `Reports/validation.json`, `.csv`, `play-mode.txt`, and comparison frames document the preceding full-set retargeting checks. They are historical checks of the source retargeting assets, not screenshots of the new tester. The earlier scene-builder and test commands are grouped under Legacy.

## Source preservation audit

The original scene, animation clips, controllers, model files/import settings, and textures match their pre-task SHA-256 hashes. However, Unity saved **31 original material files** during the first derived-model import. Their loaded shaders were already URP in the initial inspection. Exact pre-task material bytes could not be recovered from the available local packages or sibling project, so complete byte-for-byte preservation of original assets is **not** claimed. `Reports/source-integrity.json` lists these files and both hashes; `source-hashes-before.json` contains the full initial manifest. No unrelated older material versions were substituted. The subsequent two-character tester update changed none of the 499 audited original Frank files; see `Reports/tester-source-integrity.json`.

Humanoid pose coordinate handling follows Unity's API documentation:
- https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HumanPoseHandler.GetHumanPose.html
- https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HumanPoseHandler.SetHumanPose.html
