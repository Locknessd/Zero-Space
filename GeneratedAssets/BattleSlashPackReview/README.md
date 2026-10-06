# VFX Hovl Studio và ErbGameArt trong Battle

Đã chọn 4 VFX sau khi render 34 ứng viên ở hai thời điểm, rồi gắn vào `BattleScene`. `GameManager → BattleVfxPlayer → Blade Slash Variants` chọn hiệu ứng theo vũ khí của từng lượt.

| Vũ khí | VFX nguồn | Phong cách |
| --- | --- | --- |
| GreatSword, WarriorShield, Spear khi vung | Hovl Studio / Sword slash VFX / Sword Slash 3 | Cung vàng, lửa và tia sáng; chỉnh kích thước riêng từng vũ khí. |
| TwoHandedAxe | Hovl Studio / Sword slash VFX / Sword Slash 7 | Cung lực đỏ, mạnh hơn cho đòn rìu. |
| Katana | ErbGameArt / Sword slash FX / New / Slash 3 | Crescent cam sắc, sáng. |
| DualDaggers, Assassin | ErbGameArt / Sword slash FX / New / Slash 6 | Nhiều vệt xanh, ngắn và nhanh cho combo dao. |

Prefab và material dành riêng cho Battle nằm trong `Assets/Vfx/Battle/SlashPacks`. Particle không loop/autoplay, không tự hủy đối tượng pool; bỏ âm thanh, demo script, distortion và đèn của pack. Giới hạn 64 particle/system, lifetime tối đa .42 giây ở tốc độ mô phỏng 1.8–2.1. Mỗi cue vung phát một hiệu ứng, dùng pool 32 instance hiện có.

Slash xoay theo chuyển động vũ khí tại pose của cue và theo lưỡi kiếm trong .18 giây đầu. Với vũ khí skinned, lấy tâm mesh ở pose đã evaluate thay vì bounds có thể còn thuộc frame trước. Dùng một mesh tạm tái sử dụng; các hiệu ứng cùng bám một lưỡi kiếm dùng chung kết quả trong frame. Particle dùng local simulation để di chuyển cùng hiệu ứng.

Giữ các burst White Mage/Archer ở hit và ground impact, chữ POW/WHAM/WHOOSH/SMASH, nhịp âm thanh và phần damage chia theo từng hit. Camera giữ nguyên cả code lẫn các component trong scene.

Ảnh chạy thật:

- [GreatSword vàng](Live/Mankey_Heavy_6.png)
- [Rìu đỏ](Live/Mankey_Heavy_2.png)
- [Katana cam](Live/Mankey_Heavy_Katana.png)
- [Combo dao của Pepe](Live/Pepe_Heavy_8.png)
- [Assassin của Pepe](Live/Pepe_Heavy_Assassin.png)

Ảnh preview 1280×720 tại pose cue: [Katana](Battle/Mankey_Heavy_Katana.png), [GreatSword](Battle/Mankey_Heavy_6.png). [Danh sách ứng viên](Candidates/Index.txt) và contact sheet [1](Candidates/Sheet0.png), [2](Candidates/Sheet1.png), [3](Candidates/Sheet2.png).

Kiểm tra:

- [TimelineValidation.txt](TimelineValidation.txt): 72 trường hợp hai nhân vật, hai hướng, thường/KO; đúng prefab từng vũ khí, cue không lặp, hủy/reset, nhảy qua toàn bộ combo, shader; pool 32/32.
- [BindingValidation.txt](BindingValidation.txt): 7 ánh xạ vũ khí, 4 prefab, material riêng, particle local và các giới hạn phát hiệu ứng.
- [RenderValidation.txt](RenderValidation.txt): 14 preview trong Battle, đủ 7 vũ khí ở hai nhân vật.
- [LiveSlashValidation.txt](LiveSlashValidation.txt): 14 ảnh native Play Mode; điểm phát bám tâm mesh vũ khí với offset .09 m.
- [PlayValidation.txt](PlayValidation.txt): 18 server exchanges và đòn KO; 71 mốc damage, 213 VFX, 94 cue đèn va chạm. Không phát trùng, giữ hit-stop/KO/queue, toàn bộ particle trả về pool.
- [CameraUnchanged.txt](CameraUnchanged.txt): 6 block camera trong YAML và SHA256 của `FrankCinematicCamera.cs` giữ nguyên.

Chạy lại bằng các job `InstallBattleSlashPacks`, `ValidateBattleSlashPacks`, `CaptureBattleSlashPackContacts`, `BattleSlashPackPlayCheck`. Play check dùng scene Battle tạm với WebSocket tắt và tự xóa khi xong. Backup scene trước thay đổi nằm ở `BattleSceneBefore.unity.txt`.
