# ZeroSpace — Hướng dẫn FE gọi Unity WebGL

Tài liệu bàn giao cho Frontend, áp dụng cho API hiện tại của ZeroSpace.

FE tải Unity WebGL, đợi runtime sẵn sàng, rồi truyền ID để Unity kết nối Backend và hiển thị trận.
Trong chế độ Frontend, Unity chờ lời gọi từ FE trước khi mở kết nối game.

## 1. Điều kiện tích hợp

- Bản WebGL được build với `Startup Mode = Frontend` trên `NetworkManager` trong LoadingScene.
- GameObject nhận lời gọi có tên chính xác là `NetworkManager`.
- FE đã tải file `*.loader.js` của build và tạo một `<canvas>` để Unity hiển thị.
- FE có Match ID của một trận đã tồn tại, hoặc request ID dùng để yêu cầu BE bắt đầu trận.

Unity chỉ có một mục `Startup Mode` áp dụng cho cả Editor và bản build:
`Client Input` dùng ID nhập trong Unity để test; `Frontend` chờ FE gọi hàm.
Phía Unity chọn `Frontend`, lưu scene rồi build WebGL trước khi bàn giao cho FE.

Lấy cấu hình `unityConfig` và đường dẫn loader từ `index.html` đi kèm bản build.
Giữ đúng tên các file build, kể cả đuôi nén. Khi đổi vị trí host, cập nhật các URL tương ứng.
Bản release tối ưu dùng Brotli: host bằng HTTPS và trả `Content-Encoding: br` cho `*.br`,
`Content-Type: application/wasm` cho `*.wasm.br`, `application/javascript` cho `*.framework.js.br`
và `application/octet-stream` cho `*.data.br`. Không nén thêm file đã có đuôi `.br`.
Xem [cấu hình build và host](WEBGL_BUILD_OPTIMIZATION.md) để chọn bản Desktop/Mobile và kiểm tra header.
`streamingAssetsUrl` cần trỏ tới thư mục `StreamingAssets` được bàn giao cùng build.
[Tham khảo cấu hình build của Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/web-templates-build-configuration.html).

## 2. Cách gọi nhanh: FE đã có Match ID

Ví dụ dưới đây chạy sau khi loader đã tải và `unityConfig` đã được khai báo theo bản build:

```javascript
const canvas = document.querySelector("#unity-canvas");

const unityInstance = await createUnityInstance(
  canvas,
  unityConfig,
  (progress) => {
    console.log("Unity loading:", Math.round(progress * 100) + "%");
  }
);

// Gọi tại thời điểm FE muốn hiển thị trận đã có trên BE.
const matchId = "match_123"; // Thay bằng Match ID thực tế.
unityInstance.SendMessage(
  "NetworkManager",
  "StartMatchFromFrontend",
  matchId
);
```

