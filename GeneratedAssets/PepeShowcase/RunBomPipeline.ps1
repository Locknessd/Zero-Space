param([switch] $SkipValidation)

$ErrorActionPreference = 'Stop'
$taskOutput = Join-Path (Get-Location) 'GeneratedAssets/PepeShowcase'
$taskJobs = Join-Path (Get-Location) 'Temp/FrankRetarget'
$taskLog = Join-Path $taskOutput 'BomPipeline.txt'
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

if (!$SkipValidation) { Invoke-TaskJob 'ValidateBomPepeTrailer' }
$taskRecordStart = Get-Date
Invoke-TaskJob 'RecordBomPepeTrailer'
$taskDeadline = (Get-Date).AddMinutes(25)
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
    if ((Get-Date) -gt $taskDeadline) { throw 'Timed out waiting for BOM recording.' }
    Start-Sleep -Seconds 2
}
Invoke-TaskJob 'status'
$taskStatus = Get-Content -LiteralPath (Join-Path $taskJobs 'status') -Raw
if (!$taskStatus.StartsWith('playing=False')) { throw ('Editor did not restore Edit mode: ' + $taskStatus) }
Write-TaskProgress ('COMPLETE ' + $taskStatus)
