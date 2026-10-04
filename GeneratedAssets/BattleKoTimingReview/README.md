Đã cập nhật KO trong `Assets/Scenes/BattleScene.unity` theo yêu cầu: **anim kết liễu và slow motion đều hoàn tất, sau đó mới hiện bảng KO**.

HP về 0 ở hit cuối chỉ đưa KO vào trạng thái chờ. Slow motion vẫn bắt đầu ở hit kết liễu: 32% tốc độ trong 1.8 giây thực, hồi tốc độ trong 0.3 giây. Bảng chờ cả hai nhân vật hết `IsBusy`, hết hit-stop và hết slow motion/recovery; lúc hiện bảng không khởi động lại slow motion. Trường hợp chỉ có kết quả/HP = 0 vẫn chạy slow motion trước bảng; nếu đang pause thì chờ resume. Reset/cancel xóa KO đang chờ.

Mode Animation Test dùng cùng logic. Play All chờ KO đang chờ/đang hiển thị hoàn tất trước khi chuyển sang cặp tiếp theo.

| Giai đoạn | Ảnh Unity |
| --- | --- |
| HP = 0, đang kết liễu/slow motion; bảng KO còn ẩn | [Trước KO](FinisherBeforeKo.png) |
| Cả hai anim đã xong, tốc độ bình thường; hiện KO | [Sau kết liễu](KoAfterFinisher.png) |

[Play Mode đã qua bảy trường hợp](PlayValidation.txt): Archer (anim xong trước slow recovery), GreatSword ở phía đối diện (slow recovery xong trước anim), White Mage, HP = 0 không có finishing animation, tắt slow motion, pause/resume và hủy KO đang chờ. Kiểm tra HP lặp không khởi động lại bảng/slow motion; bảng vẫn ẩn trong toàn bộ frame chờ. Case GreatSword rút ngắn slow window riêng trong bản sao test để kiểm tra gate animation; cấu hình Battle thật vẫn 1.8 + 0.3 giây.

Camera giữ nguyên góc và thiết lập gốc. Bản sao scene trước cập nhật: `BattleSceneBefore.unity.txt`.
