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

The grounded Mankey-to-Pepe second swing still misses by about 0.0506 m at
the closest sampled point in its candidate window. Contact adaptation remains
pending. No Samurai actions are registered by these tools, and no damage or
presentation timings have been accepted.

Reproduce in Edit Mode using these `CombatExpansionSamuraiStudy` methods:

1. `InspectBladeMesh()`
2. `CaptureExecution01BladeContacts()`
3. `BakeExecution01Grounding()`
4. `ValidateExecution01Grounding()`
5. `CaptureGroundedExecution01BladeContacts()`

The generic capture status text lists further integration work. For the
grounded variant, the separate grounding validation above is the completed
clearance check; full choreography and gameplay acceptance remain pending.
