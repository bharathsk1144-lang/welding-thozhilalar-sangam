$ErrorActionPreference = 'Stop'
$target = Join-Path (Join-Path $PSScriptRoot '..\..') 'artifacts\cloudinary-test'
$target = [System.IO.Path]::GetFullPath($target)
New-Item -ItemType Directory -Path $target -Force | Out-Null
$startedUtc = [DateTime]::UtcNow
$written = 0

foreach ($name in 'Cloudinary__CloudName', 'Cloudinary__ApiKey', 'Cloudinary__ApiSecret') {
    $value = Read-Host "Enter $name" -AsSecureString
    if ($value.Length -eq 0) { throw "$name was empty; setup stopped." }
    try {
        ConvertFrom-SecureString $value |
            Set-Content -LiteralPath (Join-Path $target "$name.dpapi") -NoNewline
        $saved = Get-Item -LiteralPath (Join-Path $target "$name.dpapi")
        if ($saved.LastWriteTimeUtc -lt $startedUtc.AddSeconds(-1)) {
            throw "$name was not refreshed in the expected settings folder."
        }
        $written++
    }
    finally { $value.Dispose() }
}

if ($written -ne 3) { throw "Not all encrypted Cloudinary test settings were refreshed." }
Write-Host "Encrypted Cloudinary test files refreshed: $written"
Write-Host "Settings folder: $target"
Write-Host "Refresh UTC: $($startedUtc.ToString('yyyy-MM-dd HH:mm:ss'))"
