# Art còn cần cho bản production

Theo yêu cầu ngày 19/10/2026, khán giả đã thay bằng 16 sprite meme comic mới được tạo bằng image_gen,
lưu trong `Art/MemeCrowd2026.png`; prompt và danh sách nằm trong `Documentation/MemeCrowdAndFloor.md`.
Ngày 20/10, bộ mở rộng workbook bổ sung 143 hình comic; danh mục tổng là 159 loại.
Ảnh nguồn nằm trong `Art/Robinhood20261020/`, prompt trong `Documentation/RobinhoodPrompts/`.
Tên ít có tài liệu hình ảnh được minh họa theo mô tả nguồn; đây không phải bộ mascot chính thức của dự án token.
Stone, Sand, Sky và tượng primitive vẫn dùng art/placeholder hiện có; crowd silhouette cũ không còn dùng
trong prefab mới. Bộ sprite meme không phải ảnh/model chính thức của các nhân vật.

Cần thay hoặc bổ sung khi chốt art:

- Mesh Doge Emperor hoàn chỉnh, cùng vật liệu và LOD; hiện dùng bust primitive.
- Sprite sheet nhiều pose hoặc prefab nhẹ có Animator cho từng loại fan.
- Biến thể supporter, cờ/biểu tượng riêng cho đội, texture stone/sand production nếu cần.
- Texture sky panorama có seam chuẩn và lớp crowd xa đồng bộ art direction.
- Audio crowd cheer/boo/victory là phần bổ sung tùy chọn; bộ này không tự tạo các giọng cổ vũ.

Có thể gán sprite/prefab/material/texture vào settings mà không sửa code.
