# Samurai Execution01 study checkpoint

Execution01 has been captured with an armed attacker and unarmed victim on
both fighter assignments and both lane directions. `Execution01` contains
the original placement; `Execution01Grounded` applies the new grounding asset.
Each completed capture includes geometry measurements and timestamped images.

Grounding bake and independent validation passed in Unity 6000.5.9f1:
4 pairs, 8 actor cases, 9160 clearance measurements, zero violations and
minimum clearance 0.00956505 m. See `Execution01GroundingValidation.txt`.
The maximum applied lift is 0.237699851 m. This verifies sampled body clearance,
not foot planting, accepted contacts, recovery or gameplay integration.

The probe measures 111 blade triangles selected from the native BladeR mesh.
The mesh exports and projection image document the boundary excluding the
guard, grip and sheath. Gaps are unsigned triangle surface distances; zero
does not distinguish touching from intersecting geometry.

At the original 1.70 m spacing, the grounded Mankey-to-Pepe second swing
misses by about 0.0506 m. The completed eight-case spacing study now measures
both directions at 1.70, 1.66, 1.64 and 1.62 m. Gaps at the second swing are
approximately 0.0506, 0.0132, 0 and 0 m respectively. At 1.64 and 1.62 m,
only the sample at 2.0333333 s touches within 1 mm; this narrow contact needs
closer timing review before acceptance. The reviewed 1.64 m positive-lane
sheet keeps the initial thrust, kneeling receiver and later horizontal cut.
No mid-action translation, source retiming or enlarged hitbox was introduced.
See `SpacingCandidates` for trajectories and timestamped sheets.

These spacing results cover Mankey attacking Pepe only. The existing grounded
Pepe-to-Mankey setup already reaches both candidate contacts at 1.70 m.
No Samurai actions are registered by these tools, and damage, presentation,
recovery and final contact timings remain unaccepted.

Blade presentation configuration now passes isolated Unity validation, including
persistent mesh serialization and 10,000 warmed predicate passes with zero
allocated bytes. The native blade calibration and both sheath exclusions are
saved into BattleScene. Selective-save validation confirms that unrelated saved
fields are preserved. See `Presentation/Report.txt`, `SelectiveSaveValidation.txt`
and `Installation.txt`. Actual gameplay trail appearance and effect directions
still require validation.

Reproduce in Edit Mode using these `CombatExpansionSamuraiStudy` methods:

1. `InspectBladeMesh()`
2. `CaptureExecution01BladeContacts()`
3. `BakeExecution01Grounding()`
4. `ValidateExecution01Grounding()`
5. `CaptureGroundedExecution01BladeContacts()`

The generic capture status text lists further integration work. For the
grounded variant, the separate grounding validation above is the completed
clearance check; full choreography and gameplay acceptance remain pending.
