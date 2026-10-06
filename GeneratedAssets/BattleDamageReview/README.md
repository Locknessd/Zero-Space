# Damage theo từng hit trong Battle

Đã chuyển HP và số trừ máu của một lượt đánh sang các mốc va chạm trong animation. Ví dụ tổng damage 100 với 3 hit: `33 + 33 + 34`. Các phần chỉ lệch nhau tối đa 1 đơn vị; phần dư vào các hit cuối và tổng vẫn chính xác.

`GameManager` nhận kết quả HP/damage của server một lần, chọn đòn rồi tạo `BattleHitDamageSequence`. `FrankBattlePairPlayback.TimelineAdvanced` phát đồng hồ sau khi sample animation và chạy cue âm thanh/VFX; HP, chip trail, chữ damage và CRITICAL được cập nhật trong cùng frame với hit. Metadata lấy từ timeline dùng chung, nên tắt AudioSource/VFX không tắt damage.

Đếm các cue `light_hit`, `heavy_hit`, `stab_hit`. Cú ngã của đòn đã có hit không bị tính thêm damage. Đòn quật ngã không có cue hit riêng dùng lần chạm đất cuối làm một hit. Đòn/controller thiếu timeline dùng cách cập nhật HP dự phòng hiện có.

Số hiển thị dùng `long` để giữ đúng damage atomic lớn. Với overkill, tổng số damage vẫn theo server, còn HP chia phần lượng máu thực mất; thanh máu chạm 0 ở hit cuối. KO tiếp tục chờ hoạt ảnh chết kết thúc. HP_CHANGED/DAMAGE_APPLIED trùng hoặc HP đến muộn cùng lượt không trừ lại. Reset/hủy lượt bỏ các hit chưa chạy và tháo callback, nên damage cũ không lọt sang trận mới. Camera được giữ nguyên.

| Ví dụ Play Mode | Damage 103 chia thành |
| --- | --- |
| Katana, 3 hit | 34 + 34 + 35 |
| Assassin, 2 hit | 51 + 52 |
| Heavy_8, 7 hit | 14 + 14 + 15 + 15 + 15 + 15 + 15 |

[Mankey ở hit thứ 2](Mankey_SecondHit.png) · [Pepe ở hit thứ 2](Pepe_SecondHit.png)

Kiểm tra:

- [Validation.txt](Validation.txt): 80 tổ hợp profile/tổng damage, gồm damage 1, 100, 103 và số 64-bit; tổng chính xác, cân bằng, không trừ lúc windup, không về 0 sớm, nhảy frame, clock lặp/quay lùi, overkill và hủy.
- [PlayValidation.txt](PlayValidation.txt): 18 đòn của hai nhân vật + đòn kết liễu; 71 contact frame khớp native HUD/slider/number. Gửi trùng damage, HP_CHANGED, HP muộn và winner. Âm thanh/VFX/lighting/hit-stop giữ đúng cue; camera hiện có chạy đủ 18 shot.
- [UiPlayValidation.txt](UiPlayValidation.txt): chat/KO cả hai phía, UI ở timeScale 0, winner facing; thêm đòn Katana khi tắt cả SFX/VFX, kiểm tra HP chỉ mất 33 ở hit đầu rồi reset. Trận mới giữ 1000/1000, không xuất hiện phần damage còn lại.

Các kiểm tra dùng bản BattleScene tạm với WebSocket tắt, tự xóa khi xong. Chạy lại bằng các job `ValidateBattleHitDamage`, `BattleDamagePlayCheck` và `BattleUiPlayCheck`.