Đợi promise `createUnityInstance(...)` hoàn tất rồi mới gọi hàm game.
Callback `progress` ở ví dụ trên biểu thị tiến độ tải Unity.
Việc kết nối BE và tải dữ liệu trận diễn ra sau lời gọi `StartMatchFromFrontend`.
[Tham khảo khởi tạo Unity instance](https://docs.unity3d.com/6000.0/Documentation/Manual/web-templates-structure.html).

Luồng sau lời gọi: **FE gửi Match ID → Unity kết nối BE → đăng ký trận → nhận snapshot → mở BattleScene**.

## 3. API FE → Unity

FE gọi bằng `unityInstance.SendMessage(gameObjectName, methodName, argument)`.
Hai tên hàm dưới đây phân biệt chữ hoa/chữ thường và nhận một tham số kiểu string.
[Tham khảo SendMessage](https://docs.unity3d.com/6000.0/Documentation/Manual/web-interacting-browser-unity-to-js.html).

| Thành phần | Giá trị |
| --- | --- |
| GameObject | `NetworkManager` |
| Hàm mở trận đã có | `StartMatchFromFrontend` |
| Tham số của hàm mở trận | Match ID dạng string, ví dụ `"match_123"` |
| Hàm truyền cấu hình / yêu cầu start | `StartGameFromFrontend` |
| Tham số của hàm cấu hình | String JSON được tạo bằng `JSON.stringify(...)` |

### A. Mở trận đã tồn tại

```javascript
unityInstance.SendMessage(
  "NetworkManager", "StartMatchFromFrontend", "match_123"
);
```

Unity dùng endpoint và token đã cấu hình trong bản build.
Backend nhận sự kiện `meme_battle_subscribe` với payload JSON string:

```json
{ "matchId": "match_123", "afterSequence": 0 }
```

### B. Mở trận và truyền endpoint / token

```javascript
unityInstance.SendMessage(
  "NetworkManager",
  "StartGameFromFrontend",
  JSON.stringify({
    matchId: "match_123",
    serverUrl: "https://game-api.example.com",
    serviceToken: rendererToken
  })
);
```

`rendererToken` là biến FE lấy theo cơ chế xác thực đã thống nhất với BE.
Endpoint trong ví dụ là placeholder; thay bằng endpoint thật.

### C. Yêu cầu BE bắt đầu trận bằng request ID

```javascript
unityInstance.SendMessage(
  "NetworkManager",
  "StartGameFromFrontend",
  JSON.stringify({ requestId: "request_123" })
);
```

Có thể thêm `serverUrl` và `serviceToken` như ví dụ B.
Unity gửi sự kiện start đã cấu hình trong build; scene hiện tại dùng `meme_battle_start`.
Payload gửi tới BE là JSON string `{ "requestId": "request_123" }`.
Sau kết quả start thành công, Unity mở BattleScene và tiếp tục luồng game.

`requestId` định danh yêu cầu bắt đầu trận; dùng `matchId` khi trận đã tồn tại trên BE.
FE chọn một trong A, B hoặc C cho lần khởi động của một Unity instance.

## 4. Hợp đồng JSON của StartGameFromFrontend

| Trường | Kiểu | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `matchId` | string | Nếu mở trận đã có | Match ID do BE cung cấp. |
| `requestId` | string | Nếu yêu cầu start | Request ID theo hợp đồng start của BE. |
| `serverUrl` | string | Không | Endpoint Socket.IO; bỏ qua để dùng cấu hình trong build. |
| `serviceToken` | string | Không | Token xác thực renderer; bỏ qua để dùng cấu hình trong build. |

Quy tắc dữ liệu:

- Có **đúng một** ID không rỗng: `matchId` hoặc `requestId`.
- ID được loại bỏ khoảng trắng đầu/cuối; giữ nguyên nội dung và chữ hoa/chữ thường.
- `serverUrl` phải là URL tuyệt đối với scheme `http`, `https`, `ws` hoặc `wss`.
- Bỏ qua `serviceToken` để giữ token của build; chuỗi rỗng sẽ ghi đè token thành rỗng.
- Dùng `JSON.stringify(payload)` làm tham số thứ ba của `SendMessage`.

Ví dụ dữ liệu không hợp lệ: `{}`, ID chỉ có khoảng trắng, hoặc truyền cả hai ID có giá trị.

## 5. Kết quả, lỗi và vòng đời

Lời gọi `SendMessage` gửi lệnh vào Unity và không trả về promise chờ BE hoàn tất.
Kết quả start, lỗi xác thực và lỗi dữ liệu được Unity xử lý, hiển thị ở màn hình loading.
**API hiện tại chưa có callback JavaScript báo game ready, start thành công hoặc lỗi BE.**
Promise từ `createUnityInstance` xác nhận runtime Unity đã tải xong, không xác nhận trận đã bắt đầu.

Các mã lỗi phía Unity để đối chiếu khi phối hợp xử lý sự cố:

| Mã | Nguyên nhân / cách xử lý |
| --- | --- |
| `FRONTEND_START_DISABLED` | Bản build đang dùng Client Input; cần build với chế độ Frontend. |
| `FRONTEND_START_INVALID` | JSON, ID hoặc URL không hợp lệ; kiểm tra lại payload. |
| `FRONTEND_SESSION_ACTIVE` | Instance đã chọn một ID khác; tạo instance mới để chuyển trận. |

Các mã trên được phát qua event C# `OnConnectionError`, chưa được chuyển thành callback JS.

Vòng đời của một instance:

- Gọi lặp cùng ID khi đang kết nối hoặc đang chơi không phát thêm yêu cầu khởi động trận.
- Một instance chọn một trận. Để chuyển sang ID khác, reload trang hoặc hủy instance cũ rồi tạo instance mới.
- Có thể hủy instance bằng `await unityInstance.Quit()` trước khi tạo lại.
  [Tham khảo tương tác với Unity instance](https://docs.unity3d.com/6000.0/Documentation/Manual/web-templates-build-configuration.html).
- Sau lỗi kết nối bị từ chối hoặc lỗi tải thư viện, FE có thể gọi lại **cùng ID** với endpoint/token đã sửa.
- Với cấu hình tự reconnect hiện tại, Unity tự nối lại và đăng ký trận với sequence cuối đã nhận.

## 6. Bàn giao file và kiểm tra tích hợp

Host thư mục build và `StreamingAssets` đi kèm. Socket.IO cho trình duyệt đã được đóng gói trong build;
FE có thể dùng cầu nối có sẵn mà không cần cài thêm package Socket.IO để gọi hai API trên.
Nếu trang cung cấp `window.io`, Unity sẽ dùng Socket.IO client đó.

Các bước nghiệm thu với FE và BE:

1. Tải Unity và chưa gọi hàm: game vẫn chờ FE, chưa kết nối BE.
2. Gọi Match ID hợp lệ: Unity đăng ký đúng trận và mở BattleScene sau khi nhận snapshot.
3. Gọi request ID hợp lệ: Unity gửi start và vào game sau kết quả thành công.
4. Gọi lại cùng ID: trận không khởi động lại.
5. Gửi ID rỗng / JSON sai: Unity hiển thị lỗi dữ liệu ở loading.
6. Đổi trận: hủy hoặc reload instance, khởi tạo lại rồi gọi ID mới.

Chọn các ID test được BE cung cấp. Những ID trong tài liệu chỉ là ví dụ.
