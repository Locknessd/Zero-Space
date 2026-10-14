$ErrorActionPreference = 'Stop'
$taskRoot = Get-Location
$taskEncoder = Join-Path $taskRoot '.git/codex-ko/20261010/ffmpeg/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
$taskOutput = Join-Path $taskRoot 'GeneratedAssets/PepeShowcase'
$taskVideo = Join-Path $taskOutput 'PepeCompleteCombo.mp4'
$taskTimes = @(12.80, 13.10, 13.50, 13.90, 14.20, 14.55, 14.85, 15.10, 15.30)
$taskLabels = 0..8 | ForEach-Object { '[c' + $_ + ']' }
$taskFilters = @('[0:v]split=9' + ($taskLabels -join ''))
for ($taskIndex = 0; $taskIndex -lt 9; $taskIndex++) {
    $taskStart = $taskTimes[$taskIndex].ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
    $taskEnd = ($taskTimes[$taskIndex] + .03).ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
    $taskFilters += '[c' + $taskIndex + ']trim=start=' + $taskStart + ':end=' + $taskEnd +
        ',setpts=PTS-STARTPTS,scale=640:360[p' + $taskIndex + ']'
}
$taskPanels = (0..8 | ForEach-Object { '[p' + $_ + ']' }) -join ''
$taskFilters += $taskPanels + 'xstack=inputs=9:layout=0_0|640_0|1280_0|0_360|640_360|1280_360|0_720|640_720|1280_720[out]'
& $taskEncoder -hide_banner -loglevel error -y -i $taskVideo -filter_complex ($taskFilters -join ';') -map '[out]' -frames:v 1 (Join-Path $taskOutput 'Skill05StableCameraFrames.png')
if ($LASTEXITCODE -ne 0) { throw 'Could not extract actual final attack frames.' }
$ErrorActionPreference = 'Continue'
& $taskEncoder -hide_banner -i $taskVideo -f null - 2> (Join-Path $taskOutput 'CameraVfxDecode.txt')
$taskDecodeExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Add-Content -LiteralPath (Join-Path $taskOutput 'CameraVfxDecode.txt') -Value ('Full audio/video decode exit code: ' + $taskDecodeExit)
if ($taskDecodeExit -ne 0) { throw 'Full video decode failed.' }
Copy-Item -LiteralPath $taskVideo -Destination (Join-Path $taskOutput 'PepeCompleteCombo_StableCameraVFX.mp4')
Write-Output 'Extracted nine actual Skill05 frames; full audio/video decode passed; named final copy created.'
