# VFX trong BattleScene

`GameManager`, Mankey và Pepe tham chiếu cùng `BattleVfxPlayer`. Mở BattleScene, bấm Play rồi dùng **Q** để thử đòn nhẹ bên trái hoặc **E** để thử combo vũ khí bên phải. Các lượt nhận từ WebSocket dùng cùng luồng phát hiệu ứng.

- **LightHit**: hit nhỏ tại vị trí đầu/ngực/hông gần tay, chân hoặc vũ khí đang đánh nhất.
- **HeavyHit**: hit vàng cho các mốc `heavy_hit`.
- **LandingDust**: vòng bụi sát sàn ở mốc rơi; nhánh kết liễu tăng kích thước 35%.
- **BladeSlash**: vệt chém xanh ở động tác vung lưỡi vũ khí, nhỏ hơn khi dùng dao đôi. Động tác đâm giáo và đòn tay không không phát vệt chém.

Đòn nhẹ hiện tại là đòn vật. Hit xuất hiện lúc đối thủ chạm đất, tránh lóe sáng sớm trong động tác chuẩn bị. Các mốc va chạm/rơi/vung dùng trực tiếp `BattleSfxBank → Moves → Cues`, tính theo giây trên source animation. Hiệu ứng không phụ thuộc việc bật âm thanh và không dùng Animation Event trên Animator đang tắt.

Trên **GameManager → Battle Vfx Player**, chỉnh **Effect Scale** để tăng/giảm toàn bộ hiệu ứng; **Ground Height** là cao độ sàn đấu. Bốn prefab là biến thể riêng của Cartoon FX Remaster, dùng bản vật liệu riêng trong `Materials/` để gắn đúng shader đã import và tắt soft particles khi camera không có depth texture. Các prefab tắt phát lúc mở scene, vòng lặp, ánh sáng phụ và rung camera tích hợp. Camera chiến đấu tiếp tục quản lý rung. Các instance được tái sử dụng trong pool tối đa 24 object; hủy lượt hoặc reset trận xóa ngay particle còn lại.

Hiệu ứng legacy trên nhân vật được giữ làm dự phòng và tắt lúc mở scene. Khi VFX mới có timeline hợp lệ, legacy không phát chồng.

Menu **Tools → Battle → Install battle VFX** dựng lại prefab và reference mặc định. **Validate battle VFX** kiểm tra các đòn của cả hai bên, shader, nhánh chết, mốc lặp/seek ngược và hủy lượt. Báo cáo và ảnh nằm trong `Temp/FrankRetarget/`. Job `BattleSfxPlayCheck` kiểm tra thêm particle thực sự chạy, queue hoàn thành và pool thu hồi trong Play Mode.
