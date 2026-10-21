# Meme Colosseum — hướng dẫn tích hợp

Bộ dựng dành cho project ZeroSpace, Unity 6000.4.0f1 / URP 17.5.
Các asset mới nằm trong `Assets/MemeArena`. Sau khi được duyệt ngày 18/10/2026,
`Assets/Scenes/BattleScene.unity` đã dùng Meme Colosseum làm sân chính.
Script combat, camera, UI, mạng, render pipeline và build settings được giữ nguyên.
Mở BattleScene rồi Play để dùng sân mới; LoadingScene vẫn tải chính scene này như trước.
Ngày 19/10/2026 đã cập nhật khán giả sang 16 meme và thêm hiệu ứng vỡ sàn theo contact animation.
Bản mở rộng từ workbook ngày 20/10 có 159 loại khán giả: giữ 16 loại cũ và thêm 143 hình mới.
Danh mục và nguồn: `Documentation/RobinhoodMemeCatalog.md`; art gốc, atlas và cấu hình sàn:
`Documentation/MemeCrowdAndFloor.md`. Kết quả import được ghi trong `Documentation/Review/WorkbookImport.txt`.

## Mở và dùng

Tên hiển thị hiện tại là Cash Cat và Ninja Cat; ID/prefab/anim vẫn giữ tham chiếu cũ.

1. Mở `Tools > Meme Arena > Generate Test Arena`.
2. Chọn hoặc tạo `MemeArenaSettings`, sửa kích thước sân, bán kính, số tầng ghế,
   mật độ khán giả, số fan gần, cường độ phản ứng, art, ánh sáng và chất lượng.
3. `Generate Arena` lưu **một phiên bản prefab mới** trong `Assets/MemeArena/Generated`.
4. Scene tích hợp hiện tại được ghi trong `Documentation/LatestBattleScene.txt`.
   Sau khi áp dụng chính thức, file này trỏ tới `Assets/Scenes/BattleScene.unity`.
   Các bản sao scene thử trước đó vẫn được giữ trong các thư mục `Generated/Colosseum_*`.
5. Các phím thử đòn của project vẫn giữ nguyên. Không cần kết nối BE để thử local trong Editor.

Prefab mới nhất: `Documentation/LatestGenerated.txt`.
`Regenerate Arena` và `Populate Meme Crowd` tạo bản mới, giữ asset và tham chiếu art của bản cũ.
Công cụ không tự ghi đè prefab hoặc scene có sẵn.

## Cấu trúc

- Sân chiến đấu 3D có mặt trên Y=0, không có collider mới.
- Khán đài vòng cung bằng mesh 3D, hai tầng mái vòm, cột, lan can, cờ đỏ–vàng.
- Tượng Doge Emperor hiện là **placeholder 3D dựng bằng primitive**, có socket thay prefab.
- Fan gần dùng mesh billboard hoặc prefab tùy chọn, do một manager điều khiển ở 30 Hz.
- Fan xa gom trong ba mesh và chuyển động/phản ứng bằng shader, dùng chung texture atlas.
- Hàng khán giả xa cũng dùng atlas meme, ghép trong mesh của ba phe; vòm trời dùng ảnh tĩnh.
- Vật liệu cel shading, viền ink mảnh, key light ấm, soft shadow, bloom nhẹ.
- Renderer hiện tại không bật SSAO; công cụ không sửa renderer để thêm feature đó.

## Crowd settings

Roster mở rộng có 159 meme, gồm bộ 16 loại Pepe, Doge, Cheems, Popcat, Chill Guy, Pibble
và các nhân vật trong hai sheet đã lọc của workbook. Mỗi nhân vật có ít nhất một chỗ trong sân mặc định.
Các tên trùng dùng chung hình; toàn bộ 154 dòng nguồn và các ticker/tham chiếu vẫn được giữ riêng trong JSON.
Mỗi entry cho phép sprite, prefab và trọng số xuất hiện. Có thể thêm loại meme khác.
Sprite mới được tạo bằng công cụ image_gen tích hợp, một hình cho mỗi loại: idle/jump/cheer/boo/victory/defeat
là chuyển động procedural. Các settings trống vẫn có bộ placeholder import để dự phòng.
Chín ảnh nguồn trong `Art/Robinhood20261020/` được cắt theo alpha và đóng thành một atlas tối đa 2048px.
Roster chỉ tham chiếu atlas đã đóng gói, không tải cả chín tấm nguồn khi chạy game.
Prompt được lưu tại `Documentation/RobinhoodPrompts/`; hình là minh họa comic, không phải art chính thức.
Prefab tùy chọn có thể thêm Animator với các state `Idle`, `Jump`, `Cheer`, `Boo`, `Victory`, `Defeat`.
Fan gần có phase, tốc độ và tỷ lệ ngẫu nhiên theo seed; không dùng Random toàn cục của gameplay.
LOD xa của prefab dùng sprite đi kèm; nếu không có sprite nào sẽ dùng Ape placeholder.

