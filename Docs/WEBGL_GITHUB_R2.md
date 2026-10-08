# Build dev trên GitHub Actions và deploy Cloudflare R2

Workflow `.github/workflows/build-webgl.yml` chạy trên GitHub-hosted Ubuntu khi push
`dev`. Không cần máy local bật. Build dùng Unity 6000.4.0f1, WebGL Desktop DXT,
Brotli và LoadingScene ở chế độ Frontend.

## Cấu hình repository

Settings → Secrets and variables → Actions:

| Loại | Tên | Giá trị |
| --- | --- | --- |
| Secret | UNITY_LICENSE | Nội dung license ULF tương thích GameCI |
| Secret | UNITY_EMAIL | Email Unity dùng để activate CI |
| Secret | UNITY_PASSWORD | Mật khẩu cùng tài khoản Unity |
| Secret | R2_ACCESS_KEY_ID | Access key token R2 giới hạn bucket |
| Secret | R2_SECRET_ACCESS_KEY | Secret key token R2 |
| Secret | R2_ENDPOINT | S3 API endpoint của Cloudflare |
| Secret | R2_BUCKET | `abc` nếu bucket tên abc |
| Secret | R2_PREFIX | `xyz` nếu upload vào folder xyz; có thể để trống |
| Secret | R2_PUBLIC_URL | HTTPS public domain của bucket, không thêm xyz |

**License chưa được xác minh:** file UnityEntitlementLicense.xml đã được cung cấp
không được coi là ULF tương thích. Workflow hiện theo phương án Personal ULF của
GameCI, cần activation thực sự thành công trên runner trước khi xác nhận CI hoạt
động. Không đổi tên XML thành ULF hoặc commit license vào repo.
Hướng dẫn: https://game.ci/docs/github/activation/

Image GameCI đúng phiên bản và dung lượng/RAM runner cũng cần kiểm tra trong lần
chạy đầu; chưa có build cloud được xác nhận. Log/report được lưu 7 ngày trong
Actions artifacts, kể cả khi build thất bại.

## Chạy và bàn giao FE

1. Thêm các repository secrets, hoàn tất custom domain và CORS GET/HEAD cho origin FE.
2. Commit và push các thay đổi lên dev; workflow tự chạy. Nút Run workflow chỉ
   xuất hiện khi workflow đã có trên default branch; chọn dev khi chạy thủ công.
3. Xem Actions → Build WebGL and deploy R2, kiểm tra activation rồi build/deploy.
4. Kết quả: `https://<public-domain>/xyz/<full-commit-sha>/index.html`.
5. FE đọc `https://<public-domain>/xyz/latest.json` với `fetch(..., {cache: 'no-store'})`.
   Nếu dùng Cloudflare Cache Rule, bypass cache cho latest.json.
6. Manifest cung cấp `loaderUrl`, `dataUrl`, `frameworkUrl`, `codeUrl`,
   `streamingAssetsUrl`, `baseUrl`, `indexUrl`, `commit`, `builtAt`.
   Dùng loaderUrl để tải script; truyền các URL còn lại vào createUnityInstance
   cùng cấu hình product/company/version từ index.html. loaderUrl không phải
   trường bắt buộc của cấu hình Unity runtime.
7. Đợi createUnityInstance hoàn tất, gọi
   `unityInstance.SendMessage('NetworkManager', 'StartMatchFromFrontend', matchId)`.

Script upload giữ StreamingAssets, đặt Content-Type/Content-Encoding cho Brotli,
kiểm tra size và metadata từng object qua S3 rồi mới thay latest.json. Không có
kiểm tra HTTP public tự động: lần đầu cần kiểm tra browser Network, CORS,
Brotli và kết nối BE. Upload/build lỗi không cập nhật manifest. Các workflow
được chạy tuần tự để tránh publish đồng thời.

Mỗi commit được lưu riêng, cache immutable. Rerun commit đang là latest bỏ qua
upload, chỉ thử cleanup; commit đang giữ làm previous bị từ chối để tránh ghi đè.
Manifest có previousCommit: sau deploy thành công, giữ latest và một bản thành
công trước đó, xóa các release cũ và upload dang dở có marker .release.json.
Build lỗi không thay đổi R2. Upload lỗi xóa riêng release mới sau khi đọc lại
manifest, giữ hai bản đang dùng. Nếu không đọc được manifest, không xóa để tránh
xóa nhầm một bản đã publish. Cleanup lỗi chỉ warning và thử lại ở lần sau.
Runner bị hủy có thể để lại file; lần deploy thành công tiếp theo sẽ dọn marker
dang dở. Chỉ xóa folder commit có marker do script tạo, không xóa file tùy ý trong
bucket hoặc prefix. Những bản deploy trước khi có marker không tự động bị xóa.
Token R2 cần quyền list/delete object trong bucket. Rollback phải giữ nhất quán
commit và previousCommit trong latest.json; không xóa release đang dùng.

Kiểm tra upload logic local (không kết nối R2):
`python -m unittest discover -s scripts -p test_deploy_r2.py`
