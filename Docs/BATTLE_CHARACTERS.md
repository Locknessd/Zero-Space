# Battle character roster

BattleScene supports Mankey, Meme (Pepe), Cash Monkey, Neon Nutcase, Neon Streetcat and Ninja Neko.
The four models in `Assets/Models` use Humanoid / Create From This Model, with 52 explicit bone mappings.
Their battle prefabs include a normalized root and a validated T-Pose.
All six characters now share a reference height of **2 Unity units** on both teams.
Height is measured from the evaluated skin in the reference pose, rather than each fighter's old leg scale.
Uniform scaling preserves body proportions; crouching, jumping and leaning still follow the animation.
The hidden animation drivers also use one shared scale, independent of the side or selected character.
SlapFace impacts use face anchors per avatar and blend the attacking palm onto the receiver's animated face.

## Choose a character

- In Play mode, click **CHARACTERS** at the bottom right. Choose a character in the LEFT or RIGHT column.
- Before Play/build, select a fighter in BattleScene and set **Battle Character Appearance → Startup character**.
- An active attack finishes, including the reaction and recovery, before a requested appearance is applied.
- Multiple requests for one side keep the most recent choice. A defeated fighter cannot be changed during that match.
- Appearance selection preserves fighter objects, HP, backend IDs, pending events, move pools and combat timing.

| Character | ID |
| --- | --- |
| Mankey | `mankey` |
| Meme / Pepe | `pepe` |
| Cash Monkey | `cash_monkey` |
| Neon Nutcase | `neon_nutcase` |
| Neon Streetcat | `neon_streetcat` |
| Ninja Neko | `ninja_neko` |

## C# and WebGL calls

```csharp
bool accepted = GameManager.Instance.RequestCharacter(PlayerUI.Side.Left, "cash_monkey");
GameManager.Instance.SetRightCharacter("ninja_neko");
```

Once BattleScene and its `GameManager` object are loaded, JavaScript inside the Unity iframe can call:

```javascript
unityInstance.SendMessage("GameManager", "SetLeftCharacter", "cash_monkey");
unityInstance.SendMessage("GameManager", "SetRightCharacter", "ninja_neko");
```

The iframe page that owns `unityInstance` makes these calls. A parent FE page should relay its selection
through its existing validated iframe messaging bridge. Calling these methods does not start a match or
connect to the backend. Keep the startup integration in `WEBGL_FRONTEND_INTEGRATION.md`.
Unknown IDs leave the current character intact. `RequestCharacter` reports acceptance through its return
value; Unity `SendMessage` does not return that value to JavaScript.

## Assets and authoring

- Catalog: `Assets/Characters/BattleRoster/BattleCharacters.asset`.
- Prefabs, normalized avatars, materials, portraits and ground corrections live beside the catalog.
- The original model files remain in `Assets/Models`; importer backups are in
  `GeneratedAssets/BattleRoster/BeforeHumanoid`.
- Scene backups and calibration reports are in `GeneratedAssets/BattleRoster`.
- Editor commands are under **Tools → Battle → Characters**. Convert models before installing the roster.
- Retargeting resolves anatomical bones for other avatars while preserving the original reviewed bindings.
- Contact anchors are stored per avatar. Existing contact timestamps and damage ownership stay unchanged.
- Floor correction curves are owned by each fighter/move pair; the left and right calibrations are separate.
- JPBOM, SIHO and GSWING use avatar-specific floor curves sampled at 240 Hz. Their receiver tracks replace
  the original throw's floor lift, avoiding a second upward offset when a different model lands.
- Run `RepairBattleRosterFalls` through the local Editor job bridge after changing these throw models.
  It backs up the current assets, rebakes ground contacts and checks floor clearance at 480 Hz.
- Controller get-up curves in `RecoveryGrounding` include each move's terminal pose and entry blend.
  Rebuild them with `RepairBattleRosterRecoveryFloors` after changing a character or recovery animation.
  The isolated-clip extras baker preserves these curves. Corrections may lower as well as raise the pelvis.
- After replacing a roster prefab, run **Normalize character heights**, then recalibrate the roster and recovery
  grounding. Rebuild the height measurements, floor tracks and contact anchors together before building the game.

The authoring helper invokes the installed Editor's Enforce T-Pose operation, matching
[Unity's Avatar setup implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.4/Editor/Mono/Inspector/Avatar/AvatarSetupTool.cs).
This reflection is confined to Editor tooling and is not included in a WebGL player.
