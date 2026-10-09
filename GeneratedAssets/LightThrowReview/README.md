Đã thêm `Light_JPBOM`, `Light_SIHO`, `Light_GSWING` vào pool Attack Light của cả Mankey và Meme. Mỗi nhân vật có 5 đòn Light, gồm 2 đòn cũ và 3 đòn mới. Luồng nhận `attack_light` từ BE chọn được các đòn mới qua pool hiện có.

Các đòn dùng đúng cặp animation người ra đòn/người nhận đòn của Vol10 và GetUp hiện có. VFX WHOOSH đi theo tay khi nâng hoặc theo cơ thể khi xoay/quật; bụi, vòng va chạm và âm thanh tiếp đất dùng chung mốc tiếp xúc. GSWING có va chạm khi quật xuống lần đầu và khi thả/quật lần cuối. Damage chỉ chia tại các nhịp tiếp xúc, giữ nguyên tổng damage và HP do BE gửi.

| Đòn | Mankey ra đòn: mốc tiếp đất | Meme ra đòn: mốc tiếp đất |
| --- | --- | --- |
| JPBOM | 1.096667 s | 1.092500 s |
| SIHO | 0.882500 s | 0.734583 s |
| GSWING | 0.350833 s; 1.580000 s | 0.336250 s; 1.575833 s |

Độ cao cơ thể nhận đòn được hiệu chỉnh riêng theo model, với dữ liệu lấy mẫu 480 Hz và nội suy khi chạy. Tay đang giữ cơ thể đi theo phần nâng này. Runtime không bake mesh cơ thể; các animation/driver/VFX được dùng lại, không tạo bản sao animation hoặc texture mới.

Đã kiểm tra trong Unity:

- [48 trường hợp / 64 va chạm tiếp đất](Validation.txt): hai hướng, thường/KO, frame chậm bỏ qua nhiều cue, hủy đòn, chống phát trùng và đúng tổng HP/damage. Kiểm tra cơ thể ở 960 Hz, gồm điểm giữa các mẫu; mức thấp nhất so với nền là -0.000290 m.
- [Play Mode](PlayModeValidation.txt): mọi Light/Heavy trên hai nhân vật, thường/KO, giữ đúng pose tại từng contact khi một frame vượt cả combo. Cả 6 bộ đòn mới chạy hết qua GameManager, GetUp và hai callback; tiếp tục được hàng đợi Light + Heavy.
- [VFX vũ khí cũ](WeaponRegressionValidation.txt): 48 trường hợp, 164 nhịp vung và 840 lần so sánh đầu lưỡi vũ khí; các trail, contact và pool vẫn hoạt động đúng.
- [Phạm vi thay đổi](ScopeValidation.txt): dữ liệu Scene ngoài 6 mục Light mới, 16 timeline cũ và các nhóm âm thanh giữ nguyên. Cài lại cho kết quả Scene/bank giống từng byte.
- Biên dịch phần C# với `UNITY_WEBGL`, bỏ `UNITY_EDITOR`: 0 lỗi, 45 cảnh báo sẵn có. Chưa xuất build WebGL hoặc chạy thử trong trình duyệt.

Ảnh mỗi hàng gồm pose trước cue 25 ms, pose cue với VFX, pose sau cue 25 ms. Cột giữa giữ pose để nhìn hiệu ứng: WHOOSH tại tuổi 120 ms; va chạm tiếp đất tại tuổi 35 ms. Các ảnh không dùng để đo thời điểm damage; xem báo cáo validation ở trên.

| Người ra đòn | JPBOM | SIHO | GSWING |
| --- | --- | --- | --- |
| Mankey | [Ảnh](After/Mankey_Light_JPBOM_Effects.png) | [Ảnh](After/Mankey_Light_SIHO_Effects.png) | [Ảnh](After/Mankey_Light_GSWING_Effects.png) |
| Meme | [Ảnh](After/Pepe_Light_JPBOM_Effects.png) | [Ảnh](After/Pepe_Light_SIHO_Effects.png) | [Ảnh](After/Pepe_Light_GSWING_Effects.png) |

Có thể xem từng đòn bằng F8 trong Editor, chọn nhân vật và `Light_JPBOM`, `Light_SIHO` hoặc `Light_GSWING`. Tên object nội bộ `Pepe` vẫn được giữ; tên hiển thị là Meme. Để đưa thay đổi lên web cần build lại WebGL.
