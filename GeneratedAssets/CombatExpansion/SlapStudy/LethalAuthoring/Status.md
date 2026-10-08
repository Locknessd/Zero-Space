# Provisional Slap lethal authoring

The empirical pose-encoding diagnosis completed and failed its native-pose
preservation gate. No candidate animation was created or assigned to gameplay.

`BakeProvenance.txt` records the latest encoding trials and dense validation.
Direct native-stream muscle capture progressed past coarse calibration, but the
860-sample validation still failed: maximum bone-position error 0.00475415029 m,
angle error 0.5719806 degrees, root error 0.00127869647 m and body error
0.00128094247 m. Worst bone position was LeftMiddleDistal at 0.470833331 s;
worst angle was LeftUpperArm at 0.404166669 s. The original tolerances remain.
Earlier diagnostic provenance is archived locally by the authoring tool.
This preserves a failed diagnostic, not a validated lethal treatment.

An adaptive native-sample refinement pass has since been added to address errors
between the original export keys. It compiles, but its dense pose validation has
not yet been rerun; the failed report remains the latest measured result.

The editor authoring and raw-preview modules compile. Raw preview requires a
successfully baked candidate and has not run. Grounding, conditional lethal
playback, presentation and lifecycle acceptance remain unfinished.

Full curve, importer and schema dumps are reproducible local diagnostics and
are excluded from this checkpoint. The source assets remain in their existing
repository locations; the authoring tools retain their source identities.
