# Các thay đổi trong MemeArena

Toàn bộ source và asset của package được thêm/sửa trong `Assets/MemeArena/`.
Sau phê duyệt ngày 18/10/2026, `Assets/Scenes/BattleScene.unity` được cập nhật map và ánh sáng.
Ba script import ban đầu được giữ bản sao tại `Documentation/ImportedPackage/`.
Không xóa asset cũ; công cụ generate không ghi đè scene/prefab cũ.
BattleScene chính được lưu lại sau khi có phê duyệt và được sao lưu trước khi áp dụng map.

## Script có sẵn đã hoàn thiện

- `Editor/MemeColosseumGenerator.cs`: cửa sổ tạo arena, settings và thao tác tạo phiên bản mới.
- `Runtime/MemeCrowdFan.cs`: animation procedural, hỗ trợ Animator tùy chọn, không có Update riêng.
- `Runtime/MemeCrowdReactions.cs`: giữ API cũ và chuyển tiếp tới hệ thống phản ứng mới.

## Runtime mới

Bản mở rộng workbook ngày 20/10/2026 thêm 143 hình comic, giữ 16 hình hiện có, tổng 159 loại khán giả.
Giữ 154 dòng nguồn trong JSON; gộp nhận diện hình ảnh trùng mà không gộp dữ liệu dự án/ticker.
`MemeCrowdSettings` bổ sung metadata roster; fan gần vẫn dùng ngân sách cũ và một atlas chung.

- `MemeArenaSettings.cs`, `MemeArenaStage.cs`: cấu hình, tham chiếu và giới hạn không gian combat.
- `MemeCrowdSettings.cs`, `MemeCrowdSpawner.cs`: loại fan, trọng số, seed và tái sử dụng crowd.
- `MemeCrowdGroup.cs`, `MemeCrowdReactionManager.cs`: ba phe độc lập, một lịch tick chung và GPU crowd.
- `MemeArenaCombatAdapter.cs`: đăng ký contact/sequence/HP/nhãn round; chống KO lặp và contact muộn.
- `MemeArenaCelebration.cs`: hai confetti emitter tái sử dụng, đặt tại mép camera.
- `MemeArenaPlayProbe.cs`: probe tùy chọn cho kiểm tra Editor, không được gắn trong prefab production.
- `MemeArenaFloorFracture.cs`: ground-contact adapter và pool vết nứt/mảnh đá.

Các file Runtime nằm trong `Runtime/`.

## Editor mới

- `MemeArenaBuild.cs`: tạo phiên bản prefab mới và gộp mesh theo material.
- `MemeArenaBuild.Architecture.cs`: nền, khán đài cong, mái vòm, cột và lan can.
- `MemeArenaBuild.Decor.cs`: tượng Doge placeholder và cờ, hỗ trợ prefab thay thế.
- `MemeArenaBuild.Materials.cs`: material toon, sky dome và art override.
- `MemeArenaBuild.Crowd.cs`, `MemeArenaBuild.CrowdMesh.cs`: atlas, billboard và crowd mesh theo phe.
- `MemeArenaBuild.Lighting.cs`: ánh sáng, grade nhẹ, lửa và confetti pool.
- `MemeArenaBuild.Fracture.cs`, `MemeArenaBuild.FractureMesh.cs`: tạo pool, mesh nứt và mảnh sàn.
- `MemeArenaBuild.Atlas.cs`, `MemeArenaRosterUpgrade.cs`: dùng chung atlas và tạo roster 16 meme mới.
- `MemeArenaIntegration.cs`: tạo bản sao BattleScene để xem thử với gameplay hiện có.
- `MemeArenaProduction.cs`: áp dụng sân đã duyệt vào BattleScene, sao lưu và đối chiếu dữ liệu gameplay.
- `MemeArenaValidation.cs`, `MemeArenaStageEditor.cs`: validator, Inspector và preview phản ứng.
- `MemeArenaOverview.cs`: chụp ảnh tổng quan bằng camera tạm trong preview scene.
- `MemeArenaPlayCheck.cs`, `MemeArenaPlayCheck.Flow.cs`: kiểm tra Play mode qua API game hiện có.
- `MemeArenaPlayCheck.Floor.cs`: đối chiếu frame/eventId/vị trí ground contact và vòng đời pool vỡ sàn.
- `MemeArenaEditorContext.cs`: đối chiếu scene đang mở với bản sao chưa lưu trước khi thử Play.
- `MemeArenaJobs.cs`: nối các thao tác này vào job bridge Editor sẵn có của project.

