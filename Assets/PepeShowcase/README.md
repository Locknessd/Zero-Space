# Pepe Attack Showcase

Mở `Assets/Scenes/PepeAttackShowcase.unity` và nhấn Play.

Scene chỉ hiển thị một nhân vật, mặc định là Pepe. Ba lượt dùng toàn bộ animation attack gốc:

- Katana.
- Critical Dual (animation tấn công; không chạy clip nhận hit).
- Combo 03.

Phòng sáng với sàn ô vuông, panel xanh và camera thấp di chuyển theo nhân vật được dựng theo
video tham chiếu: https://www.youtube.com/watch?v=e-hIZrAqgWc.

Trong Play mode, bảng điều khiển cho phép chọn Pepe/Mankey, chọn đòn, dừng/chạy, lặp và đổi tốc độ.
Để dùng nhân vật khác, gắn prefab có Animator và Avatar Humanoid hợp lệ vào `Custom Character Prefab`
trên object `Pepe Attack Showcase`, rồi chọn Custom. Model cần có đủ xương tay và chân;
xương ngón tay là tùy chọn. Prefab nên chỉ chứa model và Animator.

Menu `Tools > Pepe Showcase` có lệnh tạo lại scene, kiểm tra và quay video.
Chạy lệnh `Record 1080p 60 fps video` từ Edit mode; công cụ mở bản sao scene trong Play mode,
quay từng frame sau khi Unity render xong rồi trở về scene đang mở. Tiến độ nằm trong
`GeneratedAssets/PepeShowcase/Status.txt`. Video xuất ở
`GeneratedAssets/PepeShowcase/PepeAttackShowcase.mp4`, H.264 1080p/60 fps, âm thanh AAC stereo.

Bộ quay lấy một frame sau mỗi lần cập nhật animation và skin của Unity, rồi ghép âm thanh theo timeline attack.
Việc này giữ pose người và vũ khí cùng thời điểm; tránh pose mesh cũ khi render nhiều frame trong một tick Editor.
Bản video sửa lỗi còn được lưu ở `GeneratedAssets/PepeShowcase/PepeAttackShowcase_Fixed.mp4`.
Lệnh `Check actual Play mode animation` kiểm tra xương và biến dạng mesh cho ba đòn,
với Pepe, Mankey và nhân vật Humanoid thay thế. Báo cáo nằm trong `PlayModeValidation.txt`.
Nếu đổi máy, đặt EditorPrefs `ZeroSpace.Showcase.FFmpeg` thành đường dẫn FFmpeg của máy đó.
Các nút điều khiển Play mode không xuất hiện trong video.

VFX vũ khí dùng material, màu và bộ hiệu ứng slash có sẵn của BattleScene. Vệt kiếm được lấy mẫu
theo chuyển động lưỡi kiếm ở 120 Hz: Katana/Combo 03 dùng tông vàng, Critical Dual dùng tông xanh
trên cả hai kiếm. Combo 03 có muzzle flash ở cả ba phát súng. Hiệu ứng tự xóa khi đổi đòn/nhân vật.
Menu `Add Battle weapon VFX` cập nhật cấu hình; `Validate weapon VFX` kiểm tra các nhát chém,
ba muzzle flash và material. Video có VFX được lưu thêm ở `PepeAttackShowcase_WeaponVFX.mp4`.
