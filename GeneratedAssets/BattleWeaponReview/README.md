# Battle weapon materials

All seven Battle weapons now use complete, dedicated FlatKit materials on both Mankey and Pepe. The calibrated native meshes and animation tracks are inherited through 14 lightweight prefab variants in `Assets/DemoSence/BattleWeapons`.

The original weapons had complete material slots, but several referenced white, texture-free FBX default materials. GreatSword and Assassin blade materials were dark gray; the Katana blade used a flat orange emission. The new palette makes the silhouettes and separate blade/grip slots easier to read under Battle lighting.

| Weapon | Appearance | Native Battle preview |
| --- | --- | --- |
| Warrior + Shield | Cool steel sword, gold shield | [After](After/Mankey_Heavy_5.png) |
| GreatSword | Bright blue steel | [Before](Before/Mankey_Heavy_6.png) · [After](After/Mankey_Heavy_6.png) |
| Spear | Warm wood shaft, steel tip | [After](After/Mankey_Heavy_7.png) |
| TwoHandedAxe | Brass finish | [After](After/Mankey_Heavy_2.png) |
| DualDaggers | Ice-blue steel | [After](After/Mankey_Heavy_8.png) |
| Katana | Blue wrap/scabbard, champagne blade | [Before](Before/Mankey_Heavy_Katana.png) · [After](After/Mankey_Heavy_Katana.png) |
| Assassin | Slate grip, ice-blue blade | [After](After/Pepe_Heavy_Assassin.png) |

The materials use shaded faces, narrow highlights, a bright rim and a thin dark outline. Their brightness leaves headroom for Battle's key, fill, rim and impact lights. Source pack materials are retained; both the paired attack drivers and the weapon rig's fallback bindings point to the new variants. No runtime material allocation or new lighting effects were added.

## Verification

- [Original material audit](BeforeMaterials.txt).
- [Installation](Install.txt): 14 variants, original mesh and clip references retained.
- [Native render checks](AfterValidation.txt): 14 armed moves across both fighters, all 30 material slots including duplicate Katana/scabbard meshes, supported shaders and 1280 × 720 previews.
- [Slash integration checks](../BattleSlashPackReview/RenderValidation.txt): all 14 weapon attacks rendered with their existing Hovl/ERB slash effects after the material change.
- [Camera check](CameraUnchanged.txt): camera code and serialized camera settings checked against the saved pre-change scene.

Use **Tools → Battle → Repair and brighten weapon materials** to restore the bindings after regenerating the source combat drivers. Materials can be adjusted in `Assets/Materials/BattleWeapons`.
