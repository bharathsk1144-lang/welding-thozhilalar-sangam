$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$directory = Join-Path $root 'artifacts\cloudinary-test'
$names = 'Cloudinary__CloudName', 'Cloudinary__ApiKey', 'Cloudinary__ApiSecret'

foreach ($name in $names) {
    $path = Join-Path $directory "$name.dpapi"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing encrypted test setting: $name. Run SetCloudinaryTestSecrets.ps1 first."
    }
    $secure = ConvertTo-SecureString (Get-Content -LiteralPath $path -Raw)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        [Environment]::SetEnvironmentVariable($name,
            [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer), 'Process')
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $secure.Dispose()
    }
}

$env:WTS_CLOUDINARY_LIVE_TEST = '1'
$restoreTool = Join-Path $root 'artifacts\mongodb-tools\extracted\mongodb-database-tools-windows-x86_64-100.20.1\bin\mongorestore.exe'
if (Test-Path -LiteralPath $restoreTool -PathType Leaf) { $env:WTS_CLOUDINARY_RESTORE_DRILL = '1' }
$testExitCode = 0
try {
    Push-Location $root
    try {
        dotnet run --project tests/PrivateStorageChecks/PrivateStorageChecks.csproj -c Release --no-restore
        $testExitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    Remove-Item Env:WTS_CLOUDINARY_LIVE_TEST -ErrorAction SilentlyContinue
    Remove-Item Env:WTS_CLOUDINARY_RESTORE_DRILL -ErrorAction SilentlyContinue
    foreach ($name in $names) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
}
if ($testExitCode -ne 0) {
    [Console]::Error.WriteLine("Live Cloudinary test failed (exit code $testExitCode).")
    exit 1
}
