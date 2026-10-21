# Kiểm tra khán giả và vỡ sàn — 19/10/2026

Đã áp dụng vào `Assets/Scenes/BattleScene.unity`.
Prefab: `Assets/MemeArena/Generated/Colosseum_20261019_185036/MemeColosseum_Stage.prefab`.
Backup: `Backups/BattleScene_beforeColosseum_20261019_185210.unity`.

- PASS biên dịch Unity 6000.4.0f1 / URP; chạy trong Editor hiện có, không khởi động lại.
- PASS 16 loại meme mới, fan gần/xa cùng atlas `Art/MemeCrowd2026.png`, giữ đúng tỉ lệ từng sprite.
- PASS 48 fan gần, ba phe, tổng 86 renderer / 73.238 triangle trên prefab; không có collider mới.
- PASS bốn slot vỡ sàn, mesh và material hợp lệ; cả sáu mặt tam giác mảnh đá hướng ra ngoài.
- PASS combo thật `Heavy_8`: 11 contact, phản ứng combo bình thường.
- PASS hai lần tiếp đất nonlethal và hai lần lethal: 4 event đất phát đúng 4 burst vỡ sàn.
- PASS đối chiếu cùng eventId, cùng frame event, tọa độ X/Z và cao độ mặt sàn + 0,018 m.
- PASS các lần tiếp đất sau KO vẫn phát hiệu ứng; KO/confetti chỉ phát một lần, phe thắng/thua độc lập.
- PASS reset combat dọn pool; vết nứt mờ hết và slot trở lại inactive sau lifetime cấu hình.
- PASS không ghi nhận stage exception, NullReference hoặc QueueError trong luồng thử.
- PASS GUID BattleScene và bảy file gameplay/camera/render/build/package được đối chiếu giữ nguyên.
- PASS dữ liệu LoadingScene chưa lưu khớp bản sao trước Play; phục hồi dấu chưa lưu sau thử nghiệm.

Ảnh: `ProductionBattleIdle.png`, `ProductionBattleCombo.png`, `ProductionBattleKO.png`,
`ProductionFloorFracture.png`. Ảnh render target không bao gồm HUD Screen Space Overlay.
Chi tiết thô: `ProductionPlayValidation.txt`, `EditorScenePreservation.txt`, `ProductionPreservation.json`.

Lượt Play cuối chạy sau khi sửa hướng mặt của mảnh đá: 2.885 mẫu, trung bình 8,72 ms/frame,
p95 10,87 ms ở 1280×720 trong Editor. Batch counter không khả dụng; số đo GC bao gồm chụp ảnh/instrumentation.
Chưa profile WebGL/player build, không xác nhận 60 FPS trên thiết bị đích.

Sprite là art comic tạo bằng image_gen tích hợp. Atlas nguồn và prompt nằm trong
`Art/MemeCrowd2026.png` và `Documentation/MemeCrowd2026Prompt.txt`.
