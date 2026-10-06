# Katana và Assassin trong BattleScene

Hai đòn được thêm vào `heavyCombatMoves` của Mankey và Pepe, bên cạnh 5 đòn heavy hiện có.

| Đòn trong pool | Attack | Hit reaction | Khoảng cách gốc | Thời lượng cặp |
| --- | --- | --- | --- | --- |
| Heavy_Katana | Damage_Critical_Katana | Damage_Critical_Katana_Hit | 1.748693 m | 6.000 s |
| Heavy_Assassin | Damage_Critical_Assassin | Damage_Critical_Assassin_Hit | 2.138693 m | 5.833 s |

Các cặp dùng driver đã hiệu chỉnh trong demo, vị trí/rotation người nhận đòn của demo và một đồng hồ phát chung. `attackRange` lấy từ khoảng cách của cặp; không đổi riêng thời gian hit hoặc kéo từng xương để bù khoảng cách. Người còn máu chạy `rise_01` rồi về Idle; người hết máu giữ tư thế ngã. Hướng người thắng sau KO vẫn bám vị trí cơ thể đang nằm.

Âm thanh và VFX dùng chung các mốc đã xem từ pose 30 Hz: Katana chạm ở 1.57 / 2.27 / 3.13 s, ngã 4.07 s; Assassin chạm ở 0.87 / 1.33 s, bật nhảy 3.33 s, va xuống đất 4.10 s. Giữ các clip âm thanh, VFX và mức âm lượng đã chọn trong project.

Menu `Tools → Battle → Add Katana and Assassin heavy pairs` lưu lại hai đòn này và cập nhật timeline mà không tạo bản trùng. Bản scene và bank trước khi thêm nằm trong thư mục này.

`PairValidation.txt` so sánh 121 mốc của mỗi cặp với demo gốc. `RecoveryValidation.txt`, `AudioValidation.txt`, `VfxValidation.txt` và `KoFacingValidation.txt` kiểm tra hoàn thành, đứng dậy/ngã, audio/VFX và hướng nhìn. `PlayValidation.txt` chạy hai đòn mới của cả hai nhân vật qua queue trong bản sao Battle không kết nối server.
