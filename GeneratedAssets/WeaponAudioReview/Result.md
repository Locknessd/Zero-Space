Đã áp dụng hai pack vào bank đang dùng trong `Assets/Scenes/BattleScene.unity` cho cả Mankey và Pepe.
Chọn 37 clip: 24 từ Sword Sounds Pro và 13 từ Magic Sci-Fi Sword Sounds.
Các bản playback dùng mono PCM 44,1 kHz, dài 0,2–0,48 giây, tổng khoảng 1,15 MB.
Clip được cắt phần đầu theo waveform, cân mức tín hiệu và fade đuôi để dùng trong combo nhanh.
File gốc của hai pack được giữ nguyên; đường dẫn gốc và thông số chỉnh nằm trong [Selections.csv](Selections.csv).

| Vũ khí/động tác | Attack | Hit |
| --- | --- | --- |
| Warrior/Sword, Katana, GunSword | Sword Woosh; thrust ngắn từ Dagger Woosh | Fantasy Dagger Cut/Stab |
| GreatSword | Heavy Sword Woosh | Dark Sword Hit, thêm lớp trọng lượng |
| TwoHandedAxe | Heavy Sword Woosh với pitch thấp | Dark Sword Hit, thêm lớp trọng lượng |
| Spear | Sword Woosh gọn với pitch thấp | Cut/Stab theo loại va chạm |
| DualDaggers, Assassin | Dagger Woosh và lớp Magic Whoosh Fast nhỏ | Fantasy Dagger Hit/Cut/Stab |
| Shield bash | Whoosh rộng | Club hit cho va chạm cùn |
| Va chạm vũ khí/khiên | Theo cue hiện có | Sword hit another sword / Sword hit shield |

Các cue của animation/VFX/điểm hit được giữ nguyên. Âm hit được kích hoạt qua callback va chạm của VFX.
Nguồn Gun, chân và tay dùng nhóm âm riêng hiện có, kể cả hai phát đầu `combo_02`.
Hurt voice vẫn theo loại hit ban đầu, kể cả khi âm kiếm chuyển sang nhóm mới.

Kiểm tra:

- Biên dịch runtime và Editor thành công.
- [Bindings.txt](Bindings.txt): đủ 8 hệ vũ khí, clip hợp lệ, preloaded, đúng bus mixer và binding BattleScene.
- [Validation.txt](Validation.txt): 208/208 trường hợp cho 52 cấu hình Light/Heavy, có/không kết liễu,
  phát từng cue hoặc bỏ khung hình; 448 lượt chọn hit vũ khí, 56 phát súng; không lặp âm hoặc phát sau khi hủy đòn.
- Đối chiếu dữ liệu: timeline animation/VFX/contact và file BattleScene không đổi;
  sound súng, cơ thể, UI, getup và announcer vẫn giữ nguyên.

Việc chọn clip dựa trên nhóm âm trong pack, waveform onset/peak/RMS và loại động tác.
Môi trường này không hỗ trợ nghe trực tiếp audio; chưa đánh giá chất âm qua loa/tai nghe hoặc mix trong Play Mode.

Unity bị crash ở bước reload script, trước khi gán bank; Editor đã được mở lại và kiểm tra hoàn tất.
Bản phục hồi tự động nằm tại `Assets/_Recovery/0 (10).unity`.
Bản snapshot trước crash cũng được giữ tại `.git/codex-sword-audio/20261009/OpenScene.before-crash.unity`.
