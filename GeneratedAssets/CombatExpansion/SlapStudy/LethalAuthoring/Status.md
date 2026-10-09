# Provisional Slap lethal authoring

Both project-owned receiver candidates are now baked and saved under
`Assets/CombatExpansion/SlapStudy/LethalCandidates`. They are not yet assigned
to gameplay. `BakeProvenance.txt` records the successful dense validation and
the unchanged-source guard.

Native animation-stream controls, verified finger bindings, world body encoding
and adaptive curve subdivision preserve the original prefix and retimed fall.
The largest measured bone errors were 0.000489160 m for Sequence1 and
0.000489642 m for Sequence2, below the unchanged 0.002 m limit. The original
0.5-degree rotation tolerance, held-pose and reverse-replay checks also passed.
Sampling includes the 240 Hz grid, an independent offset grid and export knots.

The authored blend is checked against the native-stream controls it interpolates.
Reconstructed `HumanPose` controls are retained as a diagnostic: their nonlinear
bone-to-control conversion does not preserve a linear blend. Root/body checks
and native bone equivalence outside the authored transition remain enforced.

Earlier failed encoding and blend diagnostics are archived by the authoring tool.
The successful export does not establish visual acceptance on the playable rigs.
Eight raw runtime previews were captured across both sequences, avatar
assignments and lane directions; source and candidate guards passed. The reviewed
frames retain the slap and flow into a collapse, but raw receiver penetration
reaches 0.150 m on Pepe and 0.236 m on Mankey. See ../LethalCandidates/Raw.
Grounding, final visual review, conditional lethal assignment, landing feedback
and lifecycle acceptance remain required. Optional variant infrastructure has
passed its contract fixture but is unassigned. Reproducible full source curve/import
dumps remain local diagnostics.