Các file Editor nằm trong `Editor/`.

## Shader và asset

- `Editor/MemeCatalogDefinition.cs`, `MemeCatalogImport.cs`: đọc danh mục, tạo roster và áp dụng stage mới.
- `Editor/MemeCatalogSpriteRegions.cs`, `MemeCatalogAtlas.cs`: kiểm tra alpha, cắt sprite, đóng atlas dùng chung.
- `Art/Robinhood20261020/`: chín tấm ảnh nguồn tạo bằng công cụ image_gen tích hợp.
- `Documentation/RobinhoodMemeCatalog.json`, `.md`: danh mục, alias và nguồn của từng nhân vật.
- `Documentation/RobinhoodPrompts/`: prompt của từng tấm ảnh và các chỉnh sửa art nếu có.
- `Generated/WorkbookRoster_*`: atlas tối đa 2048px, 159 sprite asset và cấu hình roster.
- `CrowdPlacement.json` trong stage mới: bằng chứng mọi loại meme có chỗ trong khán đài mặc định.

- `Shaders/MemeStageToon.shader`: cel shading và bóng URP.
- `Shaders/MemeArenaOutline.shader`: viền kiến trúc có thể tắt; preset WebGL không tạo lớp viền này.
- `Shaders/MemeCrowdCard.shader`: atlas cutout, billboard và phản ứng khán giả trên GPU.
- `Shaders/MemeArenaParticle.shader`: lửa/confetti nhẹ.
- `Shaders/MemeFloorFracture.shader`: vết nứt trên mặt sàn, viền sáng và opacity theo vòng đời pool.
- `Art/MemeCrowd2026.png`: atlas 16 meme mới, tạo bằng image_gen nền trong suốt.
- `Art/*.png.meta`: cấu hình import sprite của placeholder được dùng lại.
- `Generated/Colosseum_*`: các phiên bản mới, gồm settings, mesh, material, atlas, volume và prefab.
  Phiên bản tích hợp có thêm bản sao scene `MemeColosseum_Battle.unity`.
- Các file `.meta` tương ứng được Unity tạo cho asset mới.

## Tài liệu và bằng chứng

- `README_VI.md`, `CODEX_PROMPT.md`: hướng dẫn sử dụng và phạm vi bảo trì.
- `Documentation/UserTask.md`: yêu cầu gốc.
- `Documentation/RemainingArt.md`: art còn là placeholder và nhu cầu production.
- `Documentation/LatestGenerated.txt`, `LatestBattleScene.txt`: đường dẫn bản mới nhất.
- `Documentation/FoundationValidation.txt`, `PrefabValidation.txt`: kết quả kiểm tra nền tảng và prefab.
- `Documentation/Review/`: ảnh tổng quan, ảnh Play mode và báo cáo kiểm tra thực tế.
- `Documentation/PreservedProject.before.json`, `PreservedProject.after.json`: đối chiếu file production.

## Phạm vi đã áp dụng vào production

BattleScene chính đã được cập nhật theo phê duyệt của người dùng ngày 18/10/2026.
Map cũ và các root ánh sáng cũ được tắt; thêm instance prefab Colosseum cùng adapter đã gán tham chiếu.
GUID scene, nhân vật, camera, combat, UI và danh sách scene build được giữ nguyên.
Bản sao scene trước thay đổi nằm trong `Documentation/Review/Backups/`.
Ngày 19/10/2026, phiên bản stage mới thay toàn bộ crowd gần/xa bằng meme mới và thêm floor-fracture pool.
SSAO không được thêm vào renderer hiện tại; không thay pipeline hay cài package.
Sprite sheet, tượng và sky production vẫn cần art thay thế như mô tả trong `RemainingArt.md`.
