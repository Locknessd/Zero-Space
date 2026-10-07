# Reference review contract

Task: inspect available actual reference and supplied recording frames; write `Docs/EDITOR_REFERENCE_TIMING.md`.
Reference is the exact linked video `https://www.youtube.com/watch?v=mBc5gjSkOmM`.
Local partial: `GeneratedAssets/EditorPolishReview/Reference/Reference.f398.mp4.part`.
Metadata: same folder `mBc5gjSkOmM.info.json`. Stream is 1280x720 AV1, 60 FPS, 15360 time base.
Partial decoding works for at least opening 36 seconds. Do not claim the complete 336 seconds downloaded.
Use `/tmp/zero-review/bin/python` with imageio_ffmpeg to extract frames. PIL/numpy available.
Supplied recording: `Assets/Movie_001(1).mp4`, 1920x1080 30 FPS, 145.3 seconds.
Existing samples: `GeneratedAssets/EditorPolishReview/Recording/`.
Write only timing doc and extracted images under `GeneratedAssets/EditorPolishReview/Reference/`.
Inspect images with view_image, not just metadata. Count source frames only where actually visible.
Aim for 8-12 events if visible in available partial; omit absent types. Record uncertainty and lack of listening.
Do not control Unity, alter assets/scripts/scenes, add packages, publish, or claim hidden engine implementation.
Self-check all timestamps against extracted frame positions; document observed versus inferred versus proposed tuning.
