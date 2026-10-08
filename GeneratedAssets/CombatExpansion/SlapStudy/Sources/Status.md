# SlapFace source preparation status

All four project-owned native imports and eight unarmed fighter drivers have
been prepared and validated in Unity 6000.5.9f1. Source capture completed for
all four clips on both Mankey and Pepe: 4232 sampled poses and 56 timestamped
sheets. See `NativeImports.json`, `Study.json` and `index.html`.

The original FBXs referenced missing Avatar GUID
`54d24dcf9716c0c40821dd95a9e62844`. `SourceDiagnostics.json` records that original
failure. The repair copies each source under
`Assets/CombatExpansion/SlapStudy/NativeSources`, preserving the original FBX
bytes and creating a valid Avatar from its complete native human mapping.
Original source files and import settings remain unchanged. Original GUID and
clip IDs remain the provenance keys; adapted identities and source hashes are
recorded explicitly. Reuse validates the prepared copies against that record.

Reproduction order: `CombatExpansionSlapStudy.PrepareNativeImports()`,
`PrepareDrivers()`, then `CaptureSources()` in Edit Mode.

The 24-case pair capture now passes on both giver avatars across two headings
and three spacings. The reviewed frontal setup uses receiver yaw 180 degrees
and 0.8 m spacing; 1.0 and 1.2 m place the striking hand too far away.
Sequence1 uses A as giver/B as receiver; Sequence2 reverses those roles.
See `../PairCandidates/Study.json` and its timestamped pose sheets.

`CaptureFaceContacts()` completed all eight avatar/direction cases using
moving skinned right-hand and head triangles. Each sequence contains one
slap; separated near-zero samples during a single hand sweep are not extra
strikes. This raw capture also exposed floor penetration, so its contact
positions are not the final authoring data.

`BakeFaceGrounding()` and `ValidateFaceGrounding()` now pass for both sequences.
The independent 361 Hz validation covers both assignments and directions:
50,760 visible-body clearance measurements, zero violations, and minimum
clearance 0.009902719 m. Saved maximum lifts are 0.12243624 m for Sequence1
and 0.14226146 m for Sequence2. See `../Grounding` for individual reports.
This verifies sampled clearance, not foot planting or complete choreography.

The grounded face-contact capture completed all eight cases. The reviewed
contact samples touch the moving face surface on both lane directions; see
`../FaceContactsGrounded/MotionReview.md` for selected per-avatar times.
Both actions are now registered for explicit selection in BattleScene, with
four per-avatar action definitions, authored feedback profiles and optional
standing recovery. The nonlethal Play Mode suite now passes 48 lifecycle cases
covering 56 accepted plays and eight range rejections, across both avatars and
lane directions. It checks contact feedback, recovery, pause, interruption,
reset, disable, repeated use and cleanup. See `../PlayMode/Report.txt` and
`../PlayMode/Cases.csv` for the complete scope.

The initial impact-position assertion omitted the existing 15 mm camera-facing
effect offset; the corrected assertion retains its 2 mm tolerance. Subsequent
validation exposed a 68 mm recovery hand snap: restoring controller bone lengths
immediately discarded the native pose's bounded reach offsets. Standing recovery
now blends those local positions with the pose; the complete suite measured a
maximum recovery entry displacement of 0.000001 m. Ground get-up behavior retains
its existing position handling.

Lethal visual treatment remains unimplemented and was excluded from the suite.
Final gameplay camera review and Slap-specific audio tuning remain pending.
These registrations remain part of an unfinished combat expansion checkpoint.
