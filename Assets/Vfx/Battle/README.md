# VFX cho BattleScene

BattleScene dùng chung `BattleVfxPlayer` trên GameManager cho Mankey và Pepe. Chạy scene và bấm **Q** để thử Light bên trái hoặc **E** để thử Heavy bên phải; các lượt nhận từ WebSocket cũng dùng cùng timeline.

| Mốc animation | Hiệu ứng |
| --- | --- |
| `light_swing` | WHOOSH nhỏ ở tay/chân đang chuyển động nhanh nhất |
| `blade_swing`, `heavy_swing` | Vệt chém tại vũ khí đang vung, xoay theo hướng chuyển động; dao đôi dùng kích thước nhỏ hơn |
| `thrust_swing` | WHOOSH ở vũ khí đang đâm |
| `light_hit`, `stab_hit` | MageLightHit: Light hit của White Mage, vòng sáng xanh/trắng + POW |
| `heavy_hit` | ArcherHeavyHit: ArrowHit của Archer, tia sáng xanh/trắng + WHAM; tắt lớp mũi tên |
| `body_fall` | Bụi và MageGroundImpact sát nền; Magic rain hit 2 của White Mage tạo vòng sáng vàng, lần rơi cuối thêm SMASH |
| KO | Cùng mốc rơi, tăng nhẹ kích thước bụi/impact/SMASH |

Ưu tiên Cartoon FX Remaster cho chữ comic và bụi; vệt chém dùng Sword Slash 3 của Hovl, xoay mặt cung chém về camera và tắt lớp distortion. Các prefab battle dùng vật liệu riêng trong `Materials/`. Particle không loop, không tự phát lúc mở scene và không tự hủy object trong pool. WHOOSH chạy nhanh hơn để không lưu quá lâu giữa các nhát combo.

Đã khảo sát 66 prefab của White mage spells và Archer skills pack, render 12 ứng viên rồi chọn ba hiệu ứng trên. Bản battle giữ nguyên source pack, bỏ âm thanh và script demo, tắt distortion. Vật liệu trong `Materials/NewPacks` có hậu tố GUID để tránh trùng tên. MageLightHit dùng scale 0.43/speed 1.35; ArcherHeavyHit 0.40/1.35; MageGroundImpact 0.50/1.70. Các trail phát liên tục được rút xuống 0.22 giây, giới hạn 96 particle/system. Chữ POW/WHAM dùng các particle ký tự có sẵn, bỏ script tái tạo chữ để giữ cấu hình pool.

Thời điểm lấy từ `Assets/Audio/Battle/BattleSfxBank.asset`, đơn vị giây trên source animation. VFX dùng cùng đồng hồ với SFX; chỉnh `Moves → Cues → seconds` nếu cần đổi nhịp cả tiếng lẫn hiệu ứng. Khi frame đi qua nhiều mốc, player lấy pose đúng từng mốc rồi trả animation về thời gian hiện tại. Vệt chém theo vũ khí trong 0.1 giây đầu. Chữ comic quay về camera Battle và được đưa ra trước thân nhân vật để đọc rõ.

Chỉnh tổng kích thước bằng **GameManager → Battle Vfx Player → Effect Scale**. `Ground Height` là độ cao mặt sàn, mặc định 0. Pool giới hạn 32 instance và được dọn khi hủy lượt hoặc reset trận.

Các menu:

- **Tools → Battle → Install battle VFX**: dựng lại tám hiệu ứng, gồm ba lựa chọn mới, và gán reference trong BattleScene.
- **Tools → Battle → Apply selected White Mage and Archer VFX**: chỉ dựng và gán ba hiệu ứng thay thế.
- **Tools → Battle → Survey White Mage and Archer VFX**: khảo sát source, render ứng viên tại ba thời điểm.
- **Tools → Battle → Validate updated pack VFX**: kiểm tra timeline, cấu hình prefab và ảnh tại mốc va chạm với camera cinematic. Báo cáo và ảnh trong `GeneratedAssets/BattleNewVfxReview`; mở `Review.html` để xem.
- **Tools → Battle → Validate battle VFX**: kiểm tra toàn bộ pool đòn, hai hướng đánh, KO, timeline, vị trí khi frame bị bỏ qua, shader và hủy/reset. Báo cáo: `Temp/FrankRetarget/battle-vfx-validation.txt`; ảnh: `Temp/FrankRetarget/Vfx/`.
- **Tools → Battle → Preview imported slash candidates**: ảnh so sánh các slash từ Cartoon FX, ErbGameArt và Hovl trong `Temp/FrankRetarget/Vfx/Candidates/`.
- **Tools → Battle → Inspect landing VFX rendering**: render bụi và ground impact ở ba thời điểm, kiểm tra pixel tím và lỗi shader; ảnh và báo cáo trong `Temp/FrankRetarget/Vfx/Landing/`.

Material bụi ngã dùng `CFXR Particle Ubershader.shader`. Bản `.cfxrshader` hiện có lỗi biên dịch particle helper trên Unity 6; bộ cài VFX chọn bản `.shader` để tránh tái tạo material tím.
