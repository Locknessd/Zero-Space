# Tối ưu bản build ZeroSpace WebGL

Các thay đổi áp dụng cho hai scene trong Build Profiles: `LoadingScene` và `BattleScene`.
Tài nguyên nguồn và dung lượng tải bản build là hai số khác nhau: Unity chỉ đóng gói tài nguyên được tham chiếu, cộng với nội dung `Resources` và `StreamingAssets`.
[Quy tắc đóng gói tài nguyên của Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/ReducingFilesize.html).

## Cấu hình đã áp dụng

- Chuyển `Resources` của Hovl Studio, demo Flipbook VFX và FightingUnityChan Asset10 sang `DemoAssets`. Các GUID được giữ nguyên, nên scene, prefab và material vẫn tham chiếu được. Những tài nguyên không dùng trong game sẽ không còn tự động đi vào build. Giữ `Resources/Combat`, TextMesh Pro và Resources của BeatEmUp vì chúng có cơ chế tải động.
- Chỉ nén texture 3D bằng override WebGL: chất lượng 90, chọn format theo nền tảng. Giữ độ phân giải của nhân vật và các tòa nhà. Giới hạn texture `TrashProps` và `Lamps` ở 1024 khi chúng đang lớn hơn. Giữ cấu hình của sprite, UI, texture không có mipmap, font và atlas hiệu ứng. Normal map không dùng Crunch.
- Audio giới hạn Quality ở tối đa 80%, giữ mức thấp hơn nếu asset đã được nén trước đó. Ưu tiên override WebGL; khi Editor không chấp nhận override này, dùng Quality mặc định và giữ nguyên codec PCM của các clip ngắn, sample rate, số kênh và thời điểm phát SFX. WebGL xuất audio bằng AAC. Các thiết lập load type không đồng nghĩa với tải âm thanh qua mạng khi cần.
[Audio trên Unity WebGL](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-audio.html).
- Mesh môi trường tĩnh dùng `Low` compression. Giữ rig nhân vật, skinning, mesh Read/Write và dữ liệu animation từ FBX.
- Animation `.anim` đã bake chỉ rút gọn curve có toàn bộ key cùng giá trị và tangent bằng 0, giữ hai đầu curve, thời lượng, wrap mode và event. Các curve chuyển động vẫn giữ nguyên key. Bỏ cache curve dành cho Editor sinh ra khi lưu clip, giữ nguyên phần dữ liệu chuyển động khi bỏ cache.
- Bản release dùng Brotli và bật Data Caching. Giữ Managed Stripping hiện tại để bảo toàn reflection của phần mạng và JSON.

Các file đã đổi có bản gốc tại `BuildOptimization/Originals`. Thư mục này nằm ngoài `Assets`, không được đóng gói vào game. Báo cáo các thay đổi nằm tại `BuildOptimization/OptimizationResult.json`.

## Kết quả audit đã kiểm tra

| Hạng mục trong Assets | Trước | Sau |
| --- | ---: | ---: |
| Tài nguyên có thể được đóng gói | 1.089 | 876 |
| Texture | 328 | 248 |
| Model | 97 | 79 |
| Audio clip | 78 | 78 |

Đã chỉnh cấu hình 73 texture, 78 audio clip, 11 mesh môi trường và rút gọn curve cố định trên 30 animation. Dung lượng nguồn của các animation đã xử lý giảm từ 1.134,67 xuống 1.036,91 MiB, khoảng 97,75 MiB. Các số liệu này không phải dung lượng tải của bản WebGL.

Kiểm tra Unity đã qua 36 trường hợp SFX và kiểm tra combat/queue. Audit trước/sau xác nhận giữ nguyên tham chiếu scene, GUID, độ phân giải nguồn của texture, sample rate/thời lượng audio, số vertex, Read/Write và cấu hình animation FBX. Xem `BuildOptimization/Validation.json`, `CombatFlowValidation.txt` và `CombatSfxValidation.txt`.

Editor hiện tại không chấp nhận override audio WebGL, nên 78 audio clip dùng giới hạn Quality mặc định ở 80%; codec và kiểu tải ban đầu được giữ nguyên. Các clip PCM vẫn dùng PCM khi test trong Editor.

## Dung lượng bản build đã kiểm tra

Bản release Desktop (DXT) tại `Build/WebGL-Desktop` đã build thành công bằng Unity `6000.4.0f1`, với Development Build tắt. Dung lượng đo trực tiếp trên các file đầu ra:

| Thành phần | Dung lượng |
| --- | ---: |
| Dữ liệu game `.data.br` | 102,89 MiB |
| WebAssembly `.wasm.br` | 8,42 MiB |
| JavaScript `.framework.js.br` | 0,08 MiB |
| Loader `.loader.js` | 0,03 MiB |
| Toàn bộ thư mục, gồm template và StreamingAssets | **111,49 MiB** |