Ba nhóm `FighterA`, `FighterB`, `Neutral` có thể sửa trong Inspector.
Fighter A = nhân vật trái / bot_a; Fighter B = nhân vật phải / bot_b.
Loại meme không quyết định fan thuộc phe nào.

Chọn stage instance trong scene rồi bấm `Preview Crowd Reactions` để xem lần lượt các phản ứng.
Preview Editor kết thúc sau 12 giây và trả transform về vị trí cũ.

## Kết nối combat

`MemeArenaCombatAdapter` nằm trên prefab; scene tích hợp gán GameManager/camera hiện có.

- `BattleVfxPlayer.ContactOccurred`: cổ vũ đúng thời điểm va chạm.
- `SequenceBegan`: bắt đầu đếm contact của đòn; hai contact trở lên tạo phản ứng combo.
- Heavy contact: phản ứng mạnh theo ý nghĩa critical đang dùng trong game.
- `CharacterCombat.SequenceEnded` và contact cuối lethal: xác định KO, có chống phát trùng.
- `Slider.onValueChanged`: phản ứng khi HP xuống ngưỡng thấp và khi winner không có contact lethal.
- Thay đổi nhãn lượt của RoundManager: cổ vũ bắt đầu lượt theo giao diện đang được GameManager phát.

Game hiện không có event C# riêng cho RoundStarted / RoundVictory / CriticalHit từ BE.
Adapter cung cấp `NotifyRoundStart()`, `NotifyVictory(bool fighterA)`, `NotifyCritical(bool fighterA)`
để nối bằng UnityEvent hoặc một event adapter khác về sau, không cần sửa combat.
Adapter không đọc, lấy ra hay phát lại các message trong hàng đợi WebSocket.

Confetti dùng hai ParticleSystem có sẵn, tái sử dụng ở mép viewport và không có collider.
`enableVfx` / `enableConfetti` trong settings có hiệu lực khi tạo phiên bản prefab mới.

`MemeArenaFloorFracture` nhận ground contact độc lập với phản ứng crowd: nứt sàn đúng điểm rơi,
mảnh đá văng lên, rồi vết nứt mờ đi và trở về pool. Bụi và âm thanh dùng lại timeline sẵn có.
Settings có `enableFloorFracture` và `fractureRadius`; không thay đổi collider hay mặt sàn vật lý.

## Chất lượng và hiệu năng

- WebGL: tối đa 24 fan gần, không dựng viền ink phụ, tối đa 32 hạt confetti mỗi bên.
- Balanced: tối đa 48 fan gần mặc định, mesh kiến trúc gộp theo material, atlas dùng chung.
- High: cho phép nâng giới hạn fan gần trong Inspector.
- Mật độ và chiều cao khán giả, kích thước geometry: regenerate để áp dụng thay đổi.
- Không có Update riêng trên mỗi fan; manager cập nhật transform gần, shader chạy fan xa.
- Mesh và material được chia sẻ; VFX transient có pool sẵn.

Mục tiêu là 60 FPS. Kết quả kiểm tra Editor không chứng minh 60 FPS trong WebGL/player build.
Cần profile một build chạy thật trên thiết bị và browser đích trước khi chốt ngân sách.

## Kiểm tra và tài liệu

- `Documentation/FoundationValidation.txt`: nền tảng / mesh normals / tham chiếu project.
- `Documentation/PrefabValidation.txt`: prefab, collider, mesh, shader, ngân sách fan.
- `Documentation/Review/PlayValidation.txt`: lần kiểm tra Play mode gần nhất.
- `Documentation/Review/WorkbookValidation.md`: kết quả của bản mở rộng danh mục workbook.
- `Documentation/ValidationReport.md`: báo cáo tổng hợp, số đo Editor và giới hạn kiểm tra.
- `Documentation/ChangeSummary.md`: danh sách source/asset được thêm hoặc sửa.
- `Documentation/Review/*.png`: ảnh dựng và ảnh Play mode thực tế.
- `Documentation/PreservedProject.before.json`: hash đối chiếu file production được giữ nguyên.
- `Documentation/ImportedPackage`: bản sao ba script import ban đầu, lưu dạng `.txt`.
- `Documentation/UserTask.md`: tài liệu yêu cầu gốc.

Map đã được áp dụng vào BattleScene theo phê duyệt của người dùng.
Bản sao scene trước thay đổi nằm tại `Documentation/Review/Backups/`.
Kết quả tích hợp: `Documentation/Review/ProductionIntegration.txt`,
`ProductionPlayValidation.txt` và `ProductionPreservation.json`.
Các thay đổi production khác tiếp tục theo phạm vi duyệt trong tài liệu gốc.
