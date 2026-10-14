$ErrorActionPreference = 'Stop'
$taskEncoder = Join-Path (Get-Location) '.git/codex-ko/20261010/ffmpeg/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
$taskOutput = 'Assets/Audio/Battle/MemeSlashes'
$taskPack = 'Assets/Audio/Battle/Clips/WeaponPacks'
$taskDeep = 'Assets/Universal Sound FX/WHOOSHES/Classic/WHOOSH_Deep_mono.wav'
$taskEdge = 'Assets/Magic_Sci_Fi_Sword_Sounds/WAV/Sci_Fi_Sword_Cut_Fast_01.wav'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskSources = @('Dagger_woosh_2_swing.wav', 'Dagger_woosh_6_swing.wav',
    'Heavy_sword_woosh_4_swing.wav', 'Heavy_sword_woosh_15_swing.wav')
for ($taskIndex = 0; $taskIndex -lt $taskSources.Length; $taskIndex++) {
    $taskSource = Join-Path $taskPack $taskSources[$taskIndex]
    $taskFilter = '[0:a]aformat=sample_rates=48000:channel_layouts=mono,atrim=0:0.42,asetpts=PTS-STARTPTS,' +
        'highpass=f=70,bass=g=5:f=140,treble=g=2,acompressor=threshold=0.2:ratio=3:attack=2:release=55,' +
        'volume=1.3[a];[1:a]aformat=sample_rates=48000:channel_layouts=mono,atrim=0:0.35,' +
        'asetpts=PTS-STARTPTS,lowpass=f=700,volume=0.32[b];' +
        '[2:a]aformat=sample_rates=48000:channel_layouts=mono,atrim=0:0.3,asetpts=PTS-STARTPTS,' +
        'highpass=f=900,volume=0.22[c];[a][b][c]amix=inputs=3:normalize=0,volume=1.35,' +
        'alimiter=limit=0.8:level=0:latency=1,afade=t=in:d=0.003,afade=t=out:st=0.29:d=0.13[out]'
    $taskName = 'Slash_Power_' + ($taskIndex + 1).ToString('00') + '.wav'
    & $taskEncoder -hide_banner -loglevel error -y -i $taskSource -i $taskDeep -i $taskEdge `
        -filter_complex $taskFilter -map '[out]' -ar 48000 -ac 1 -c:a pcm_s16le (Join-Path $taskOutput $taskName)
    if ($LASTEXITCODE -ne 0) { throw ('Could not create heavy slash: ' + $taskName) }
}
$taskHits = @('Dark_Sword_Hit_01_hit.wav', 'Dark_Sword_Hit_02_hit.wav')
for ($taskIndex = 0; $taskIndex -lt $taskHits.Length; $taskIndex++) {
    $taskName = 'Cut_Impact_' + ($taskIndex + 1).ToString('00') + '.wav'
    & $taskEncoder -hide_banner -loglevel error -y -i (Join-Path $taskPack $taskHits[$taskIndex]) `
        -af 'atrim=0:0.36,asetpts=PTS-STARTPTS,highpass=f=55,bass=g=5:f=120,treble=g=2,acompressor=threshold=0.18:ratio=3:attack=1:release=45,volume=1.8,alimiter=limit=0.8:level=0:latency=1,afade=t=in:d=0.001,afade=t=out:st=0.25:d=0.11' `
        -ar 48000 -ac 1 -c:a pcm_s16le (Join-Path $taskOutput $taskName)
    if ($LASTEXITCODE -ne 0) { throw ('Could not create cut impact: ' + $taskName) }
}
Write-Output 'Created four layered power slashes and two weightier blade impacts, mono 48 kHz PCM.'
