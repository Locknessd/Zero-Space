$ErrorActionPreference = 'Stop'
$taskOutput = Join-Path (Get-Location) 'GeneratedAssets/PepeShowcase'
$taskJobs = Join-Path (Get-Location) 'Temp/FrankRetarget'
$taskLog = Join-Path $taskOutput 'ComboPipeline.txt'
$taskHelper = Join-Path $taskOutput 'InvokeUnityJob.ps1'

function Write-TaskProgress([string] $Message) {
    $taskLine = (Get-Date).ToString('HH:mm:ss') + ' ' + $Message
    Write-Output $taskLine
    Add-Content -LiteralPath $taskLog -Value $taskLine -Encoding UTF8
}

function Invoke-TaskJob([string] $Name) {
    Write-TaskProgress ('START ' + $Name)
    & powershell -NoProfile -ExecutionPolicy Bypass -File $taskHelper -Job $Name -TimeoutSeconds 900
    if ($LASTEXITCODE -ne 0) { throw ('Unity job failed: ' + $Name) }
    Write-TaskProgress ('PASS ' + $Name)
}

Write-TaskProgress 'Waiting for the already-requested CreatePepeAssassinCombo; no duplicate setup request.'
$taskDeadline = (Get-Date).AddMinutes(20)
while ($true) {
    if (Test-Path -LiteralPath (Join-Path $taskJobs 'error')) {
        throw (Get-Content -LiteralPath (Join-Path $taskJobs 'error') -Raw)
    }
    if ((Test-Path -LiteralPath (Join-Path $taskJobs 'done')) -and
        (Get-Content -LiteralPath (Join-Path $taskJobs 'done') -Raw).Trim() -eq
        'CreatePepeAssassinCombo completed') { break }
    if ((Get-Date) -gt $taskDeadline) { throw 'Timed out waiting for combo setup.' }
    Start-Sleep -Seconds 2
}
Write-TaskProgress 'PASS combo setup'
Invoke-TaskJob 'ValidatePepeAssassinCombo'
$taskRecordStart = Get-Date
Invoke-TaskJob 'RecordPepeAssassinCombo'
$taskDeadline = (Get-Date).AddMinutes(20)
$taskLastStatus = ''
while ($true) {
    $taskStatusPath = Join-Path $taskOutput 'Status.txt'
    if ((Test-Path -LiteralPath $taskStatusPath) -and
        (Get-Item -LiteralPath $taskStatusPath).LastWriteTime -ge $taskRecordStart) {
        $taskStatus = Get-Content -LiteralPath $taskStatusPath -Raw
        if ($taskStatus -ne $taskLastStatus) {
            Write-TaskProgress $taskStatus.Trim()
            $taskLastStatus = $taskStatus
        }
        if ($taskStatus.StartsWith('ERROR')) { throw $taskStatus }
        if ($taskStatus.StartsWith('COMPLETE')) { break }
    }
    if ((Get-Date) -gt $taskDeadline) { throw 'Timed out waiting for combo recording.' }
    Start-Sleep -Seconds 2
}
Invoke-TaskJob 'status'
$taskStatus = Get-Content -LiteralPath (Join-Path $taskJobs 'status') -Raw
if (!$taskStatus.StartsWith('playing=False')) { throw ('Editor did not restore Edit mode: ' + $taskStatus) }
Write-TaskProgress ('COMPLETE ' + $taskStatus)
