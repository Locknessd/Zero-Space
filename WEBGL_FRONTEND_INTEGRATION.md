# Khởi động game từ Frontend

`WebSocketManager` tự chọn startup mode theo môi trường chạy:

- **Unity Editor → Client Input**: kết nối tự động và dùng các ID đã nhập trong Unity như trước,
  kể cả khi Build Profiles đang chọn WebGL.
- **WebGL trên trình duyệt → Frontend**: chờ FE gọi hàm; không dùng `startMatchRequestId`, `autoSubscribeMatchId` hoặc
  `initialMatchId` nhập sẵn để chọn trận. Các phím Q/E test local cũng được bỏ qua ở chế độ này.

Inspector không còn mục chọn `Startup Mode`. Để test trong Editor, nhập ID như trước rồi bấm Play.
Để bàn giao FE, build WebGL; bản build tự dùng Frontend và đợi FE truyền ID.
API khởi động từ FE chỉ hoạt động trong bản WebGL, nên cần kiểm tra luồng này trên trình duyệt.

## FE mở một trận đã có Match ID

Chỉ gọi sau khi promise `createUnityInstance(...)` hoàn tất. Tên `NetworkManager` là tên GameObject,
không phải tên class; nếu đổi tên object trong scene thì phải đổi tham số đầu của `SendMessage`.

```javascript
const unityInstance = await createUnityInstance(canvas, config, onProgress);

// Gọi lúc FE đã có Match ID và muốn bắt đầu hiển thị trận.
unityInstance.SendMessage("NetworkManager", "StartMatchFromFrontend", matchId);
```

Unity kết nối BE với cấu hình Inspector, gửi `meme_battle_subscribe` chứa
`{ matchId, afterSequence: 0 }` dưới dạng chuỗi JSON, đợi snapshot rồi mở BattleScene.

## FE truyền cấu hình hoặc yêu cầu BE bắt đầu trận

`StartGameFromFrontend` nhận một chuỗi JSON chứa **đúng một** trong `matchId` và `requestId`.
`serverUrl` và `serviceToken` là tùy chọn; bỏ qua để dùng cấu hình Inspector.

```javascript
unityInstance.SendMessage("NetworkManager", "StartGameFromFrontend", JSON.stringify({
  matchId: "match_123",
  serverUrl: "https://your-game-backend.example",
  serviceToken: rendererToken
}));
```

Nếu FE có `requestId` theo luồng BE đang dùng:

```javascript
unityInstance.SendMessage("NetworkManager", "StartGameFromFrontend", JSON.stringify({
  requestId: "request_123"
}));
```

Unity gửi sự kiện `Start Match Event Name` trong Inspector với payload `{ requestId }` dạng chuỗi JSON.
LoadingScene hiện cấu hình sự kiện này là `meme_battle_start`.
Sau `meme_battle_start_result` thành công, Unity mở BattleScene và tiếp tục xử lý sự kiện như bình thường.
Chế độ FE tự gửi yêu cầu này, không cần bật `Emit Start Match On Connect`.

Gọi lặp cùng ID trong lúc kết nối hoặc đang chơi sẽ không khởi động trận lần nữa.
Mỗi Unity instance xử lý một trận; reload instance để chuyển sang ID khác.
Khi kết nối bị từ chối hoặc thư viện không tải được, có thể gọi lại cùng ID với cấu hình đã sửa.
Sau khi mất kết nối, Unity đăng ký lại Match ID hiện tại với sequence cuối đã nhận.

## Event trong lúc tải BattleScene

FE gọi hàm khởi động một lần như các ví dụ trên. Unity giữ event tại `WebSocketManager`
xuyên suốt thời gian tải scene ở cả Client Input và Frontend. Game chỉ nhận và phát hàng đợi
khi hai nhân vật, UI đã khởi tạo và màn hình loading đã đóng. Không có thời gian tải tối đa
hoặc giới hạn số event khiến lịch sử bị bỏ đi.

Trận đã `FINISHED` vẫn phát đủ lịch sử rồi mới hiện kết quả. Event được phát theo `sequence`,
bỏ qua bản gửi trùng; event trực tiếp đến trước lịch sử sẽ chờ phần sequence còn thiếu.
`snapshot.latestSequence` mô tả trạng thái BE, không thay thế việc nhận từng event.
Sau start result có Match ID, Unity đăng ký lấy phần lịch sử chưa nhận.

BE cần trả đầy đủ `events` sau `afterSequence` trong phản hồi `meme_battle_snapshot`,
với sequence liên tiếp bắt đầu từ 1 cho từng trận. Unity không thể tái tạo lượt đánh chỉ từ
snapshot HP cuối trận nếu BE không gửi lịch sử tương ứng.

Thay đổi này cần build lại WebGL để FE nhận bản sửa; API gọi từ FE giữ nguyên.

## Socket.IO trên WebGL

WebGL dùng cầu nối `Assets/Plugins/WebGL/ZeroSpaceSocketIO.jslib` với Socket.IO trình duyệt,
không dùng `ClientWebSocket` .NET. Bản Socket.IO **4.8.4** và giấy phép MIT được đóng gói trong
`Assets/StreamingAssets/ZeroSpace`; FE không cần tải thêm thư viện từ CDN.
Nếu trang đã cung cấp `window.io`, cầu nối sẽ dùng client đó.
Khi triển khai, giữ thư mục `StreamingAssets` đi kèm build và cho phép tải script cùng origin.
Token truyền cho trình duyệt phải là credential mà BE cho phép sử dụng ở client.

Snapshot và các sự kiện nhận trước khi BattleScene sẵn sàng được giữ trong loading và chuyển tiếp theo thứ tự.
Trong chế độ FE, các ID nhập sẵn ở loading hoặc battle không ghi đè trận FE đã chọn.

## Kiểm tra thủ công

1. Editor tự dùng Client Input: kiểm tra luồng kết nối/start hiện tại, kể cả khi Build Profiles chọn WebGL.
2. WebGL build tự dùng Frontend: tải game, xác nhận chưa có socket tới BE trước khi FE gọi.
3. Gọi với Match ID hợp lệ: kiểm tra subscribe đúng ID, nhận snapshot rồi chuyển sang BattleScene.
4. Gọi với request ID hợp lệ: kiểm tra `meme_battle_start` và chuyển scene sau kết quả thành công.
5. Gọi lặp cùng ID: không có yêu cầu start/subscribe thêm. ID rỗng hoặc JSON sai hiển thị lỗi ở loading.
6. Nhập ID khác sẵn trong các component: chế độ FE vẫn dùng ID FE gửi.
7. Ngắt rồi nối mạng lại: kiểm tra đăng ký lại cùng trận và sequence tiếp tục.

Tham khảo: [Unity SendMessage](https://docs.unity3d.com/6000.0/Documentation/Manual/web-interacting-browser-unity-to-js.html)
và [Socket.IO client API](https://socket.io/docs/v4/client-api/).
