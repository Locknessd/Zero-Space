# Battle: 6 bước polish theo video tham chiếu

Đã lắp cả 6 phần vào `Assets/Scenes/BattleScene.unity`, theo thứ tự đã thống nhất. Giữ nguyên camera hiện có: đối chiếu cả 6 khối Main Camera trong scene và SHA256 của `FrankCinematicCamera.cs` với bản trước khi làm. Xem [CameraUnchanged.txt](CameraUnchanged.txt).

| Bước | Đã áp dụng | Chỉnh trong project |
| --- | --- | --- |
| 1. Lực va chạm | Hit-stop 35 ms cho đòn nhẹ, 75 ms cho đòn nặng, 55 ms khi chạm đất. Flash hình sao nhỏ tại điểm va chạm, tắt trong 130 ms; theo đúng vị trí khi camera hiện có di chuyển. Cùng dừng đồng hồ animation/VFX, UI vẫn chạy bằng thời gian thực. | GameManager → `BattleImpactFeedback`: Light / Heavy / Ground Hold. |
| 2. Slash, hit, ground | Slash mesh nhọn, viền tối, dải vàng, lõi sáng, xoay theo hướng vung vũ khí. Vòng lực vàng mở rộng trên mặt đất. Giữ burst Mage/Archer, debris và chữ POW/WHAM/SMASH; rút ngắn chữ để không che lâu. | GameManager → `BattleVfxPlayer`; prefab riêng trong `Assets/Vfx/Battle/Comic`. |
| 3. Nhân vật toon | FlatKit dùng bóng chuyển mềm, màu tối sáng hơn và viền mảnh. Tắt highlight/rim tối gây đốm đen trên đầu và thân; giữ texture gốc. | `Assets/Materials/BattleFlatKit`; [ảnh trước/sau](../BattleCharacterSurfaceReview/README.md). |
| 4. Nền dịu hơn | Bốn nhóm material nền dùng bản riêng: saturation 0.58, contrast 0.86, exposure 0.88; giữ texture, lightmap và đèn nền. | `Assets/Materials/BattleComicBackdrop`. |
| 5. Bảng chiêu comic | Portrait và tên chiêu cho Katana/Assassin hoặc đòn kết liễu: “BLADE FURY!”, “PHANTOM STRIKE!”, “FINISHING BLOW!”. Native Unity UI/Text + DOTween, hiển thị 1,05 giây (120 ms xuất hiện, 780 ms giữ rõ, 150 ms mờ dần). Không chặn bấm; tự xóa khi hủy đòn/reset. | HUD → `Critical Comic Panel` / `BattleComicCutIn` → Display Seconds. |
| 6. HUD phản hồi | HP chính cập nhật ngay. Phần máu mất màu vàng giữ 200 ms rồi tụt trong 450 ms; snapshot HP trùng không làm chạy lại. Đòn nặng có CRITICAL và số damage pop lớn hơn; bộ đếm HIT lấy từ cue va chạm thực tế. | HUD → `BattleHudFeedback`; `BattleUiTextEffects`. |

Số trừ máu vẫn nằm phía trên thanh máu; chat mới và cách hiển thị `Turn X` được giữ. Các UI mới dùng thời gian unscaled để tiếp tục hoàn tất trong hit-stop hoặc khi `timeScale = 0`. Flash/chip/scale không cập nhật lại geometry khi đã đứng yên. Slash và vòng lực chỉ dùng một mesh particle mỗi cue; các hiệu ứng vẫn dùng pool tối đa 32 instance.

Đã cập nhật tiếp phần damage nhiều hit: HP và số trừ máu chia đều theo các cue va chạm, tổng và HP cuối giữ đúng server. Xem [BattleDamageReview](../BattleDamageReview/README.md).

| Xem hình | Ảnh |
| --- | --- |
| Toon + nền | [Nền và nhân vật](Step4_Backdrop.png) |
| Slash đã gắn vào đòn | [GreatSword swing](Step2_BattleSlash.png) |
| Đòn nhẹ quật ngã / hit nặng | [Light_1](Contact_Light_1_Ground.png), [Heavy_6](Contact_Heavy_6_Hit.png) |
| Va chạm đất | [Katana landing](Contact_Heavy_Katana_Ground.png) |
| Bảng chiêu chạy trong Play Mode | [Mankey](LiveComic_Mankey.png), [Pepe](LiveComic_Pepe.png), [Kết liễu](LiveComic_Finisher.png) |
| Bảng chiêu tiếng Anh ở mốc 0,8 giây | [Katana](Step5_Mankey.png), [Assassin](Step5_Pepe_Assassin.png), [Finisher](Step5_Mankey_Finisher.png) |
| HP chip và critical damage | [HUD](Step6_Hud.png) |

Kiểm tra VFX: [72 trường hợp](Step2Validation.txt), gồm hai nhân vật, hai hướng, thường/kết liễu, nhảy frame, cue không phát trùng, hủy đòn, shader và pool 22/32. [Kiểm tra render mesh](Step2MeshValidation.txt) xác nhận slash và vòng lực hiện đúng dải vàng trong renderer của Unity; đã sửa lỗi float vertex color bị Shuriken đọc sai bằng packed `Color32`.

Play Mode: [PlayValidation.txt](PlayValidation.txt) ghi 18 lượt đánh + sự kiện damage kết liễu/winner gửi trùng; 193 audio starts, 213 VFX starts, 94 cue đèn impact, 1,222 frame giữ nguyên pose trong hit-stop, 5 bảng chiêu. Âm thanh có tín hiệu thực ở AudioListener, peak 0.291. Đã kiểm tra trả lại nhịp, hủy hit-stop, giữ tốc độ 0.5 cũ, không tự mở pause bên ngoài, reset trận và disable. Camera hiện có chạy đủ 18 shot; cơ thể/vũ khí nằm trong khung an toàn ở 30,747 frame được kiểm tra.

Kiểm tra riêng: [flash/cue/reset](Step1Validation.txt), [FlatKit](../BattleCharacterSurfaceReview/AfterValidation.txt), [bảng chiêu](Step5Validation.txt), [HP chip/critical/reset](Step6Validation.txt). Kết quả kiểm tra chat và KO sau thay đổi nằm trong [UiPlayValidation.txt](UiPlayValidation.txt).

`BattleSceneBefore.unity.txt` và `MaterialsBefore/` lưu bản trước khi làm. Các pack VFX gốc không bị ghi đè; bộ prefab comic được tạo riêng. Các lần kiểm tra Play Mode dùng bản scene tạm, tắt WebSocket và tự xóa sau khi xong.
