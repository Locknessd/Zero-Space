# Combat triplets

Luồng chính:

`WebSocketManager → GameManager FIFO queue → random CombatTripletData → CharacterCombat → AnimatorOverrideController → AnimationEndAction → hoàn tất stack`

- `WebSocketManager` chỉ chuyển message về main thread; không điều khiển animation.
- `GameManager` gom các event cùng `matchId/turnId` vào một stack. Stack chờ `DAMAGE_APPLIED`, `HP_CHANGED`, `TURN_RESOLVED`, ranh giới lượt mới hoặc event cấp match. Không dùng khoảng chờ 0.15 giây để đoán BE đã gửi xong.
- Khi chạy stack, `animationId` chứa `heavy` chọn Heavy pool; các giá trị còn lại chọn Light pool. Random đúng một lần, Hit và GetUp lấy từ chính bộ đã chọn.
- `CombatPositioningController` chỉ di chuyển tới `attackRange` và chỉnh vị trí sau animation; không giữ queue.
- Hai nhân vật dùng override controller riêng. Attack và Hit bắt đầu cùng lượt. Hit tự chuyển sang GetUp khi bộ có clip đứng dậy. Không có GetUp thì Hit trở về Idle.
- Queue chờ callback hoàn tất cả Attack lẫn chuỗi Hit/GetUp. Callback trùng hoặc từ lượt cũ bị bỏ qua. HP do BE quyết định, được cập nhật một lần mỗi lượt khi cặp animation bắt đầu.
- Với HP bằng 0, người nhận chạy hết Hit rồi giữ tư thế cuối, không chạy GetUp.
- Pool rỗng, cấu hình sai, animation bị ngắt hoặc thiếu callback sẽ báo `QueueError` và dừng queue. Timeout chỉ báo lỗi, không tự cắt animation để chạy lượt sau.

## Cấu hình

`Assets/Resources/Combat/TripletCombat.controller` có Idle, Walk, Attack, Hit, GetUp, Victory. Các state action gắn sẵn `AnimationEndAction`; transition kết thúc dùng normalized exit time 1 và duration 0.

Trên mỗi `CharacterCombat`:

1. Gán Animator, controller và clip Idle/Walk. Các clip mốc Attack/Hit/GetUp có thể tự tìm theo tên placeholder.
2. Điền `lightCombatMoves` và `heavyCombatMoves`: attackAnim, hitAnim, getUpAnim tùy chọn, attackRange và weapon.
3. Các clip phải tương thích với rig/Avatar của Animator nhận chúng. Đây là luồng Animator trực tiếp, không dùng hệ retarget của demo Frank.
4. Gán `GameManager.leftCombat` và `rightCombat`. Nếu cần bước vào tầm đánh, gán hoặc giữ `CombatPositioningController` trong scene.

BattleScene đã chuyển các slot cũ sang clip reference trong pool, giữ mapping AttackIndex/HitIndex từ controller cũ và hướng GetUp 02 cho slot 7. Light giữ khoảng cách 1.5, Heavy 2.2. Có thể chỉnh từng bộ trong Inspector. `victoryAnim` là tùy chọn; để trống thì chỉ hiển thị kết quả trận, không tự chọn clip khác.

`CharacterAnimatorBridge` được thay bằng `CharacterCombat`, giữ script GUID để các reference trong scene không mất. `FrankCombinationTester` và các script retarget demo không thay đổi.

## Kiểm tra

- Trong Play Mode: Q chạy Left Light, E chạy Right Heavy, đều đi qua queue. Inspector GameManager có nút tương ứng và hiển thị lỗi queue.
- `Tools → Combat → Validate Triplet Queue` kiểm tra controller, callback, override isolation, GetUp, lethal hit, BE stack ordering, duplicate/late events và pool trong BattleScene. Kết quả ghi vào `Temp/CombatFlowValidation.txt`.
- `Reset Combat Queue` bỏ các stack đang chờ, dừng lượt hiện tại và reset hai Animator. Dùng sau khi sửa cấu hình hoặc để bắt đầu lại bài test local.
