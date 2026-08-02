# Windows MSIX release

The Windows reminder scheduler requires package identity. Release builds are therefore configured as packaged MSIX builds and the unpackaged AppUserModelID fallback has been removed.

## Local test package

Create a temporary code-signing certificate whose subject exactly matches the manifest publisher (`CN=AttendanceList`), copy its thumbprint, and run:

```powershell
.\scripts\publish-msix.ps1 -RuntimeIdentifier win-x64 -CertificateThumbprint YOUR_THUMBPRINT
```

Install the generated `.msix`. A self-signed certificate is for development only; the target computer must trust it before installation.

## Unsigned package for Store or external signing

```powershell
.\scripts\publish-msix.ps1 -RuntimeIdentifier win-x64 -Unsigned
```

Do not send that unsigned package directly to users. Submit it through the Microsoft Store workflow or sign it with a publicly trusted code-signing identity.

## Microsoft Store

This is the simplest public route. Create a Partner Center developer account, reserve the app name, and associate the Visual Studio project with the Store. Association updates the package identity in `Package.appxmanifest`. Build the Store submission package and upload it to Partner Center. Microsoft signs the certified MSIX for Store delivery.

Never keep the placeholder publisher `CN=AttendanceList` after Store association. Use the exact publisher identity supplied by Partner Center.

## Azure Artifact Signing

1. Create an Azure Artifact Signing account.
2. Complete a **Public Trust** identity validation.
3. Create a **Public Trust** certificate profile.
4. Ensure the `Publisher` in `Platforms/Windows/Package.appxmanifest` exactly matches the certificate subject shown by the profile.
5. Install the Artifact Signing client tools and authenticate to Azure.
6. Build an unsigned MSIX.
7. Copy `docs/artifact-signing-metadata.sample.json`, fill in its values, and sign:

```powershell
.\scripts\sign-msix-artifact-signing.ps1 `
  -MsixPath .\path\AttendanceList.msix `
  -SignToolPath 'C:\path\x64\signtool.exe' `
  -ArtifactSigningDlibPath 'C:\path\x64\Azure.CodeSigning.Dlib.dll' `
  -MetadataPath .\artifact-signing-metadata.json
```

Timestamping is required. The included script uses Microsoft's Artifact Signing timestamp service and verifies the result after signing.

## Traditional CA certificate

A CA-issued OV code-signing certificate is another option for direct downloads. The certificate subject and manifest `Publisher` must match exactly. Modern public code-signing private keys are commonly held by the CA's cloud service or a hardware token, so follow the selected CA's SignTool integration instructions.

## Alarm troubleshooting

Scheduling failures are written to `notification-errors.log` in the app data directory. The user-facing error now includes the exception type and HRESULT. Launch the installed application from the Start menu; do not run the executable from the publish folder.
