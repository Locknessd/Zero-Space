# Frank demo coverage closure

Scope is the transitive AnimationClip inventory for `Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity`, keyed by
stable GUID + local file ID. The current `SourceInventory.json` yields **126 rows and 126 unique identities**.
`FrankCoverage.tsv` assigns every identity to saved BattleScene moves, the Frank tester libraries/controls, or
supporting source assets; the inventory role field alone only describes BattleScene move arrays.

The saved BattleScene registrations cover **24 canonical actions** across Mankey and Pepe: seven weapon Heavy
families plus 17 non-idle Vol10 actions. The 62 scoped source identities with registered roles are exact animation
references in those arrays. `HOLD_LALI` and `NAGE_ESC` continuation clips share the registered HOLD entry; they are
continuation phases, not additional entry moves. Idle is support. The old Heavy `.anim` attack/reaction clips are
directly registered; the seven source-rig FBX clips have no demonstrated stable-identity or curve mapping to those
registrations and remain pending comparison.

The Frank tester scene separately references **17 Insane combo choices** (3 full combos + 14 steps), with 14
generated step reactions, plus **4 GreatSword execution pairs**. These are saved in `FrankCombinationTester`
libraries and are actionable demo content, but are not BattleScene combat move registrations. `FrankInsaneBuilder`
explicitly pairs source clips and generated responses; `FrankGreatSwordBuilder` explicitly maps the eight execution
FBX clips directly into its four pairs. No motion approval or damage/contact guarantee follows from those
registrations. Two `Damage_Critical_Warrior_Hit2` clips are reachable through the demo's alternate-reaction
control, but lack BattleScene move registration. Required remaining work is to trace canonical relationships
between the three full combos and their 14 steps, integrate every distinct action and supporting transition,
integrate the four execution pairs, and give the alternate Warrior reactions suitable reachable battle roles. Each
integration requires complete combat pair/damage/presentation support and validation; none is optional merely
because it currently lives only in the tester. Compare the seven Frank source-rig clips and selected Warrior
reaction take against live `.anim` clips by curves/stable identity before asserting canonical links; matching names
do not prove motion equivalence.

The other **19 identities** are support: two Frank target rigs, seven weapon source rigs, one Insane source model,
one calibration avatar, one selected Warrior reaction take, and seven selected Frank mesh assets. Their presence in
the dependency graph does not make them distinct actions. The TSV keeps support links and pending verification
explicit. Current runtime approval remains with the root-owned authoritative replay; this inventory closure makes
no new runtime claim.

Self-check: 126 data rows / 126 unique GUID-local-ID keys; 62 BattleScene-registered identities; 43
demo-library/support identities (42 distinct canonical action labels, plus idle); 19 support-only identities; 2
alternate-reaction identities. The 62 + 43 + 19 + 2 partition is exhaustive and totals 126.
