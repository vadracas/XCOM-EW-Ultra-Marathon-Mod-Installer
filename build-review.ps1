$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'SetupFrontend\UMMSetup\UMMSetup.csproj'
$projectDir = Split-Path -Parent $project
$upkSource = Join-Path $projectDir 'Resources\UPKUtilsSource'
$expectedDotnetSdk = '9.0.308'
$expectedBytes = 147427014
$expectedSha256 = 'D86387ADDE5B8C09DA28FB8950F7176E1DF836C8D88B2D0E1750C389BCCD85A4'

$currentDotnetSdk = (dotnet --version).Trim()
if ($currentDotnetSdk -ne $expectedDotnetSdk) {
    throw "Expected .NET SDK $expectedDotnetSdk, found $currentDotnetSdk"
}

foreach ($required in @('COPYING', 'LICENSE', 'PatchUPK.cpp', 'DecompressLZO.cpp')) {
    $path = Join-Path $upkSource $required
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Required UPKUtils source file is missing: $path"
    }
}

foreach ($buildDir in @((Join-Path $projectDir 'bin'), (Join-Path $projectDir 'obj'))) {
    if (Test-Path $buildDir) {
        Remove-Item $buildDir -Recurse -Force
    }
}

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$output = Join-Path $projectDir 'bin\Release\net9.0-windows\win-x64\publish\Ultra Marathon Mod Setup.exe'
if (-not (Test-Path $output -PathType Leaf)) {
    throw "Expected output was not produced: $output"
}

$actualBytes = (Get-Item $output).Length
$actualSha256 = (Get-FileHash -Algorithm SHA256 $output).Hash.ToUpperInvariant()

Write-Host "OUTPUT=$output"
Write-Host "BYTES=$actualBytes"
Write-Host "SHA256=$actualSha256"

if ($actualBytes -ne $expectedBytes) {
    throw "Review build byte length mismatch: $actualBytes != $expectedBytes"
}
if ($actualSha256 -ne $expectedSha256) {
    throw "Review build SHA-256 mismatch: $actualSha256 != $expectedSha256"
}

Write-Host 'BYTE_IDENTICAL_REVIEW_BUILD=PASS'
