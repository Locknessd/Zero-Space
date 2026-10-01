# Cinematic camera mathematical validation

Result: **PASS**. Unity 6000.5.9f1.

This report verifies camera projection and playback numerically. It is not a visual cinematography approval. Composition, readability of overlapping silhouettes, dramatic timing, lighting and perceived smoothness require reviewing the captured images and motion.

- Role-specific camera keys visited: 94.
- Camera samples checked: 26842; projected geometry corners: 2185824.
- Repeat seeks checked: 282; review captures: 564.
- Full-duration samples: 30 fps at 960 × 720; 5 fps at 576 × 720 and 1280 × 720, including each clip endpoint.
- Selection and loop blends: 60 fps for the first 1.4 seconds at 960 × 720.
- Requirements: finite perspective camera transform/FOV; at least 0.049 viewport margin; geometry depth above 0.025; camera height at least 0.299.

Minimum viewport margin: 0.053924. Minimum geometry depth: 4.645081. Minimum camera height: 3.012249.

Maximum observed camera speed: 21.27919 units/s; acceleration: 181.3903 units/s²; angular speed: 85.36137 degrees/s. These are diagnostics, not universal pass/fail thresholds.

Per-shot and aspect measurements are in `Validation.csv`. Source pelvis displacement at loop boundaries and endpoint camera differences are in `LoopEndpoints.csv`; these diagnose source resets separately from blended playback. Six review frames per key are listed with time and FOV in `/tmp/frank-camera-review/Captures.csv`.
