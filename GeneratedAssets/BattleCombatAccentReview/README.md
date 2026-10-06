Đã tích hợp vào `Assets/Scenes/BattleScene.unity`, theo thứ tự ưu tiên của yêu cầu.

1. Hit có lõi sáng, tia ngắn và hình comic tập trung tại đầu/ngực/chân gần điểm vũ khí hoặc tay/chân chạm nhất. Vũ khí được bake theo đúng pose tại cue, dùng đỉnh mesh để tránh chọn nhầm vùng rỗng của bounding box. Khi ngã có chữ **SMASH**, vòng lực ngắn và 16 cụm khói mềm lan thành vòng tròn.
2. Khi giao đấu: nền và fill còn 82%, key 90%, rim 92%; chuyển vào 0.18 giây và phục hồi 0.3 giây. Đèn chớp impact giữ nguyên cường độ. Reset/disable phục hồi ánh sáng và property block ban đầu.
3. Hit cuối đưa HP về 0 và bắt đầu slow motion 32% trong 1.8 giây thực, hồi tốc độ trong 0.3 giây; dùng chung bộ điều khiển clock với hit-stop. **Bảng KO chờ cả hai anim và slow motion/recovery xong mới hiện**, theo yêu cầu cập nhật sau đó. Có fallback cho KO nhận từ kết quả trận khi không có animation kết liễu. Chữ/UI chạy bằng thời gian thực. [Timing KO mới và kiểm tra Play Mode](../BattleKoTimingReview/README.md).
4. Một cue hit chỉ phát một prefab hit. Loại bỏ loop, auto-play, emission liên tục, burst tự lặp, sub-emitter và script demo. Gọi Begin/Advance trùng hoặc đi ngược thời gian không phát lại. Pool giới hạn 48, tái sử dụng chỗ trống khi đổi loại hiệu ứng.
5. Heavy hit rung tối đa 0.045 m, chạm đất 0.035 m, trong 0.2 giây. Biên rung tự giảm nếu cơ thể/vũ khí sát mép khung. Offset được xóa hoàn toàn sau rung hoặc hủy đòn. Góc, vị trí gốc, lens và thư viện shot được giữ.
6. Bảy loại vũ khí có bảy slash khác nhau và 14 impact nhẹ/nặng khác màu, hình tia. Đã đối chiếu năm frame từ video MP4 người dùng gửi: ưu tiên tia tập trung, lõi sáng, đường slash dễ đọc và nền dịu để nổi nhân vật. [Frame reference](Reference/Frame3.png). Link bổ sung là trận [Classic Iron Man vs Red Hulk (Max Difficulty) - Marvel Tokon](https://www.youtube.com/watch?v=mBc5gjSkOmM), khác clip trailer MP4. Đã đối chiếu các ảnh storyboard của trận này; [mốc hình ảnh và đối chiếu Battle](Reference/YouTube/README.md).
7. Hai skill heavy mới cho cả Mankey và Pepe: **PIERCING VOLLEY** dùng animation/bow/arrow của Archer, ba mũi tên với ba lần trừ HP; **RADIANT BURST** dùng animation/projectile/hit của White Mage, một lần trúng phép. Projectile đến đúng thời điểm contact; đối thủ bắt đầu đứng, recoil rồi ngã, lùi tối đa khoảng 0.65 m. Portrait/tên skill toàn bộ tiếng Anh.

FlatKit giữ bề mặt toon mềm; bỏ inverted hull trên cơ thể vì nó tạo mảng đen ở các pose gập người. Material vũ khí và asset pack gốc được giữ.

| Cảnh kiểm tra | Ảnh Unity |
| --- | --- |
| GreatSword hit đúng đầu | [Hit](Final/Heavy_6_Hit.png) |
| Katana chạm đất | [SMASH và khói](Final/Heavy_Katana_Ground.png) |
| Archer | [Hit](Final/Heavy_Archer_Hit.png), [Ngã](Final/Heavy_Archer_Ground.png) |
| White Mage | [Hit](Final/Heavy_WhiteMage_Hit.png), [Ngã](Final/Heavy_WhiteMage_Ground.png) |

Kiểm tra editor: [88 trường hợp VFX](VfxValidation.txt), [44 trường hợp audio](AudioValidation.txt), [90 trường hợp chia damage](DamageValidation.txt), [ánh sáng/rung/reset](AccentValidation.txt), [camera giữ nguyên](CameraUnchanged.txt). Các trường hợp bao gồm cả hai nhân vật, KO, đổi phía, frame skip, lặp cue và hủy đòn. Bản sao scene trước thay đổi: `BattleSceneBefore.unity.txt`.

Play Mode: [đã qua toàn bộ 22 lượt đánh và một lượt kết liễu](PlayValidation.txt): 79 lần cập nhật HP đúng frame, 245 VFX, 106 cue đèn, 74 lần rung có phục hồi; 22,488 frame giữ cơ thể/vũ khí trong khung. KO phát một lần, quan sát được tốc độ 32% cùng chữ KO; fallback kết quả trận, pause, reset và disable đều phục hồi clock. [KO thực tế](LiveKO.png).

Thẻ của hai skill mới giữ thời gian đọc 1.05 giây, thu gọn và đưa lên trên để lộ đường bắn/impact. Các thẻ critical hiện có giữ layout đầy đủ.
