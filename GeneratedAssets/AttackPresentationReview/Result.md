Đã thêm `combo_01`, `combo_02`, `combo_03` vào Heavy của Mankey và Pepe, dùng toàn bộ clip attack/reaction gốc.
Mỗi nhân vật hiện có 10 Light và 16 Heavy. Các đòn cũ được giữ nguyên.

Đã sửa ba material dùng shader Standard sang shader URP của battle, giữ texture và màu:

- `InsaneCombos_02 - Default.mat`
- `InsaneCombos_Material #17123165.mat`
- `Samurai Executions/Material/PlayerA.mat`

Ba combo có timeline riêng cho từng nhân vật: vệt kiếm, cú đá, chớp súng, hit và chạm đất.
Đã chỉnh anchor hit của Mankey/Atemi3 và vệt rút kiếm của GreatSword/Execution1.
Grounding được lấy mẫu ở 240 Hz trên avatar dùng trong BattleScene.

Đã bổ sung hai phát súng mở đầu `combo_02` tại 0,4 s và 0,5667 s cho cả Mankey và Pepe.
Mỗi phát có chớp súng và hit riêng; combo hiện có đủ ba phát súng và bảy hit.
[Kiểm tra riêng combo_02](Combo2Gunfire.txt) đạt 16/16 trường hợp với hai hướng, có/không kết liễu,
phát theo từng cue hoặc bỏ qua toàn bộ khung hình. Kiểm tra bỏ khung hình của cả ba combo cũng đã chạy lại, đạt 12/12.
Ảnh [combo_02_vfx.png](combo_02_vfx.png) đã cập nhật với hai phát đầu.

| Kiểm tra trong Unity Editor ở lượt rà soát đầu | Kết quả |
| --- | --- |
| Toàn bộ Light/Heavy, hai nhân vật, hai hướng, có/không kết liễu | 208 trường hợp, 0 lỗi |
| Material của vũ khí và VFX đang phát | 19.625 lượt kiểm tra slot, không thiếu hoặc lỗi shader |
| Vệt vũ khí bám mesh | 104 trường hợp, 236 vệt, 1.132 đối chiếu đầu vệt |
| Ba combo khi bỏ khung hình và hủy đòn | 12 trường hợp, không mất VFX, sai lệch vị trí 0 m |
| Biên dịch runtime và Editor | Thành công |
| Đối chiếu scene và dữ liệu đòn cũ | Thành công |

Chi tiết: [Validation.txt](Validation.txt), [FrameSkips.txt](FrameSkips.txt),
[Preservation.txt](Preservation.txt), [vệt vũ khí](../WeaponVfxAlignmentReview/Validation.txt).
Các ảnh `*_vfx.png` ghi lại VFX tại thời điểm cue. Ảnh `*_source.png` là bản khảo sát trước khi sửa material.

Các kiểm tra chạy trên scene preview trong Unity Editor; chưa chạy một trận Play Mode với server.
