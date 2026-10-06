# VFX vũ khí: bám quỹ đạo và phân biệt đòn đánh

Đã áp dụng trực tiếp vào `Assets/Scenes/BattleScene.unity`.

## Cách vệt khớp với vũ khí

Vệt cũ là một hiệu ứng chém được xoay theo camera, đặt ở tâm vũ khí và đi theo vị trí tâm. Nó không mô tả đầy đủ chuyển động xoay của lưỡi, nên có thể lệch hoặc tạo một vòng chém quá rộng.

`BattleWeaponTrails` lấy hai điểm trên mesh vũ khí, tính vị trí theo các bone của rig nguồn và dựng một dải ngắn theo quỹ đạo thật trong không gian game. Phần vệt đã đi qua giữ nguyên vị trí. Đầu vệt bám tư thế hiện tại, kể cả khi dao đổi hướng nhanh giữa hai mẫu.

Cửa sổ vệt bắt đầu tại cue vung và kết thúc gần cue chạm tương ứng. Combo hai dao ghép từng cue vung với từng hit, đồng thời có thể tạo hai dải riêng khi cả hai dao cùng chuyển động. Cú đánh bằng khiên lấy mesh khiên; đấm/đá trong combo dùng phản hồi tay/chân.

VFX dùng đồng hồ animation của cặp nhân vật: hit-stop và pause giữ nguyên vệt, KO đi chậm cùng animation, reset/cancel xóa ngay. Timeline audio, điểm chạm và thời điểm sát thương đã hiệu chỉnh được giữ nguyên.

## Các kiểu hiệu ứng

| Vũ khí | Vệt | Va chạm bổ sung |
| --- | --- | --- |
| Kiếm và khiên | Vệt kiếm vàng ngắn; khiên có dải riêng | Vòng va chạm vàng tại cú đập khiên |
| GreatSword | Dải vàng cam theo lưỡi rộng, lưu lâu hơn | Giữ bộ impact nhẹ/nặng của kiếm lớn |
| Rìu hai tay | Dải đỏ cam tập trung vào đầu rìu | Impact theo rìu; cú đá dùng impact cơ thể |
| Katana | Dải mảnh màu cam nhạt, viền sáng | Impact riêng của katana |
| Thương | Vệt xanh ngọc gọn ở đầu thương | Dấu đâm xuyên dài theo hướng lực |
| Hai dao | Hai dải xanh lam ngắn theo từng dao | Dấu đâm xanh lam cho các cú stab |
| Assassin | Dải tím ngắn | Dấu đâm tím, khác với cú chém |

Dấu va chạm khiên/đâm xuyên rất ngắn và được vẽ rõ tại điểm chạm khi vũ khí che khuất nó. Vệt vũ khí và các tia lửa vẫn giữ kiểm tra độ sâu của cảnh.

## Hiệu năng và tài nguyên

- Hai đầu lưỡi đã được hiệu chỉnh theo mesh và bone; không cần bật lại Read/Write cho các mesh đã tối ưu.
- Quỹ đạo ngắn được lưu một lần cho mỗi cue. Mỗi khung hình nội suy dải và cập nhật đầu vệt bằng các biến đổi bone, không bake toàn bộ mesh vũ khí đã cấu hình.
- Pool vệt tối đa 16; pool particle hiện tại tối đa 48. Mesh và material được dùng lại khi phát các lượt tiếp theo.
- Các impact mới dùng lại tia lửa và texture đã có trong trận. Dải, vòng khiên và dấu đâm dùng mesh/shader không cần texture mới.
- Hiệu chỉnh endpoint chạy trong preview scene tạm. Việc cài đặt chỉ lưu binding VFX và component mới, không lưu tư thế hoặc controller của lượt thử.

## Chỉnh trong Unity

`BattleScene → GameManager → BattleWeaponTrails`:

- **Styles → Color:** màu từng bộ vũ khí.
- **Lifetime:** độ dài thời gian lưu vệt, preset 75–150 ms.
- **Blade Start:** vùng lưỡi tạo vệt; rìu/thương tập trung gần đầu vũ khí.
- **Max Trails:** giới hạn pool.

`BattleVfxPlayer` có `Weapon Trails`, `Shield Impact` và `Impact Variants → Stab`. Prefab chém cũ vẫn là fallback cho cấu hình chưa có binding vệt.

Menu `Tools → Battle → VFX` có:

- **Align weapon trails and contact styles:** cài lại preset và hiệu chỉnh endpoint.
- **Validate aligned weapon trails:** kiểm tra quỹ đạo, điểm bám, timeline, reset và combo.
- **Validate battle VFX:** kiểm tra cả bộ particle/vệt và shader.

Khi thay mesh hoặc rig vũ khí, chạy lại bước hiệu chỉnh endpoint. Bản WebGL cần build lại để FE nhận các thay đổi này. F8 tiếp tục chỉ có trong Unity Editor.

## Bằng chứng kiểm tra

Các báo cáo và ảnh nằm tại `GeneratedAssets/WeaponVfxAlignmentReview`:

- `Validation.txt`: 36 trường hợp, 116 vệt, 536 lần đối chiếu đầu vệt với vertex vũ khí; cả Mankey/Pepe, sống/chết, combo, chống phát lặp và hủy đòn.
- `ExistingVfxValidation.txt`: kiểm tra 72 trường hợp của toàn bộ VFX, gồm đảo hướng, skip cả combo, tắt audio và giới hạn pool.
- `PlayModeValidation.txt`: hit-stop giữ đúng từng contact, KO một lần, pause/reset, queue Light → Heavy hoàn tất đủ bốn callback và GetUp.
- `Dependencies.txt`: tài nguyên bổ sung so với BattleScene trước khi chỉnh.
- `WebGLCompile.txt`: biên dịch mã runtime với define WebGL, bỏ define Editor.
- `ScopeValidation.json`: đối chiếu object trong scene và bank contact/audio.
- `Before/`, `After/`: ảnh hai nhân vật với cả bảy bộ vũ khí ở pha vung và pha chạm. `Before` tắt component vệt mới để đối chiếu với prefab chém cũ.

Ảnh mẫu: [GreatSword](../GeneratedAssets/WeaponVfxAlignmentReview/After/Mankey_GreatSword_Sweep.png), [hai dao](../GeneratedAssets/WeaponVfxAlignmentReview/After/Pepe_DualDaggers_Sweep.png), [khiên](../GeneratedAssets/WeaponVfxAlignmentReview/After/Mankey_WarriorShield_Contact.png).

Endpoint được đọc trong Editor bằng [MeshUtility.AcquireReadOnlyMeshData](https://docs.unity3d.com/ja/current/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html), API cho phép đọc mesh đã tắt Read/Write. Việc kiểm tra đối chiếu dùng [SkinnedMeshRenderer.BakeMesh](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/skinnedmeshrenderer/bakemesh) trên tư thế nguồn.
