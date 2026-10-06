Đã thêm mode test animation vào `Assets/Scenes/BattleScene.unity`.

Cách dùng trong Play Mode:

1. Bấm **ANIMATION TEST [F8]** ở góc dưới trái, hoặc nhấn **F8** khi Game view có focus.
2. Bảng lấy tất cả cặp từ pool Light/Heavy của hai nhân vật. Hiện có **22 cặp**, mỗi phía 2 Light + 9 Heavy; gồm cả Katana, Assassin, Archer và White Mage. Bấm **ALL / LIGHT / HEAVY / LEFT / RIGHT** để lọc; dùng cuộn chuột để xem hết.
3. Bấm một hàng để phát đúng attack + reaction + get-up. Bảng tự thu lại để xem nhân vật, VFX và âm thanh rõ hơn. Bấm **LIST [F8]** để mở lại.
4. **REPLAY** phát lại cặp vừa chọn; **NEXT** chuyển cặp tiếp theo; **PLAY ALL** chạy lần lượt danh sách đang lọc; **STOP** hủy preview và đưa cả hai về vị trí đầu.
5. Bật **KO PREVIEW** trước khi chọn/phát lại để thử kết liễu, pose chết, chữ KO và slow motion. Bảng KO chờ cả anim và slow motion/recovery hoàn tất mới hiện; Play All chờ bảng xong trước khi chuyển cặp. Khi tắt KO, tổng damage test là 30% HP; đòn nhiều hit chia damage theo contact thực tế. Mỗi preview bắt đầu đầy HP.
6. Bấm **EXIT TEST [ESC]** để trở lại Battle. HP, vị trí và clock trước khi vào mode được phục hồi. Event server đến trong lúc test chờ trong queue và được xử lý sau khi thoát; preview chỉ thay đổi HUD test.

Toàn bộ chữ của bảng trong game là tiếng Anh. Panel hoạt động trong Editor và Development Build. Muốn bật ở release build, chọn object **Battle Animation Test** và bật **Enable In Release Builds** trong Inspector. **Open On Start** cho phép mở mode ngay khi vào Play.

Danh sách đã lắp: [InstalledPairs.txt](InstalledPairs.txt). [Ảnh bảng](PairBrowser.png), [KO Preview sau khi kết liễu của Archer](../BattleKoTimingReview/KoAfterFinisher.png). [Kiểm tra timing KO mới](../BattleKoTimingReview/README.md).

Kiểm tra native Unity [đã qua](PlayValidation.txt): bấm bằng pointer, cả năm bộ lọc, chọn đúng cặp, tự thu bảng, Stop giữa animation, Replay/get-up, Play All đủ 22 cặp, Next quay vòng, Archer KO ba hit, thoát phục hồi HP/vị trí, event server tiếp tục sau test và disable phục hồi trạng thái pause trước đó. Đã đối chiếu 19.601 frame lấy mẫu với timeline trừ HP. Kiểm tra bổ sung [cleanup KO](CleanupValidation.txt) xử lý cả trường hợp UI con đã bị hủy khi thoát scene.

Góc, vị trí gốc và lens của camera được giữ nguyên ([đối chiếu serialized component](CameraUnchanged.txt)); mode dùng chính playback, positioning, SFX/VFX và hit timeline của Battle.
