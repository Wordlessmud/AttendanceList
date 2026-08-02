[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$MsixPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$SignToolPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ArtifactSigningDlibPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$MetadataPath
)

$ErrorActionPreference = "Stop"

$arguments = @(
    "sign",
    "/v",
    "/debug",
    "/fd", "SHA256",
    "/tr", "http://timestamp.acs.microsoft.com",
    "/td", "SHA256",
    "/dlib", (Resolve-Path $ArtifactSigningDlibPath).Path,
    "/dmdf", (Resolve-Path $MetadataPath).Path,
    (Resolve-Path $MsixPath).Path
)

& (Resolve-Path $SignToolPath).Path @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Artifact Signing failed with exit code $LASTEXITCODE."
}

& (Resolve-Path $SignToolPath).Path verify /pa /v (Resolve-Path $MsixPath).Path
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed with exit code $LASTEXITCODE."
}
