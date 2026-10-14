# Nhân vật BattleScene

Đã tích hợp Cash Monkey, Neon Nutcase, Neon Streetcat và Ninja Neko cùng Mankey/Pepe.
4 FBX trong `Assets/Models` dùng Humanoid, Create From This Model, đủ 52 mapping xương.
Prefab dùng trong game có trục root chuẩn hóa, T-Pose hợp lệ, FlatKit và chân dung riêng.

## Sử dụng

- Trong Play Mode: bấm **CHARACTERS** góc phải dưới, chọn riêng LEFT hoặc RIGHT.
- Trước Play/build: chọn fighter, đổi **Battle Character Appearance → Startup character**.
- Đổi trong lúc đánh sẽ chờ hết tấn công, phản ứng và đứng dậy. HP, vị trí và danh tính bot được giữ.
- API C#/WebGL, danh sách ID: `Docs/BATTLE_CHARACTERS.md`.

## Kiểm tra

- `Humanoid.txt`: 4 Avatar hợp lệ, mỗi Avatar 52 mapping gồm 30 xương ngón tay.
- `Validation.txt`: 312 lượt / 12.456 mẫu pose; kiểm tra hai phía với cả 6 nhân vật.
- Độ lệch grip lớn nhất ngoài đoạn chuyển từ Idle: 0,032855 m; ngưỡng kiểm tra 0,04 m.
- `ControllerClips.txt`: 680 mẫu Idle/Walk/Victory và toàn bộ clip đứng dậy được đăng ký.
- `Calibration.txt`: 776 điểm chạm được đo trên bề mặt model tại thời điểm của timeline hiện tại.
- `ExtraStatus.txt`: đã bổ sung ground correction cho các Avatar mới trong clip đứng dậy.
- `RuntimeCheck.txt`: PASS đổi ngay, đổi đang chờ, attack/reaction/get-up, VFX,
  giữ HP/vị trí, đổi lại Mankey/Pepe và từ chối ID không hợp lệ.
- `BattleRosterOverview.png`, `RuntimePreviews/`: ảnh gameplay 1920×1080.

## Khôi phục và tài nguyên

- Scene trước khi thêm roster: `Backup_20261014_202122/BattleScene.unity`.
- Metadata model trước Humanoid: `BeforeHumanoid/`.
- Asset trước khi thêm anchor/grounding: `BeforeCalibration/`.
- Bộ chọn: `Assets/Characters/BattleRoster/BattleCharacters.asset`.
- Prefab, Avatar, material, portrait, grounding: cùng thư mục catalog.
- Chỉ hai model hiển thị trong trận; model cũ được giải phóng khi đổi.
- Ground/contact được đo bằng Editor, không tính lại mesh contact khi chạy WebGL.

Đã đồng bộ vũ khí gốc với độ nâng cơ thể do ground correction, để tránh vũ khí rời bàn tay.
Các mốc hit, danh sách đòn và backend ID của trận vẫn dùng dữ liệu hiện tại.
