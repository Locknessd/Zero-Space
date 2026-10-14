param(
    [Parameter(Mandatory = $true)] [string] $Job,
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$taskJobs = Join-Path (Get-Location) 'Temp/FrankRetarget'
New-Item -ItemType Directory -Force -Path $taskJobs | Out-Null
$taskRequest = Join-Path $taskJobs 'request'
if (Test-Path -LiteralPath $taskRequest) {
    throw 'A Unity job is already pending. Wait for it before requesting another.'
}
$taskStarted = Get-Date
$taskPending = Join-Path $taskJobs 'request.pending'
[IO.File]::WriteAllText($taskPending, $Job)
Move-Item -LiteralPath $taskPending -Destination $taskRequest
Write-Output ('Requested ' + $Job + ' at ' + $taskStarted.ToString('HH:mm:ss'))
while (((Get-Date) - $taskStarted).TotalSeconds -lt $TimeoutSeconds) {
    $taskError = Join-Path $taskJobs 'error'
    if ((Test-Path -LiteralPath $taskError) -and
        (Get-Item -LiteralPath $taskError).LastWriteTime -ge $taskStarted) {
        Get-Content -LiteralPath $taskError -Raw
        exit 1
    }
    $taskDone = Join-Path $taskJobs 'done'
    if ((Test-Path -LiteralPath $taskDone) -and
        (Get-Item -LiteralPath $taskDone).LastWriteTime -ge $taskStarted) {
        $taskResult = Get-Content -LiteralPath $taskDone -Raw
        if ($taskResult.Trim() -eq ($Job + ' completed')) {
            Write-Output $taskResult
            exit 0
        }
    }
    Start-Sleep -Seconds 1
}
Write-Output ('Timed out waiting for ' + $Job + '. A pending job is not cancelled or duplicated.')
exit 2
