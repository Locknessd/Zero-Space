# Kiểm tra roster workbook — 20/10/2026

Đã áp dụng vào `Assets/Scenes/BattleScene.unity` trong Unity 6000.4.0f1 / URP 17.5, Editor DX11.

## Danh mục và art

- PASS: giữ đủ 154 dòng nhân vật đã lọc: 55 Robinhood và 99 meme tham chiếu.
- PASS: không thiếu hoặc lặp ID nguồn; xem `WorkbookSourceCoverage.json`.
- Giữ 16 hình cũ, thêm 143 hình comic; tổng 159 loại khán giả sau khi gộp nhận diện trùng.
- Từng dòng dự án/ticker/tham chiếu được giữ riêng trong manifest, kể cả khi dùng chung hình.
- Chín tấm nguồn qua kiểm tra alpha; tám tấm 16 nhân vật, tấm cuối 15 nhân vật.
- Sheet09-v2 sửa khoảng cách giữa các cutout; bản đầu được giữ và không dùng trong roster.
- Hình và chỉnh sửa dùng image_gen tích hợp; prompt tại `Documentation/RobinhoodPrompts/`.
- Đây là minh họa comic theo nhận diện/mô tả nguồn; không phải art chính thức của các dự án.

## Import và production

- Atlas: `Generated/WorkbookRoster_20261020_004132/SpectatorAtlas.png`, 2048 x 2048, nén HQ và mipmap.
- Roster dùng 159 sprite asset tham chiếu đúng một atlas; chín tấm ảnh nguồn không được tham chiếu runtime.
- Prefab: `Generated/Colosseum_20261020_004218/MemeColosseum_Stage.prefab`.
- PASS: mọi loại có chỗ trong 327 vị trí khán giả; 48 fan gần và 279 card xa trong ba mesh.
- PASS: 48 fan gần, 86 renderer, 73.238 triangle; không thêm collider hoặc script/material bị thiếu.
- PASS: shader được Editor hỗ trợ và validator không phát hiện lỗi biên dịch shader.
- PASS: GUID BattleScene giữ nguyên; snapshot gameplay và transform camera/fighter trước/sau trùng nhau.
- Backup scene: `Review/Backups/BattleScene_beforeColosseum_20261020_004240.unity`.
- Các phiên bản stage/asset cũ được giữ. Chi tiết: `WorkbookImport.txt`, `ProductionIntegration.txt`.

Đường dẫn asset ở trên tính từ `Assets/MemeArena/`.

## Chạy thử thực tế

`CheckProductionMemeColosseumPlay` hoàn tất lúc 00:43:33 ngày 20/10/2026.

- PASS: BattleScene khởi tạo GameManager, hai fighter, camera và adapter.
- PASS: cả 159 loại dùng chung atlas.
- PASS: combo Heavy_8 có 11 contact, tạo một phản ứng combo.
- PASS: HP xuống 20% phát phản ứng Boo cho phe thua.
- PASS: lethal combo phát đúng một KO và một confetti burst; hai phe Victory/Defeat độc lập.
- PASS: 4 ground contact tạo đúng 4 lần vỡ sàn, khớp event/frame/vị trí; pool có 4 slot.
- PASS: reset animation xóa hiệu ứng; vết nứt mờ và trả pool sau thời gian cấu hình.
- PASS: không ghi nhận stage exception, NullReference hoặc QueueError trong đường thử.
- Ảnh `ProductionBattleIdle.png` và `ProductionFloorFracture.png` đã được xem để kiểm tra hiển thị.

Chi tiết thô: `ProductionPlayValidation.txt`. Ảnh camera render target không gồm HUD Screen Space Overlay.
Editor được giữ nguyên tiến trình; scene đang mở và trạng thái chưa lưu được bảo toàn khi thử Play.

## Giới hạn kiểm tra

2.102 mẫu Editor 1280 x 720: trung bình 10,55 ms, p95 14,83 ms.
Số đo có instrumentation/chụp PNG; counter batch không khả dụng.
Đây không phải bằng chứng 60 FPS trên WebGL. Chưa build/profile trên browser hoặc kiểm thử trận BE hoàn chỉnh.
Các hình crowd có một pose gốc; animation cổ vũ dùng chuyển động procedural hiện có.
