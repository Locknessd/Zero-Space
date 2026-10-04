# Battle: camera, ánh sáng và VFX

Đã lưu thay đổi vào `Assets/Scenes/BattleScene.unity`.

- Camera tiến gần 18% trên hướng nhìn của từng shot, giữ góc quay và tiêu cự. Khi cơ thể/vũ khí cần thêm khoảng trống, cơ chế bảo vệ khung hình vẫn lùi camera vừa đủ. `Battle Distance Scale` trên Main Camera cho phép chỉnh mức này.
- Key light ấm với bóng mềm, fill ấm và rim xanh theo vị trí hai nhân vật. Hai đèn point được tái sử dụng cho chớp vàng của đòn nhẹ, xanh của đòn nặng và cam của va chạm đất; chớp tắt trong 0.15–0.28 giây, được xóa khi hủy đòn/reset trận. `Impact Intensity` trên GameManager/BattleLightingRig chỉnh lực ánh sáng.
- Đòn nặng ghép Archer với CFXR Hit Contrast + Debris, có lõi sáng, vòng lực và mảnh vỡ. Đòn nhẹ thêm lõi tương phản vào Mage. Va chạm đất ghép Mage với CFXR Ground Hit Contrast + Debris. Slash lớn hơn 20%; chữ POW/WHAM/WHOOSH/SMASH và các mốc animation/audio hiện có được giữ.
- Các prefab mạnh được tạo riêng trong `Assets/Vfx/Battle/BattlePower*.prefab`. Asset gốc của pack được giữ. Đã xem thêm Light Bomb, Energy Strike, Light Flash, Discharge Hit và ShockWave trong Candidates.png; chọn các burst ngắn, đọc rõ lực trên nhân vật.

| Cảnh | Trước | Sau |
| --- | --- | --- |
| Đứng chờ | [Ảnh](Before_Idle.png) | [Ảnh](After_Idle.png) |
| Va chạm GreatSword | [Ảnh](Before_Heavy_6_Hit_0.045.png) | [Ảnh](After_Heavy_6_Hit_0.045.png) |
| Ngã Katana | [Ảnh](Before_Heavy_Katana_Ground_0.120.png) | [Ảnh](After_Heavy_Katana_Ground_0.120.png) |

Kiểm tra: `Validation.txt` đo góc camera, khoảng dolly, biên cơ thể/vũ khí, cue đèn, decay, hủy đòn và phục hồi quality settings. `CharacterLightPixels.txt` so sánh cùng một tư thế/VFX khi chỉ bật/tắt đèn impact: 7,940 pixel màu xanh trên nhân vật nhận ánh sáng, độ sáng RGB tăng. `CameraValidation.txt` kiểm tra 72 trường hợp / 5,824 frame, cả hai phía, khung ngang/dọc và camera demo. `VfxValidation.txt` kiểm tra 72 trường hợp, shader, cue không phát trùng, reset và giới hạn pool. `PlayValidation.txt` ghi kết quả Play Mode.

Chỉ directional key tạo bóng; fill/rim/impact không tạo bóng. Rig cho phép tối đa hai flash cùng lúc, bật pixel lighting và shadow distance 18 m trong Battle rồi trả lại quality settings khi tắt. Cấu hình dựa trên shader Built-in FlatKit hiện có và [hướng dẫn Forward lighting của Unity](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/built-in-render-pipeline/built-in-rendering-paths/render-tech-forward-rendering).

`BattleSceneBefore.unity.txt` và các file `*.mat.before.txt` lưu cấu hình trước thay đổi.
