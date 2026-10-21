# Prompt và ảnh nguồn của roster workbook

Công cụ: image_gen tích hợp, chế độ nền trong suốt; không dùng CLI hay API key.
Mỗi tấm là một atlas 4 x 4, thứ tự trái sang phải, trên xuống dưới.

| Prompt | Ảnh dùng để import | Số nhân vật |
| --- | --- | --- |
| Sheet01.txt | Art/Robinhood20261020/Sheet01.png | 16 |
| Sheet02.txt | Art/Robinhood20261020/Sheet02.png | 16 |
| Sheet03.txt | Art/Robinhood20261020/Sheet03.png | 16 |
| Sheet04.txt | Art/Robinhood20261020/Sheet04.png | 16 |
| Sheet05.txt | Art/Robinhood20261020/Sheet05.png | 16 |
| Sheet06.txt | Art/Robinhood20261020/Sheet06.png | 16 |
| Sheet07.txt | Art/Robinhood20261020/Sheet07.png | 16 |
| Sheet08.txt | Art/Robinhood20261020/Sheet08.png | 16 |
| Sheet09.txt, Sheet09-v2.txt | Art/Robinhood20261020/Sheet09-v2.png | 15 |

Đường dẫn ảnh tính từ `Assets/MemeArena/`. Sheet09-v2 là chỉnh sửa khoảng cách giữa các nhân vật;
bản Sheet09 đầu tiên được giữ để tham chiếu và không dùng cho roster mới.
Manifest `Documentation/RobinhoodMemeCatalog.json` xác định chính xác ảnh, ô và tên từng nhân vật.

Ảnh là hình minh họa comic cho khán giả. Các pose cổ vũ do hệ thống procedural hiện có điều khiển.
Không chứa ticker, lời quảng bá, nhận định tài chính hay nội dung pháp lý trong hình hoặc giao diện game.
Nhóm người thật dùng chân dung cổ vũ trung tính; tên ít có hình tham khảo được diễn họa từ mô tả nguồn.
Ba ảnh tham chiếu nhận diện ban đầu nằm trong `Documentation/References/Robinhood/`.

Atlas runtime được ghi tại đường dẫn trong `Documentation/LatestWorkbookRoster.txt` sau khi import thành công.
