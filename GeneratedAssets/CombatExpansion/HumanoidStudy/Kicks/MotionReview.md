# Kick source-motion review

Reviewed all 39 stable identities from `Study.json` for source GUID `ec11ddf4c9321c24c9509644025391dc` and local subasset IDs 7400000–7400076 (even IDs), each from `Assets/Selected/FightingAnimsetPro/Animations/KB_Kicks.fbx`. The 37 action clips were reviewed from the corrected 12-pose Mankey CPU-skinned sheets and per-frame 60 Hz CSV trajectories; Pepe sheets were compared for representative high, turning, side/back, and axe-kick motions. The two one-frame bind/T-pose entries are support references, not actions.

## Preliminary strike-motion observations

The set includes straight/front kicks at low, mid, and high levels, curved roundhouse/turning kicks, raised-knee actions, side and rear-directed kicks, an overhead axe arc, hopping/repeated kicks, and a punch-to-kick combination. Most clips return to a compact guard after chamber, extension, and retraction; several round and rear kicks turn the pelvis or pivot the support foot. The long straight-kick clip has especially large pelvis travel (~2.2 m), and some knee/straight actions also advance substantially, so source motion range deserves attention. Repeated actions include 7400060, 7400064, and the distinct upper-body-then-kick beats in 7400066; their sparse frames leave some late lifts ambiguous. The TSV records preliminary source-time pose cues and pelvis displacement, not gameplay timing.

## Limits and unverified contact

No victim was paired with these animations, so no collision, hitbox, contact point, reach, damage window, or gameplay suitability is approved. Candidate beats mark visible chamber/extension or arc landmarks only. Twelve sampled poses per sheet cannot resolve exact onset or apex; low feet, high feet near the head, and rear-facing limbs can be occluded. Pepe comparisons preserve the broad motion silhouette in the inspected examples, but do not establish paired reach or contact. AnimatorRoot stays stationary in the CSV; pelvis/Hips positions carry the visible root travel and are reported in source coordinates.
