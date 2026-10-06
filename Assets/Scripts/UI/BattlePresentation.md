# Chữ UI và KO trong BattleScene

Các nhãn chính dùng `UnityEngine.UI.Text` và DOTween. `BattleUiTextEffects` quản lý hiệu ứng hiện chữ thoại, popup sát thương, nhấn số máu/tên/lượt; giá trị HP và thanh máu vẫn cập nhật ngay. Khu vực thông tin lượt chỉ hiện `Turn X`; bộ đếm giờ, hệ số và tổng phiếu chứa mã `turn_x_x` được ẩn trong BattleScene.

Bong bóng thoại dùng nền kem, góc bo, đuôi, bóng đổ và viền xanh/đỏ theo nhân vật. `BattleSpeechBubbleGraphic` vẽ mesh UI nên vẫn sắc nét khi đổi độ phân giải. `BattleSpeechBubble` tính chiều cao từ toàn bộ nội dung trước khi chạy hiệu ứng gõ chữ; tự xuống dòng và ẩn khi nội dung rỗng. Chữ sát thương dùng `FighterSlot.damageText` riêng, nằm ngay trên thanh máu, nảy nhẹ rồi bay lên/mờ dần. Thoại mới hoặc việc xóa thoại không ghi đè popup sát thương; popup mới hủy bộ đếm cũ và tự dọn sau 1.15 giây, kể cả lúc `timeScale = 0`.

`BattleHudCallouts` bám vị trí thực của thanh HP giữa hai Canvas. Các nhãn sát thương nằm trong anchor riêng, nên thay đổi layout không cản tween bay lên. Bảng thoại không chặn input; lớp KO luôn nằm phía trên. Các scene chưa có `damageText` giữ cách hiển thị cũ.

Đuôi bong bóng nằm ở góc trên bên avatar, chĩa lên và ra ngoài. BattleScene đã gỡ `SpeechBubble_TrumpVictim` cùng hai panel `ScoreCyan`/`ScoreRed` cũ và xóa liên kết `memeResultObject`/`memeResultText`. Khi nhận `ARGUMENT_SELECTED`, thoại đi vào bong bóng HUD; `ShowMemeResult` chỉ dùng popup cũ ở scene chưa có bong bóng HUD, nên không bật thêm bản thoại trùng.

`MemeBattleUI.UpdateHealth` gửi HP cho `BattleKoPresentation`. Khi HP bằng 0, KO chờ `CharacterCombat.IsBusy` kết thúc rồi hiện một lần mỗi trận. Bảng KO dùng chữ vàng/đỏ, viền đen, bóng đỏ, flash và tia va đập tạo bằng mesh UI. Không cần texture hay shader VFX riêng, không chặn input.

Nếu server chỉ gửi `WINNER_DECLARED`, GameManager đồng bộ HP của nhân vật thua về 0 khi đánh dấu chết, nên KO vẫn hoạt động mà không cần sự kiện damage riêng.

Animation chạy bằng thời gian không phụ thuộc `Time.timeScale`. Trận mới hoặc `GameManager.ResetCombatQueue` hủy tween và dọn hiệu ứng đang hiển thị. Chỉnh thời gian giữ KO ở **Battle Text Presentation → KO → Hold Seconds**.

Menu **Tools → Battle**:

- **Install animated text and KO**: dựng lại `Battle Text Presentation` và lưu các reference vào BattleScene.
- **Style chat bubbles and HP damage numbers**: dựng hai bong bóng và hai nhãn sát thương riêng; gỡ các panel thoại cũ cùng liên kết bật lại chúng.
- **Remove legacy chat bubbles**: gỡ các panel thoại cũ và liên kết có thể bật lại chúng trong BattleScene.
- **Preview styled chat and HP damage numbers**: render chữ tiếng Việt và popup ở 1280×720, 1920×1080, 1024×768; ảnh và báo cáo trong `GeneratedAssets/BattleChatUiReview`.
- **Preview text animations and KO**: ảnh chữ UI, KO lúc va đập/giữ và KO màn hình dọc trong `Temp/FrankRetarget/BattleUi/`.
- **Check animated text and KO in Play Mode**: chạy bản sao Battle không kết nối server, kiểm tra HP 0, hai bên chết, sự kiện trùng, reset và animation khi `timeScale = 0`; trả editor về scene ban đầu sau khi xong.

Q/E chỉ thử animation local. KO trong trận thật được kích hoạt bởi cập nhật HP về 0 từ luồng dữ liệu trận đấu.

DOTween 1.3.030 được nhập từ [Demigiant](https://dotween.demigiant.com/download.php) vào `Assets/Plugins/Demigiant/DOTween/`. Giữ nguyên readme và thông tin tác giả của gói; [license](https://dotween.demigiant.com/license.php).
