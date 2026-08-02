# Windows MSIX release

The Windows reminder scheduler requires package identity. Release builds are therefore configured as packaged MSIX builds and the unpackaged AppUserModelID fallback has been removed.

## Current notification limitation

Attendance List does not yet have a publicly trusted Windows code-signing certificate. This has a direct drawback for the reminder/notification function:

- An unpackaged executable has no package identity, so Windows reminder scheduling is intentionally rejected.
- An unsigned MSIX cannot be installed through the normal Windows package installer.
- A self-signed MSIX can be used for development, but every tester must manually install and trust the certificate first.
- Asking general users to trust an unknown self-signed certificate is poor security practice and is not recommended.
- Until Microsoft Store, SignPath Foundation, or another trusted signing provider signs the package, Windows reminders should be described as development or controlled-testing functionality rather than a normal public-release feature.

This does not affect the core attendance, history, reports, exports, or local database. It affects installation of the packaged Windows build and the scheduled reminder feature that depends on trusted package identity.

## Important rule

A deployable MSIX must be signed, and the signing identity must be trusted on the user's Windows device. Do not publish an unsigned MSIX to end users.

The package manifest `Publisher` must exactly match the subject used to sign the package. This includes punctuation, field order and spacing.

## Recommended public distribution routes

### SignPath Foundation for open-source GitHub releases

SignPath Foundation provides free code signing for qualifying open-source projects. This is the preferred route when the signed MSIX should remain downloadable from GitHub Releases.

The project should:

- use an OSI-approved licence for all project-owned components;
- contain no proprietary maintainer-controlled component;
- be actively maintained;
- already have a public release in the form that should be signed;
- have a documented, reviewable build process;
- contain no malware or potentially unwanted behaviour.

Attendance List uses GPL-3.0, but eligibility is determined by SignPath Foundation. Publish the source-first `v0.3.0` release before applying because SignPath requires the project to have already been released.

After approval:

1. Configure the approved GitHub build and SignPath signing workflow.
2. Update `Platforms/Windows/Package.appxmanifest` so `Publisher` exactly matches the subject assigned by SignPath.
3. Build the unsigned MSIX only inside the approved workflow.
4. Submit it to SignPath for signing.
5. Verify and install the returned signed artifact on a clean test account.
6. Attach the signed package and its SHA-256 checksum to a new GitHub release.

Do not guess the SignPath publisher value and do not keep the placeholder `CN=AttendanceList` unless SignPath explicitly assigns that exact subject.

### Microsoft Store

This is the simplest route for users who prefer Store installation and automatic updates. Create a Partner Center developer account, reserve the app name, and associate the Visual Studio project with the Store. Association updates the package identity in `Package.appxmanifest`. Build the Store submission package and upload it to Partner Center. Microsoft signs the certified MSIX for Store delivery.

Never keep the placeholder publisher `CN=AttendanceList` after Store association. Use the exact publisher identity supplied by Partner Center.

The Store signature applies to the Store-distributed package; it does not provide a certificate for signing a separate GitHub-hosted MSIX.

## Local test package

Create a temporary code-signing certificate whose subject exactly matches the manifest publisher (`CN=AttendanceList`), copy its thumbprint, and run:

```powershell
.\scripts\publish-msix.ps1 `
  -RuntimeIdentifier win-x64 `
  -CertificateThumbprint YOUR_THUMBPRINT
```

Install the generated `.msix`. A self-signed certificate is for development only; the target computer must trust it before installation. Do not ask general users to install an unknown root certificate.

## Unsigned package for Store or external signing

```powershell
.\scripts\publish-msix.ps1 `
  -RuntimeIdentifier win-x64 `
  -Unsigned
```

Do not send that unsigned package directly to users. Submit it through the Microsoft Store workflow or an approved signing service.

## Azure Artifact Signing

Azure Artifact Signing is Microsoft's managed signing service, but Public Trust availability is restricted by country and identity type. Confirm current eligibility before designing the release process around it.

When eligible:

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

Timestamping is required. The included script timestamps and verifies the result after signing.

## Traditional CA certificate

A CA-issued OV code-signing certificate is another option for direct downloads. This is generally paid and is unlikely to be the best first route for a free open-source application. The certificate subject and manifest `Publisher` must match exactly. Modern public code-signing private keys are commonly held by the CA's cloud service or a hardware token, so follow the selected CA's SignTool integration instructions.

## Installation testing

Test the exact release artifact, not an earlier local build:

1. Use a clean Windows user account or test machine.
2. Uninstall any earlier package with the same identity when testing a fresh-install path.
3. Install the signed MSIX.
4. Launch it from the Start menu.
5. Complete onboarding and enable notifications.
6. Create a reminder a few minutes in the future.
7. Close the app and confirm that Windows delivers the reminder.
8. Reopen the app and confirm the reminder remains enabled.

## Alarm troubleshooting

Scheduling failures are written to `notification-errors.log` in the app data directory. The user-facing error includes the exception type and HRESULT. Launch the installed application from the Start menu; do not run the executable from the publish folder.

See `docs/RELEASING.md` for the full release checklist.
