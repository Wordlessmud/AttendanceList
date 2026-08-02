# Microsoft Store submission notes

## Restricted capability: `runFullTrust`

Attendance List declares one restricted Windows capability:

```xml
<rescap:Capability Name="runFullTrust" />
```

Do not remove it merely to clear the Partner Center warning. The Windows target is a packaged .NET MAUI application, which uses the WinUI 3 desktop app model and runs as a medium-integrity desktop process rather than inside AppContainer. Packaged medium-integrity desktop applications are required to declare `runFullTrust`.

`runFullTrust` does **not** mean that the app requests administrator elevation. Attendance List runs with the signed-in user's normal permissions. It does not install a service or driver, inject input, modify protected system locations, or read another user's profile.

The manifest deliberately contains no other restricted capabilities, and its manifest explicitly includes the `Windows.Desktop` target device family.

### Text for Partner Center

Paste the following into the restricted-capability explanation on **Submission options**:

> Attendance List is a packaged .NET MAUI/WinUI 3 desktop application. On Windows, this app model runs as a medium-integrity desktop process rather than an AppContainer process, and Microsoft requires packaged medium-integrity desktop apps to declare the runFullTrust capability. The capability is required by the application model; the app does not request administrator elevation. Attendance List does not install services or drivers, inject input, modify protected system locations, or access another user's data. Its working data is stored in the app's local data directory, and files leave that directory only when the user explicitly exports or shares them.

### Certification notes

Use this shorter wording in the tester notes when useful:

> This is a packaged .NET MAUI/WinUI 3 desktop app. `runFullTrust` is required for the medium-integrity desktop application model and does not request elevation. No administrator account is required to install or use the Store package.

## Validate before packaging

Run:

```powershell
.\scripts\validate-store-manifest.ps1
```

The script verifies that:

- the package targets `Windows.Desktop`;
- the only restricted capability is `runFullTrust`;
- the manifest does not contain unrelated broad-access capabilities;
- the package identity is no longer the local development placeholder before Store submission.

The final identity must be supplied by Partner Center through **Associate App with the Store** in Visual Studio. Do not invent or manually abbreviate the Store publisher value.

## Language behavior

The app defaults to **System default**. It detects Simplified Chinese system cultures (`zh-CN`, `zh-SG`, and `zh-Hans`) and otherwise uses English. Traditional Chinese cultures fall back to English until a dedicated Traditional Chinese translation is added. Users can override the system choice in Settings.

English date formatting follows the user's English regional format, such as `en-AU`, `en-GB`, or `en-US`. Simplified Chinese uses `zh-CN` formatting.
