# Khán giả meme và hiệu ứng vỡ sàn

Yêu cầu ngày 19/10/2026: thay toàn bộ khán giả bằng meme dễ nhận diện và thêm vỡ sàn khi bị đập xuống.

## Khán giả mới

16 loại: Pepe, Doge, Cheems, Popcat, Chill Guy, Capybara, Pibble, Maxwell,
Tung Tung Tung Sahur, Tralalero Tralala, Ballerina Cappuccina, Bombardiro Crocodilo,
Chimpanzini Bananini, Wojak, Smurf Cat và Big Floppa.

Art comic mới được tạo bằng công cụ image_gen tích hợp, nền alpha trong suốt.
Đây là sprite minh họa mới, không phải bộ hình/model chính thức của các meme.

- Atlas gốc: `Assets/MemeArena/Art/MemeCrowd2026.png`.
- Prompt: `Documentation/MemeCrowd2026Prompt.txt`.
- Sprite asset và cấu hình được tạo trong thư mục `Generated/MemeRoster_*`.
- Đo vùng cắt từng nhân vật để giữ tai, mũ và tay; billboard giữ đúng tỉ lệ sprite.
- Cả fan gần, fan xa và hàng sau cùng đều dùng bộ meme mới; không dùng lớp crowd silhouette cũ.
- Các sprite dùng chung một texture; generator tái sử dụng atlas trực tiếp, không đóng gói trùng thêm một atlas.
- Giữ ba phe, phản ứng hit/combo/HP thấp/KO và giới hạn 48 fan gần của cấu hình đang dùng.

Tham khảo nhận diện/xu hướng:

- https://www.creativebloq.com/news/this-weird-legal-case-could-change-how-we-think-of-copyright-forever
- https://knowyourmeme.com/memes/pibble

Danh sách là lựa chọn art cho game, không khẳng định thứ hạng phổ biến của từng meme.

## Vỡ sàn

`MemeArenaFloorFracture` đăng ký trực tiếp `BattleVfxPlayer.ContactOccurred` và chỉ nhận `ContactKind.Ground`.
Các cue `body_fall` / `knockout_fall` đã có trong timeline của từng animation phát event này.
Vị trí X/Z lấy đúng điểm tiếp đất; mặt hiệu ứng đặt cao hơn sàn 0,018 m để tránh nhấp nháy độ sâu.

- Vết nứt dạng phân nhánh, viền sáng và vòng nứt trung tâm; xoay khác nhau ở mỗi lần va chạm.
- Mảnh đá 3D ít polygon bật lên theo lực và trọng lực, xoay rồi thu nhỏ khi hết thời gian sống.
- Giữ bụi, vòng va chạm và âm thanh có sẵn của timeline; không phát thêm một bản âm thanh trùng.
- Lần tiếp đất sau KO vẫn chạy VFX: component độc lập với khóa phản ứng thắng/thua của crowd.
- Chống phát lặp cùng eventId; chỉ phát trong giới hạn sàn chiến đấu.
- Bốn slot có sẵn trong Balanced/High, ba slot trong preset WebGL; không Instantiate khi đang đánh.
- Không có Rigidbody hoặc collider, không thay đổi mặt đất vật lý hay chuyển động nhân vật.
- Vết nứt giữ 4 giây rồi mờ trong 1,25 giây; reset combat dọn hiệu ứng về pool.

Inspector của stage settings có `enableFloorFracture` và `fractureRadius`.
Regenerate để đưa thay đổi cấu hình vào prefab mới; lifetime có thể chỉnh trên component instance.

## Kiểm tra

`PrefabValidation.txt`: tham chiếu, material/shader, collider, roster và các slot vỡ sàn.
`Review/ProductionPlayValidation.txt`: Play mode với animation thật, đối chiếu eventId, frame tiếp đất,
vị trí X/Z, số burst và thời gian thu hồi pool.
Ảnh thực tế lưu dưới `Review/ProductionBattle*.png` và `Review/ProductionFloorFracture.png`.

Phiên bản trước thay đổi và scene backup được giữ lại trong `Generated/` và `Review/Backups/`.
Đã kiểm tra hướng mặt của cả sáu tam giác mesh mảnh đá để hiển thị đúng culling và ánh sáng URP.
`Review/EditorScenePreservation.txt` đối chiếu LoadingScene đang mở với bản sao chưa lưu trước Play;
test harness phục hồi dấu trạng thái chưa lưu khi quay lại Editor.
