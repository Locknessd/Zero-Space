# BattleScene: giữ và phát đủ event khi tải

WebSocketManager giữ tin nhận được xuyên suốt quá trình tải scene. GameManager chỉ nhận tin sau
khi hai CharacterCombat, UI đã khởi tạo và LoadingManager cho phép phát sau khi đóng loading.
Trường hợp mở trực tiếp BattleScene trong Editor không phải chờ LoadingManager.

Backlog và event trực tiếp dùng chung hàng đợi. Sequence và event ID loại bỏ bản trùng;
event đến trước phần lịch sử chờ sequence còn thiếu. Snapshot không tự tăng cursor để bỏ lịch sử.
LoadingManager đăng ký muộn vẫn nhận biết trạng thái trận đã có tại WebSocketManager.

Các kiểm tra đã chạy trong Unity 6000.4.0f1:

- `Validation.txt`: Client Input/Frontend, khởi tạo nhanh/chậm, chờ loading đóng, listener đăng ký muộn,
  lịch sử trận FINISHED, bản trùng, event trực tiếp trong lúc bàn giao, backlog 1.024 event qua bốn frame,
  lỗi consumer không làm mất tin và event kết quả đến trước lịch sử. Các kiểm tra queue hiện có cũng qua.
- `PlayModeValidation.txt`: hai lượt đánh Light/Heavy trong BattleScene thật, đủ bốn callback kết thúc,
  GetUp, event kết quả sau animation, kiểm tra contact/hitstop/KO và pause.
- `WebGLCompile.txt`: nhánh C# WebGL biên dịch không lỗi. Chưa tạo lại bundle WebGL cho FE.

Chạy lại kiểm tra queue bằng menu `Tools > Combat > Validate Triplet Queue`.
Điều kiện giao thức BE và API FE được ghi trong `WEBGL_FRONTEND_INTEGRATION.md`.
