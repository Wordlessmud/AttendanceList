[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$CertificateThumbprint,

    [switch]$Unsigned
)

$ErrorActionPreference = "Stop"

if ($Unsigned -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Use either -CertificateThumbprint or -Unsigned, not both."
}

if (-not $Unsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Provide -CertificateThumbprint for a locally signed package, or use -Unsigned for Store/Artifact Signing."
}

$project = Join-Path $PSScriptRoot "..\AttendanceList\AttendanceList.csproj"
$arguments = @(
    "publish",
    $project,
    "-f", "net10.0-windows10.0.19041.0",
    "-c", "Release",
    "-p:RuntimeIdentifierOverride=$RuntimeIdentifier",
    "-p:WindowsPackageType=Package"
)

if ($Unsigned) {
    $arguments += "-p:AppxPackageSigningEnabled=false"
}
else {
    $arguments += @(
        "-p:AppxPackageSigningEnabled=true",
        "-p:PackageCertificateThumbprint=$CertificateThumbprint"
    )
}

Write-Host "dotnet $($arguments -join ' ')"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MSIX publish failed with exit code $LASTEXITCODE."
}

$packageRoot = Join-Path $PSScriptRoot "..\AttendanceList\bin\Release\net10.0-windows10.0.19041.0\$RuntimeIdentifier\AppPackages"
if (Test-Path $packageRoot) {
    Write-Host "Packages: $((Resolve-Path $packageRoot).Path)"
}
