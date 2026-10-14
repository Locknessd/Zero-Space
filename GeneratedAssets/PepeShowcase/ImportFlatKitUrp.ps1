$ErrorActionPreference = 'Stop'
$taskRoot = (Get-Location).Path
$taskExtract = Join-Path $taskRoot 'Temp/FlatKitURPInspection'
$taskItems = Get-Content Temp/FlatKitURPContents.json -Raw | ConvertFrom-Json
$taskBackup = Join-Path $taskRoot ('GeneratedAssets/FlatKitIntegration/Backup_' + (Get-Date).ToString('yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Force -Path $taskBackup | Out-Null
Set-Content GeneratedAssets/FlatKitIntegration/BackupPath.txt -Value $taskBackup
$taskFolders = @('Assets/Materials/BattleFlatKit', 'Assets/Materials/BattleWeapons',
    'Assets/Rendering/BattleURP/Materials', 'Assets/PepeShowcase/Characters')
$taskOriginals = @('Assets/Scenes/BattleScene.unity', 'Assets/Scenes/BOMPepeTrailer.unity',
    'Assets/Scripts/Vfx/BattleImpactFeedback.Surface.cs')
foreach ($taskFolder in $taskFolders) {
    $taskOriginals += @(Get-ChildItem -LiteralPath $taskFolder -File | ForEach-Object { $_.FullName })
}
foreach ($taskOriginal in $taskOriginals) {
    if (!(Test-Path -LiteralPath $taskOriginal)) { continue }
    $taskResolved = (Resolve-Path -LiteralPath $taskOriginal).Path
    $taskRelative = $taskResolved.Substring($taskRoot.Length + 1)
    $taskDestination = Join-Path $taskBackup $taskRelative
    New-Item -ItemType Directory -Force -Path (Split-Path $taskDestination) | Out-Null
    Copy-Item -LiteralPath $taskResolved -Destination $taskDestination
}
$taskSelected = @($taskItems | Where-Object { $_.Path.StartsWith('Assets/FlatKit/Shaders/') -and $_.Bytes -gt 0 })
$taskSelected += $taskItems | Where-Object { $_.Guid -eq '576f949be0794f548b825c9ce6f556db' }
foreach ($taskItem in $taskSelected) {
    $taskRelative = $taskItem.Path
    if ($taskItem.Guid -eq '576f949be0794f548b825c9ce6f556db') {
        $taskRelative = 'Assets/FlatKit/Runtime/ObjectOutlineRendererFeature.cs'
    }
    $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskRoot $taskRelative))
    $taskAllowed = Join-Path $taskRoot 'Assets/FlatKit/'
    if (!$taskDestination.StartsWith($taskAllowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Asset destination outside FlatKit: ' + $taskDestination)
    }
    foreach ($taskSuffix in @('', '.meta')) {
        $taskCurrent = $taskDestination + $taskSuffix
        if (Test-Path -LiteralPath $taskCurrent) {
            $taskSaved = Join-Path $taskBackup ($taskRelative + $taskSuffix)
            New-Item -ItemType Directory -Force -Path (Split-Path $taskSaved) | Out-Null
            Copy-Item -LiteralPath $taskCurrent -Destination $taskSaved
        }
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $taskDestination) | Out-Null
    $taskSource = Join-Path $taskExtract $taskItem.Guid
    Copy-Item -LiteralPath (Join-Path $taskSource 'asset') -Destination $taskDestination
    Copy-Item -LiteralPath (Join-Path $taskSource 'asset.meta') -Destination ($taskDestination + '.meta')
    if ($taskItem.Guid -eq '576f949be0794f548b825c9ce6f556db') {
        $taskCode = [IO.File]::ReadAllText($taskDestination)
        $taskCode = $taskCode.Replace('using ExternPropertyAttributes;', '')
        $taskCode = $taskCode.Replace('using UnityEditor;', "#if UNITY_EDITOR`r`nusing UnityEditor;`r`n#endif")
        $taskCode = $taskCode.Replace('[ReadOnly]', '')
        [IO.File]::WriteAllText($taskDestination, $taskCode)
    }
}
$taskSelected | ConvertTo-Json | Set-Content GeneratedAssets/FlatKitIntegration/ImportedAssets.json -Encoding UTF8
Write-Output ('Imported ' + $taskSelected.Count + ' FlatKit shader/editor dependencies; backup: ' + $taskBackup)
