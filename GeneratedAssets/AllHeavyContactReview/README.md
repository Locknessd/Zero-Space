# Rà soát toàn bộ animation Heavy

Đã cập nhật 14 cặp attack/hit Heavy, gồm 7 loại vũ khí của cả Mankey và Meme. GameObject của Meme trong asset vẫn tên `Pepe`. Có 84 lần chạm vũ khí/khiên/chân đã được hiệu chỉnh, tăng từ 76; đòn quật ngã gây sát thương của Assassin được giữ riêng.

| Vũ khí | Hit trước → sau, mỗi nhân vật | Ảnh Mankey | Ảnh Meme |
| --- | --- | --- | --- |
| Sword/Shield | 5 → 7 | [Xem](After/Mankey_Heavy_5_Contacts.png) | [Xem](After/Pepe_Heavy_5_Contacts.png) |
| Great Sword | 4 → 4 | [Xem](After/Mankey_Heavy_6_Contacts.png) | [Xem](After/Pepe_Heavy_6_Contacts.png) |
| Spear | 8 → 8 | [Xem](After/Mankey_Heavy_7_Contacts.png) | [Xem](After/Pepe_Heavy_7_Contacts.png) |
| Axe | 4 → 4 | [Xem](After/Mankey_Heavy_2_Contacts.png) | [Xem](After/Pepe_Heavy_2_Contacts.png) |
| Dual Daggers | 7 → 9 | [Xem](After/Mankey_Heavy_8_Contacts.png) | [Xem](After/Pepe_Heavy_8_Contacts.png) |
| Katana | 2 → 2 | [Xem](After/Mankey_Heavy_Katana_Contacts.png) | [Xem](After/Pepe_Heavy_Katana_Contacts.png) |
| Assassin | 8 → 8 | [Xem](After/Mankey_Heavy_Assassin_Contacts.png) | [Xem](After/Pepe_Heavy_Assassin_Contacts.png) |

Mỗi hàng ảnh là một lần chạm, theo thứ tự thời gian. Ba cột lần lượt là tư thế trước hit 25 ms, đúng tư thế hit với VFX, và tư thế sau hit 25 ms. Ảnh giữa giữ nguyên tư thế chạm và mô phỏng particle thêm 35 ms để thấy vòng tỏa; phép kiểm tra riêng xác nhận tia hit và hai vòng đã sinh ngay tại thời điểm chạm, trước hit-stop. Ảnh kích thước 1280×720 của từng hit cũng nằm trong `After/`.

Đã xem toàn bộ chuyển động của 14 cặp animation, lấy mẫu thô 120 lần/giây và kiểm tra mặt mesh trong từng khoảng đánh ở 480 lần/giây (bước 2,08 ms). Mỗi nhân vật được đo riêng trên mesh đã retarget. Chọn đúng vũ khí đang chuyển động, gồm cả dao trái/phải, rồi lưu điểm chạm theo bone của người nhận đòn. Khi hai dao cùng ở gần cơ thể, không dùng dao đang giữ yên để đại diện cho nhát chém của dao còn lại. Các đoạn giữ vũ khí trong người không tạo thêm hit; hai ứng viên Dual Daggers khoảng 3,63 s đã bị loại vì không có chạm khi vũ khí đang chuyển động.

Sword/Shield được bổ sung nhát chém ngược và đòn khi đối thủ đang ngã. Dual Daggers được bổ sung nhát chém trên không và nhát đâm xuống đất. Assassin được chuyển hit thứ hai từ đoạn giữ dao (~1,213 s) sang đoạn rút/chém thực sự (1,320 s). Các mốc Spear, Axe, Great Sword và Katana cũng được hiệu chỉnh lại theo chuyển động và bề mặt tiếp xúc. Tiếng vung vũ khí và vệt chém đi cùng mốc hit mới.

Đã kiểm tra trực tiếp bằng Unity:

- [112 trường hợp / 672 lần chạm](Validation.txt): đủ từng nhát đã xem, đánh hai hướng, thường/kết liễu, vượt khung hình, không phát sớm/lặp, dừng combo dọn sạch VFX. Khoảng hở mặt mesh lớn nhất đo được: 11.74 mm.
- [28 combo trong Play Mode](PlayModeValidation.txt): vượt cả combo trong một khung hình vẫn hit-stop đúng từng tư thế, tiếp tục đủ hit, kết liễu chậm một lần, hàng đợi Light/Heavy và GetUp hoạt động.
- [36 trường hợp vệt vũ khí](WeaponTrailValidation.txt): 164 vệt, 840 lần đối chiếu đầu vũ khí, pool tối đa 9/16.
- [16 trường hợp Assassin](AssassinSlashValidation.txt): vệt chém vẫn bám lưỡi dao; đủ 32 góc xem khi vũ khí đang chuyển động và sau chạm.
- [Phạm vi asset](ScopeValidation.txt): chỉ 14 profile Heavy đổi, scene Battle và Light giữ nguyên, tiếng/clip nguồn và các mốc ngã/quật được giữ nguyên.
- [Áp dụng lại](IdempotenceValidation.txt): bank giống từng byte, không thêm hit hay swing lần nữa.

Tổng sát thương/HP được kiểm tra đúng với BE cho cả đòn thường và kết liễu. Sát thương được chia qua các mốc chạm, không cộng thêm tổng sát thương khi bổ sung hit.

[Mốc và vị trí đã chọn](ApprovedSelection.csv) · [Thay đổi từng hit](Installation.csv) · [Ứng viên đã loại](RejectedCandidates.csv)

Thay đổi đã lưu trong `Assets/Audio/Battle/BattleSfxBank.asset`. Cần build WebGL lại để bản web nhận timeline mới. Phiên kiểm tra này chạy trong Unity Editor/Play Mode, chưa xuất hoặc thử bản WebGL mới trên trình duyệt.
