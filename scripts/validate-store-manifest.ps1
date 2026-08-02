[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\AttendanceList\Platforms\Windows\Package.appxmanifest"),

    [switch]$AllowDevelopmentIdentity
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ManifestPath)) {
    throw "Package manifest not found: $ManifestPath"
}

[xml]$manifest = Get-Content -Raw -Path $ManifestPath
$namespaces = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaces.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$namespaces.AddNamespace("rescap", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")

$target = $manifest.SelectSingleNode(
    "/f:Package/f:Dependencies/f:TargetDeviceFamily[@Name='Windows.Desktop']",
    $namespaces)
if ($null -eq $target) {
    throw "TargetDeviceFamily must be Windows.Desktop for this .NET MAUI desktop package."
}

$restricted = @($manifest.SelectNodes("/f:Package/f:Capabilities/rescap:Capability", $namespaces))
if ($restricted.Count -ne 1 -or $restricted[0].GetAttribute("Name") -ne "runFullTrust") {
    $declared = ($restricted | ForEach-Object { $_.GetAttribute("Name") }) -join ", "
    throw "The only expected restricted capability is runFullTrust. Found: $declared"
}

$unexpectedGeneralCapabilities = @(
    $manifest.SelectNodes("/f:Package/f:Capabilities/f:Capability", $namespaces)
)
if ($unexpectedGeneralCapabilities.Count -gt 0) {
    $declared = ($unexpectedGeneralCapabilities | ForEach-Object { $_.GetAttribute("Name") }) -join ", "
    throw "Review unexpected general capabilities before Store submission: $declared"
}

$identity = $manifest.SelectSingleNode("/f:Package/f:Identity", $namespaces)
if ($null -eq $identity) {
    throw "The manifest has no package Identity."
}

if (-not $AllowDevelopmentIdentity
    -and ($identity.GetAttribute("Name") -eq "com.local.attendancelist"
        -or $identity.GetAttribute("Publisher") -eq "CN=AttendanceList")) {
    throw "The manifest still uses the local development identity. Associate the project with the reserved Partner Center product, then run this check again. Use -AllowDevelopmentIdentity only for local test packages."
}

Write-Host "Store manifest validation passed."
Write-Host "Identity: $($identity.GetAttribute('Name'))"
Write-Host "Publisher: $($identity.GetAttribute('Publisher'))"
Write-Host "Target: $($target.GetAttribute('Name'))"
Write-Host "Restricted capability: runFullTrust"