Một MiB bằng 1.048.576 byte. Tổng thư mục là 116.900.626 byte; ba file Brotli chiếm 116.804.186 byte. Các số này chưa tính HTTP headers và phụ thuộc những file trình duyệt thực sự tải. Xem chi tiết trong `BuildOptimization/BuildFileSizes.json` và kết quả Unity trong `BuildOptimization/Build-DXT.txt`.

Chưa có bản player build trước tối ưu để đối chiếu, nên không kết luận phần trăm giảm dung lượng build. Bản Mobile ASTC chưa được build hoặc kiểm tra trên thiết bị. Test combat/SFX đã qua trong Editor; chất lượng hiển thị và âm thanh trên trình duyệt cần được kiểm tra theo các tình huống ở cuối tài liệu.

## Build và bàn giao FE

1. Dùng LoadingScene làm scene khởi động. WebGL tự dùng Frontend và đợi FE gọi hàm; Editor tự dùng Client Input để test.
2. Chuyển Build Profiles sang WebGL.
3. Chọn `Tools → WebGL → Build Release Desktop` để tạo `Build/WebGL-Desktop`, dùng texture DXT.
4. Nếu cần bản tối ưu cho điện thoại, chọn `Tools → WebGL → Build Release Mobile` để tạo `Build/WebGL-Mobile`, dùng ASTC. Dùng trên các thiết bị/trình duyệt hỗ trợ ASTC.
5. Gửi toàn bộ thư mục build tương ứng cho FE, gồm `index.html`, `Build`, `TemplateData` và `StreamingAssets`.

Hai bản build dùng cùng game và API FE. FE có thể chọn URL iframe theo khả năng GPU của thiết bị. DXT phù hợp máy tính; ASTC phù hợp điện thoại có hỗ trợ. Nếu GPU không hỗ trợ format của bản build, Unity có thể giải nén texture, làm tăng bộ nhớ và thời gian tải.
[Texture compression cho WebGL](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-texture-compression.html).

Nút build release của công cụ tắt Development Build thông qua `BuildOptions`, nên không mang script debugging hoặc profiler vào bản bàn giao. Có thể build bằng giao diện Unity, nhưng cần chọn đúng texture compression và tắt Development Build.

## Cấu hình host Brotli

Host bản build bằng HTTPS. Server/CDN cần trả header theo đuôi file:

| File | Content-Type | Content-Encoding |
| --- | --- | --- |
| `*.wasm.br` | `application/wasm` | `br` |
| `*.framework.js.br` | `application/javascript` | `br` |
| `*.data.br` | `application/octet-stream` | `br` |
| `*.loader.js` | `application/javascript` | Không có |

Giữ nguyên đuôi `.br` và cấu hình URL trong `index.html`; server không nén thêm các file đã nén. Decompression Fallback đang tắt để dùng bộ giải nén của trình duyệt. FE cần cấu hình header ở host chứa game trong iframe.
[Hướng dẫn Brotli và header của Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-deploying.html).

Data Caching giúp lần tải sau dùng lại dữ liệu, không làm giảm số byte tải lần đầu. Dùng đường dẫn theo phiên bản build để quản lý cache khi cập nhật game.

## Đo dung lượng và kiểm tra chất lượng

`Tools → WebGL → Write Asset Audit` ghi audit vào `Temp/WebGLAssetAudit.json`. Baseline được lưu tại `BuildOptimization/BaselineAudit.json`.
Sau build release, `BuildOptimization/Build-DXT.txt` hoặc `Build-ASTC.txt` ghi kết quả, dung lượng và danh sách tài nguyên theo kích thước đóng gói. Dung lượng thư mục build, dữ liệu tải qua mạng và bộ nhớ sau giải nén là ba phép đo riêng.

Khi kiểm tra trên trình duyệt, dùng các đòn light/heavy, kỹ năng Archer/WhiteMage, UI và Victory để kiểm tra texture/alpha, chuyển động, SFX và nhịp va chạm. So sánh Network transferred size của cùng một loại build, cùng scene và cùng server headers trước/sau để kết luận mức giảm tải.

## Khôi phục

Khôi phục các `.meta`, animation và `ProjectSettings.asset` từ `BuildOptimization/Originals` về đúng đường dẫn khi Unity ở Edit mode. Đổi tên ba thư mục `DemoAssets` về `Resources` trong Project window nếu muốn trả lại việc đóng gói demo như trước. Không xóa rồi tạo lại các thư mục vì sẽ làm mất GUID.
