$ErrorActionPreference = 'Stop'
$taskRoot = Get-Location
$taskEncoder = Join-Path $taskRoot '.git/codex-ko/20261010/ffmpeg/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
$taskOutput = Join-Path $taskRoot 'GeneratedAssets/PepeShowcase'
$taskVideo = Join-Path $taskOutput 'BOM_Pepe_CharacterTrailer.mp4'

function Export-TaskGrid([double[]] $Times, [string] $Name, [int] $Columns) {
    $taskLabels = 0..($Times.Length - 1) | ForEach-Object { '[c' + $_ + ']' }
    $taskFilters = @('[0:v]split=' + $Times.Length + ($taskLabels -join ''))
    $taskPanels = ''
    $taskLayout = @()
    for ($taskIndex = 0; $taskIndex -lt $Times.Length; $taskIndex++) {
        $taskTime = $Times[$taskIndex].ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
        $taskEnd = ($Times[$taskIndex] + .04).ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
        $taskFilters += '[c' + $taskIndex + ']trim=start=' + $taskTime + ':end=' + $taskEnd +
            ',setpts=PTS-STARTPTS,scale=640:360[p' + $taskIndex + ']'
        $taskPanels += '[p' + $taskIndex + ']'
        $taskLayout += ((($taskIndex % $Columns) * 640).ToString() + '_' +
            ([Math]::Floor($taskIndex / $Columns) * 360).ToString())
    }
    $taskFilters += $taskPanels + 'xstack=inputs=' + $Times.Length +
        ':layout=' + ($taskLayout -join '|') + '[out]'
    & $taskEncoder -hide_banner -loglevel error -y -i $taskVideo -filter_complex ($taskFilters -join ';') `
        -map '[out]' -frames:v 1 (Join-Path $taskOutput $Name)
    if ($LASTEXITCODE -ne 0) { throw ('Could not export actual video frames: ' + $Name) }
}

Export-TaskGrid @(1.5, 3, 5, 7, 9, 11, 12, 14, 15.5) 'BomIntroFrames.png' 3
Export-TaskGrid @(17.99, 19.4, 21, 23.34, 24.2, 25.10, 26.4, 27.15, 28.4) 'BomComboFrames.png' 3
Export-TaskGrid @(29, 29.3, 29.7, 30.1, 30.4, 30.75, 31.05, 31.3, 31.5) 'BomBlinkFrames.png' 3
Export-TaskGrid @(18.02, 18.38, 18.72, 25.1, 25.5, 25.9, 26.85, 27.02, 27.22) 'BomAirCutFrames.png' 3
Export-TaskGrid @(23.32, 23.355, 23.44, 23.52, 23.555, 23.64, 24.67, 24.705, 24.79) 'BomMuzzleFrames.png' 3
foreach ($taskFrame in @(@(1.5, 'BomTitle.png'), @(13, 'BomPepeReveal.png'), @(25.1, 'BomPowerSlash.png'))) {
    $taskTime = $taskFrame[0].ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
    & $taskEncoder -hide_banner -loglevel error -y -ss $taskTime -i $taskVideo -frames:v 1 `
        (Join-Path $taskOutput $taskFrame[1])
    if ($LASTEXITCODE -ne 0) { throw 'Could not export a full resolution review frame.' }
}
$taskProcess = New-Object System.Diagnostics.Process
$taskProcess.StartInfo.FileName = $taskEncoder
$taskProcess.StartInfo.Arguments = '-hide_banner -i "' + $taskVideo + '" -f null -'
$taskProcess.StartInfo.UseShellExecute = $false
$taskProcess.StartInfo.CreateNoWindow = $true
$taskProcess.StartInfo.RedirectStandardError = $true
$taskProcess.Start() | Out-Null
$taskDecodeLog = $taskProcess.StandardError.ReadToEnd()
$taskProcess.WaitForExit()
$taskDecodeExit = $taskProcess.ExitCode
$taskProcess.Dispose()
[IO.File]::WriteAllText((Join-Path $taskOutput 'BomDecode.txt'),
    $taskDecodeLog + [Environment]::NewLine + 'Full audio/video decode exit code: ' + $taskDecodeExit)
if ($taskDecodeExit -ne 0) { throw 'Full video decode failed.' }
Write-Output 'BOM intro, combat and final blink frames extracted; full audio/video decode passed.'
