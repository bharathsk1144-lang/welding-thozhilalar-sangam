$ErrorActionPreference = 'Stop'
$target = Join-Path (Join-Path $PSScriptRoot '..\..') 'artifacts\cloudinary-test'
$target = [System.IO.Path]::GetFullPath($target)
New-Item -ItemType Directory -Path $target -Force | Out-Null

foreach ($name in 'Cloudinary__CloudName', 'Cloudinary__ApiKey', 'Cloudinary__ApiSecret') {
    $value = Read-Host "Enter $name" -AsSecureString
    if ($value.Length -eq 0) { throw "$name was empty; setup stopped." }
    try {
        ConvertFrom-SecureString $value |
            Set-Content -LiteralPath (Join-Path $target "$name.dpapi") -NoNewline
    }
    finally { $value.Dispose() }
}

$count = (Get-ChildItem -LiteralPath $target -Filter '*.dpapi' -File | Measure-Object).Count
Write-Host "Encrypted Cloudinary test files saved: $count"
