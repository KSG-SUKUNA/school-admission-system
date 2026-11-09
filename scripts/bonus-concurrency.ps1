$ErrorActionPreference = "Stop"

$ApiA = "http://localhost:8080"
$ApiB = "http://localhost:8081"
$Capacity = 5
$RequestCount = 25

Write-Host "Waiting for both API processes..."
$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        Invoke-RestMethod -Uri "$ApiA/health" -TimeoutSec 3 | Out-Null
        Invoke-RestMethod -Uri "$ApiB/health" -TimeoutSec 3 | Out-Null
        $ready = $true
        break
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $ready) {
    throw "Both API processes did not become healthy. Check: docker compose --profile bonus ps"
}

$className = "Bonus concurrency $(Get-Date -Format 'yyyyMMddHHmmssfff')"
$classBody = @{ name = $className; totalSeats = $Capacity } | ConvertTo-Json
$createdClass = Invoke-RestMethod -Method Post -Uri "$ApiA/classes" -ContentType "application/json" -Body $classBody
$classId = $createdClass.id

Write-Host "Created $classId with capacity $Capacity. Sending $RequestCount concurrent requests across both API processes..."

$jobs = @()
for ($i = 1; $i -le $RequestCount; $i++) {
    $api = if ($i % 2 -eq 0) { $ApiA } else { $ApiB }
    $studentId = "100000000000000" + ("{0:D2}" -f $i)
    $jobs += Start-Job -ArgumentList $classId, $api, $studentId -ScriptBlock {
        param($TargetClassId, $TargetApi, $TargetStudentId)
        try {
            Invoke-RestMethod -Method Post `
                -Uri "$TargetApi/classes/$TargetClassId/holds" `
                -Headers @{ "X-Student-Id" = $TargetStudentId } `
                -TimeoutSec 30 | Out-Null
            [pscustomobject]@{ StatusCode = 201; Error = "" }
        }
        catch {
            $statusCode = 0
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            [pscustomobject]@{ StatusCode = $statusCode; Error = $_.Exception.Message }
        }
    }
}

try {
    Wait-Job -Job $jobs | Out-Null
    $results = @($jobs | Receive-Job)
}
finally {
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}

$createdCount = @($results | Where-Object { $_.StatusCode -eq 201 }).Count
$conflictCount = @($results | Where-Object { $_.StatusCode -eq 409 }).Count
$unexpected = @($results | Where-Object { $_.StatusCode -notin @(201, 409) })
$finalClass = Invoke-RestMethod -Uri "$ApiA/classes/$classId"

Write-Host "Successful holds: $createdCount"
Write-Host "Conflict responses: $conflictCount"
Write-Host "Database seat counts: held=$($finalClass.heldSeats), confirmed=$($finalClass.confirmedSeats), total=$($finalClass.totalSeats)"

if ($unexpected.Count -gt 0) {
    $unexpected | Format-Table -AutoSize | Out-Host
    throw "One or more requests returned an unexpected status code."
}
if ($createdCount -ne $Capacity) {
    throw "Expected exactly $Capacity successful holds, got $createdCount."
}
if ($conflictCount -ne ($RequestCount - $Capacity)) {
    throw "Expected $($RequestCount - $Capacity) conflict responses, got $conflictCount."
}
if (($finalClass.heldSeats + $finalClass.confirmedSeats) -gt $finalClass.totalSeats) {
    throw "Seat capacity was exceeded."
}

Write-Host "PASS: both backend processes respected the shared PostgreSQL seat limit." -ForegroundColor Green
