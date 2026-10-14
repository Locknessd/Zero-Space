# FlatKit URP trong game

Shader chính: `FlatKit/Stylized Surface`, lấy từ gói URP có sẵn của FlatKit 4.9.8.
Project dùng Unity 6000.4 và URP 17.5.

- Material của Battle được cập nhật tại chỗ để giữ GUID và liên kết trong scene.
- Prefab Pepe/Mankey và BOM showcase tiếp tục tham chiếu material FlatKit hiện có, đã chuyển sang URP.
  Công cụ chỉ tạo bản sao riêng trong `Assets/Rendering/FlatKitGame/Materials` nếu gặp material nguồn khác.
- Giữ albedo, scale/offset UV, màu gốc, normal, emission và alpha cutout khi chuyển shader.
- Chất liệu toon dùng cel shading, texture multiply và bóng đổ URP.
- Mức sáng đã hạ: RGB màu nền nhân 0.62; RGB vùng bóng nhân 0.56, giữ nguyên alpha.
  Màu nguồn được lưu trong tag của material để chạy lại preset không làm tối dần.
- Không bật outline, post-process hoặc thêm renderer feature mặc định.
- VFX, trail, UI và backdrop có lightmap tiếp tục dùng shader riêng.
- Hit flash hỗ trợ FlatKit bằng property block theo từng material, khôi phục block cũ khi hết flash.

Đã nhập phần shader/HLSL/Inspector cần thiết; không nhập scene/texture demo của gói URP.
`ImportedAssets.json` ghi các asset gốc. `BackupPath.txt` trỏ tới bản sao trước khi tích hợp.
Renderer feature cho outline chỉ là dependency của Inspector; phần `UnityEditor` được bọc điều kiện
và dependency thuộc tính Inspector bên ngoài được bỏ để mã runtime có thể biên dịch cho Player.

Menu Editor:

- `Tools > Battle > Apply FlatKit URP shaders`
- `Tools > Battle > Validate FlatKit URP integration`
- `Tools > Battle > Apply darker FlatKit tone`

Kết quả kiểm tra thực tế được ghi trong `Validation.txt` khi lệnh Validate chạy thành công.
Ảnh `BattleBefore.png` và `BattleAfter.png` được render từ BattleScene ở cùng góc máy.

Kết quả ngày 2026-10-13: cập nhật 34 material hiện có, không cần tạo thêm material showcase.
BattleScene giữ nguyên nội dung file. Kiểm tra shader và flash cho cả hai nhân vật đều PASS;
flash tự hết hạn và phục hồi property block trước đó.

Chưa thực hiện bản build WebGL mới trong lần tích hợp này.
