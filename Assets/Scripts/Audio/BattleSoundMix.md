# Âm thanh BattleScene

Đã đo 5.108 WAV gốc trong project: thời lượng, kênh, sample rate, peak/RMS, khoảng lặng đầu và vị trí peak. Báo cáo đầy đủ ở `GeneratedAssets/BattleAudioVisualReview/AudioInventory.csv`; các ứng viên dùng cho trận ở `BattleCandidates.csv`.

Battle dùng 37 clip thuộc 20 nhóm. Bản phát riêng trong `Assets/Audio/Battle/Clips/Processed` là mono PCM, cân peak −6 dBFS, cắt khoảng lặng đầu/đuôi và fade 1 ms. Giữ nguyên WAV và importer của nguồn. `BattleSfxSelection.json` giữ đường dẫn/GUID nguồn và đường dẫn bản phát để đối chiếu.

- Master volume: **0,95**, trước là 0,85; tăng khoảng 12% ở mức tổng.
- Tiếng trúng đòn dùng transient ngắn, giảm tiếng đuôi chồng trong combo. Whoosh kiếm, vũ khí nặng, giáo và tay không có khoảng phát trước điểm chạm riêng.
- Tiếng ngã có cue riêng với tiếng trúng người. Hai động tác quật/ngã tay không có một tiếng boing nhẹ sau chạm đất 20 ms.
- FIGHT phát một lần khi bắt đầu lượt đầu; luồng không gửi TURN_STARTED vẫn có fallback ở đòn đầu. VICTORY phát một lần theo kết quả. Tiếng KO phát khi chữ đáp vào màn hình sau 290 ms, chỉ một lần mỗi trận.
- Pool tối đa 12 voice SFX và một voice announcer riêng. Khi đầy, ưu tiên giữ impact/ngã; click UI có khoảng cách tối thiểu 60 ms. Reset trận hủy âm thanh cũ.

Nghe từng clip ở `GeneratedAssets/BattleAudioVisualReview/AudioPreview.html`. Mức volume nhóm ở `BattleSfxBank.asset`; chỉnh master trên component `BattleSfxPlayer` của GameManager. Menu **Tools → Battle → Install selected SFX** dựng lại bank và timeline từ manifest.

FlatKit Built-in được dùng cho vật liệu riêng của BattleScene trong `Assets/Materials/BattleFlatKit`. Menu **Tools → Battle → Apply FlatKit battle look** áp dụng bóng cel có texture, viền nhân vật/vũ khí và rim light. Gói shader là bản 4.9.8 đã có trong project; tài liệu: https://flatkit.dustyroom.com/stylized-surface/.

## ??ng b? sound v?i animation v? VFX

- Playback ?i qua m?i cue sound/VFX ??ng th?i gian ?? author, k? c? khi m?t frame v??t qua nhi?u cue. Hit sound v?n d?ng s? ki?n ContactOccurred chung v?i VFX hit.
- startOffsets ???c ?o ri?ng cho t?ng bi?n th?: b? ?o?n ?m nh? tr??c attack (30% peak RMS, c?a s? 2 ms), gi? kho?ng 5 ms d?n v?o v? ch?n sample g?n zero ?? tr?nh ti?ng click. Kh?ng s?a WAV g?c, pitch, volume ho?c m?c hit/damage.
- Ba c?ng c? d?ng bank/weapon sounds t?nh l?i offset ?? gi? ??ng b? sau khi ?p d?ng l?i sound pack.
- Tools > Battle > Audio c? c?c l?nh Align sound attacks, Validate all animation sound timings. B?o c?o waveform, timeline v? AudioSource Play Mode ? GeneratedAssets/BattleAudioSyncReview.
