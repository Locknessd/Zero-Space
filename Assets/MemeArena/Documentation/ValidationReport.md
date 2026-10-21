# Báo cáo kiểm tra Meme Colosseum

Ngày kiểm tra: 18/10/2026. Project ZeroSpace, Unity 6000.4.0f1, URP 17.5, Editor DX11.

Đây là kết quả kiểm tra nền tảng và scene thử trước tích hợp production.
Người dùng đã duyệt áp dụng vào BattleScene chính; xem thêm `Review/ProductionIntegration.txt`,
`Review/ProductionPlayValidation.txt` và `Review/ProductionPreservation.json` cho lần tích hợp sau đó.
Nâng cấp khán giả/vỡ sàn ngày 19/10/2026 có kết quả tại `Review/CrowdAndFloorValidation.md`.
Bản mở rộng workbook 159 loại ngày 20/10/2026 có kết quả mới nhất tại `Review/WorkbookValidation.md`.
Các số liệu bên dưới là lịch sử của bản nền tảng ngày 18/10, không phải roster hiện tại.

## Bản đã kiểm tra

- Prefab: `Assets/MemeArena/Generated/Colosseum_20261018_232403/MemeColosseum_Stage.prefab`.
- Scene thử: `Assets/MemeArena/Generated/Colosseum_20261018_232403/MemeColosseum_Battle.unity`.
- Scene này sao chép BattleScene và giữ các actor, GameManager, UI, camera và combat hiện có.
- Trong bản sao, map/ánh sáng cũ được tắt và thay bằng stage mới. Scene gốc không bị ghi đè.

## Kết quả

| Hạng mục | Kết quả và bằng chứng |
| --- | --- |
| Biên dịch C# thực tế | PASS trong Editor đang mở; runtime DLL cập nhật 23:26:30, Editor DLL 23:27:12. |
| Công cụ Editor | Menu đăng ký, job gọi generator/validator thành công; tạo prefab và bản sao battle thực tế. |
| Nền tảng Phase 1/2 | PASS; khán đài cong có normal mặt trên hướng lên, material hợp lệ, không thêm collider. |
| Prefab đầy đủ | PASS; không mất script, mesh hoặc material; shader được Editor hỗ trợ và không báo compile error. |
| Ngân sách geometry | 72.078 triangle, 79 renderer, 17 mesh asset chia sẻ; tính trên prefab stage, gồm lớp outline. |
| Crowd | 48 fan gần; ba phe độc lập; fan xa dùng atlas và ba mesh shader animation. |
| Camera và game thật | PASS; GameManager khởi tạo hai fighter, adapter đăng ký và ảnh Play mode hiển thị sân đúng. |
| Hit/combo | PASS; chạy `Heavy_8` bằng API test hiện có của GameManager: 11 contact, một phản ứng combo. |
| HP thấp | PASS; subscription slider phát phản ứng Boo cho phe FighterB khi HP còn 20%. |
| KO | PASS; lethal animation phát đúng một KO và một lần confetti pool. |
| Contact sau KO | PASS; trạng thái cuối Neutral=Victory, FighterA=Victory, FighterB=Defeat. |
| Runtime trong luồng thử | PASS; không ghi nhận exception của stage, NullReference hoặc QueueError. |
| Bảo toàn production | PASS; cả tám file đối chiếu SHA256 giữ nguyên. |

Chi tiết thô nằm trong `FoundationValidation.txt`, `PrefabValidation.txt`, `Review/PlayValidation.txt`
và `PreservedProject.after.json`. Ảnh: `Review/ColosseumOverview.png`, `BattleIdle.png`, `BattleCombo.png`, `BattleKO.png`.
Ảnh từ render target của camera không bao gồm HUD dạng Screen Space Overlay.

Lượt Play đầu tiên phát hiện selector đòn test không khớp tên, đã đổi sang chọn đòn có nhiều contact từ timeline thật.
Lượt tiếp theo phát hiện contact muộn ghi đè phản ứng thắng/thua; đã sửa adapter và chạy lại đạt kết quả trên.
Unity được giữ nguyên tiến trình; không khởi động lại Editor.

## Số đo hiệu năng

ProfilerRecorder chạy trong Play mode Editor, render target 1280×720, gồm sân và hai fighter thật:

- 2.067 mẫu sau 120 frame warm-up.
- Trung bình 9,74 ms/frame; p95 11,94 ms/frame.
- Counter triangle cao nhất: 223.598 cho toàn frame của bài kiểm tra.
- Counter GC cao nhất: 857.675 byte/frame; có instrumentation và chụp PNG trong bài kiểm tra.
- Counter batch không khả dụng trong phiên Editor này; giá trị 0 trong báo cáo thô không có nghĩa là không có draw call.

Đây là số đo Editor, không xác nhận 60 FPS trong build. Chưa chạy WebGL/player build hoặc profile trên thiết bị đích.
Cần đo frame time, draw call, GPU time, bộ nhớ và GC ổn định trên browser/thiết bị dự kiến phát hành.

## Phạm vi còn giới hạn

- Placeholder sprite chỉ có một hình mỗi loại; các pose phản ứng được làm procedural.
- Tượng Doge, sky và một số art vẫn là placeholder, xem `RemainingArt.md`.
- Renderer đang dùng không có SSAO; không sửa renderer hay cài thêm package.
- Game không có event C# riêng cho RoundStarted, RoundVictory hoặc backend CriticalHit.
  Adapter quan sát nhãn round, dùng heavy contact để cổ vũ mạnh và cung cấp các hàm Notify công khai.
- Không kiểm thử một trận BE/WebSocket hoàn chỉnh; bài kiểm tra dùng combat playback/timeline hiện có trong Editor.
- API tùy chọn cho prefab fan có Animator và Populate runtime đã biên dịch; chưa có art Animator production để kiểm tra.
- Sau lần kiểm tra này, scene battle chính đã được cập nhật theo phê duyệt; build settings giữ nguyên.

## Mở để xem thử

Mở `MemeColosseum_Battle.unity` trong thư mục phiên bản nêu trên, rồi Play.
Tạo/chỉnh cấu hình tại `Tools > Meme Arena > Generate Test Arena`.
Các lần regenerate/populate tạo phiên bản asset mới và giữ lại art reference được gán.
