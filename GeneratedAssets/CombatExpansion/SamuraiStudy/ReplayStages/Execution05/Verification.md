# Absolute base-clip sampling verification

Original native Animator roots accumulated order-dependent displacement during absolute seeks.
The plain single-clip path now uses the existing track-sampling policy: prime at zero, restore the
configured source pose, then evaluate the requested time. Track and grapple continuation paths are unchanged.

The four actual fighter/direction diagnostic cases completed after the repair:

| Pair | Maximum replay world error (m) | Maximum forward-pose difference from baseline (m) | Attacker root travel (m) | Receiver root travel (m) |
|---|---:|---:|---:|---:|
| Mankey_Pepe_1 | 0.000000000 | 0.000141814 | 0.858346 | 3.446954 |
| Mankey_Pepe_-1 | 0.000000000 | 0.000137871 | 0.858346 | 3.446954 |
| Pepe_Mankey_1 | 0.000000000 | 0.000126720 | 0.925624 | 3.196415 |
| Pepe_Mankey_-1 | 0.000000000 | 0.000123791 | 0.925624 | 3.196415 |

The 1 mm gate is unchanged. These results cover pose replay, including all mapped source/fighter
human bones; they do not approve contacts, reactions, presentation or the full gameplay expansion.
Pre-repair CSVs and the initial duplicate-endpoint diagnostic failure are preserved in subdirectories.
Dense Execution05 contact recapture is still required. Samurai gameplay regression passed all 40 cases / 44 activations after the repair; the prior play scene was restored.
Other affected base-clip gameplay paths require their corresponding existing regression suites.
