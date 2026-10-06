# SFX cho BattleScene

Mở **Preview.html** bằng trình duyệt để nghe từng file, lọc nhóm âm và chỉnh âm lượng nghe thử. File này dùng trực tiếp WAV trong `Assets/Universal Sound FX`; cần giữ trong vị trí hiện tại để đường dẫn hoạt động.

`BattleSfxSelection.json` chứa danh mục theo sự kiện, đường dẫn, GUID Unity, độ dài, mức volume và khoảng pitch đề xuất. Trong Unity có thể tìm theo tên WAV ở Project rồi nghe trong Inspector, hoặc kéo file gốc vào trường AudioClip.

## Sử dụng trong scene

`BattleSfxBank.asset` là asset dùng khi chạy game. `GameManager` và hai `CharacterCombat` trong BattleScene tham chiếu cùng một `BattleSfxPlayer` trên GameManager. Mở BattleScene, bấm Play rồi dùng **Q** (light bên trái) hoặc **E** (heavy bên phải) để thử. Lượt đánh nhận từ WebSocket đi qua cùng cơ chế phát SFX.

Trên **GameManager → Battle Sfx Player**:

- **Master Volume** chỉnh âm lượng tổng; mặc định 0.85.
- **Enable Announcer** bật tiếng Fight ở lượt đánh đầu và Victory khi nhận kết quả; mỗi âm chỉ phát một lần cho mỗi trận.
- **Enable Hurt Voices** bật tiếng rên khi trúng đòn; mặc định tắt.
- **Enable UI Sounds** bật âm click của các Button đã gán trong scene; mặc định bật.

Trong **BattleSfxBank → Groups**, chỉnh volume/pitch hoặc các clip của từng nhóm. `clipGains` cân peak giữa biến thể mà không sửa WAV gốc. Trong **Moves → Cues**, chỉnh `seconds` theo giây trên source attack/reaction, không phải normalized time. Mốc `finalLanding` đổi sang `knockout_fall` khi bên nhận đòn hết máu. GetUp chỉ phát nếu còn sống và thực sự vào pha đứng dậy.

Clip UI nào bị Unity từ chối import sẽ có bản PCM tương thích trong `Clips/`, thêm im lặng tới 0.1 giây. Hiện `UI_Click_Smooth_mono.wav` dùng bản này; danh mục và trang nghe thử vẫn trỏ về nguồn gốc.

Các nhóm footsteps, metal_clash, shield_block và blade_cut được giữ sẵn trong bank nhưng chưa được gọi tự động: pool đòn hiện tại không có logic bước chân theo vật liệu hoặc đỡ bằng vũ khí. Lựa chọn ban đầu dựa trên nhãn pack và WAV; mốc chiến đấu được đối chiếu thêm bằng hình cùng dữ liệu source pose 30 Hz. Volume/pitch vẫn có thể tinh chỉnh khi nghe trong scene.

## Ghép với các đòn hiện có

| Đòn / sự kiện | Khi vung | Khi trúng người | Khi ngã / đứng dậy |
| --- | --- | --- | --- |
| Light_1, Light_3 | `light_swing` nếu có vung tay/chân | `light_hit` ở lần tiếp xúc; đòn vật dùng tiếng ngã ở điểm rơi | `body_fall`, sau đó `getup` nếu còn sống |
| Heavy_5 — WarriorShield | `blade_swing` khi vung kiếm | `light_hit`; `heavy_hit` khi đập bằng khiên; tùy chọn `blade_cut` khi chém | `body_fall`, `getup` |
| Heavy_6 — GreatSword | `heavy_swing` | `heavy_hit`; tùy chọn thêm `blade_cut` nhỏ | `body_fall`, `getup` |
| Heavy_7 — Spear | `thrust_swing` | `stab_hit` | `body_fall`, `getup` |
| Heavy_2 — TwoHandedAxe | `heavy_swing` | `heavy_hit`; tùy chọn thêm `blade_cut` nhỏ | `body_fall`, `getup` |
| Heavy_8 — DualDaggers | `blade_swing` hoặc `thrust_swing` theo nhát | `light_hit` cho chém, `stab_hit` cho đâm; tùy chọn `blade_cut` | `body_fall`, `getup` |
| Hết máu | Theo đòn kết liễu | Theo điểm tiếp xúc của đòn | `knockout_fall` thay `body_fall` tại cùng điểm rơi; không GetUp |

Các nhóm `metal_clash` và `shield_block` là dự phòng cho cảnh thật sự có vũ khí va nhau hoặc đánh vào khiên. Bên nhận đòn hiện không cầm vũ khí, nên không dùng chúng thay tiếng va chạm thân người. Pack không có nhóm giáo riêng; `ARROW_Hit_Body` là lựa chọn thay thế cần nghe thử cho đòn đâm.

## Cách dùng bộ đã chọn

- Bắt đầu bằng các nhóm chính: whoosh, hit, body_fall, getup và knockout_fall. Thêm voice, footsteps, UI hoặc gore sau khi nghe thử.
- Mỗi sự kiện chọn một biến thể, tránh lặp lại cùng file ngay liên tiếp. Không phát toàn bộ biến thể trong nhóm cùng lúc.
- Phát hit tại điểm tiếp xúc và fall tại điểm chạm đất. Không phát tất cả tiếng ngay lúc bắt đầu animation.
- Giữ whoosh và fabric nhỏ hơn hit; thông số volume là tuyến tính 0–1. Volume thấp không thay thế bước cân âm giữa các file.
- Không gộp thêm whoosh vào một clip đã có cả swing và hit. Bộ chính ưu tiên các file tách riêng để dễ canh thời điểm.
- Giọng Fight/Victory chỉ phát một lần cho mỗi trận/kết quả; các gói WebSocket lặp lại không được tạo thêm tiếng.
- Với nhánh chết, tiếng rơi phát một lần; giữ tư thế chết không được lặp âm và không phát getup.

## Đồng bộ và kiểm tra

Battle dùng `FrankBattlePairPlayback` để lấy mẫu attack/reaction theo một đồng hồ chung và tắt Animator hiển thị trong đoạn source motion. SFX bám `SampleTime` của pair và dùng con trỏ mốc tăng một chiều, nên frame bị chậm, cùng thời điểm bị đánh giá lại hoặc seek ngược không phát lặp âm. Không phụ thuộc Animation Event trên Animator đang tắt. Gọi `EvaluateAt` để xem trước pose không tự phát tiếng.

Sau source reaction, `BeginSourceGetUp` kích hoạt lớp fabric phục hồi đúng một lần. Hủy sequence hoặc reset trận dừng âm đang phát và bỏ các mốc còn lại. Pool AudioSource 2D dùng chung cho scene giúp âm lượng không phụ thuộc khoảng cách camera. Random biến thể âm dùng luồng riêng, không ảnh hưởng random chọn đòn.

Menu **Tools → Battle → Install selected SFX** dựng lại bank/reference theo dữ liệu trong `FrankBattleSfxSetup.cs` (sẽ thay các mốc đã chỉnh tay trong bank). Các Editor jobs `ValidateBattleSfx` và `BattleSfxPlayCheck` kiểm tra callback/GetUp/death, phát lặp, hủy lượt và AudioSource trong Play Mode; báo cáo nằm trong `Temp/FrankRetarget/`.
