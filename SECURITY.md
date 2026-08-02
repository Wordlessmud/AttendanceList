# Security policy

Attendance List stores attendance and related records locally. Security reports involving data exposure, unsafe update or installation behaviour, notification abuse, or dependency vulnerabilities are welcome.

## Supported versions

Until the first signed binary release is available, only the latest commit on `master` is supported. After binary releases begin, the latest published version will receive security fixes.

## Reporting a vulnerability

Please do not open a public issue containing exploit details, personal information, real attendance records, database files, signing material or credentials.

Use GitHub's **Report a vulnerability** option under the repository's **Security** tab when private vulnerability reporting is enabled. If that option is unavailable, open a minimal public issue asking the maintainer for a private contact channel without including sensitive technical details.

A useful report includes:

- affected commit or version;
- platform and operating-system version;
- reproduction steps using synthetic data;
- expected and observed behaviour;
- impact assessment;
- suggested mitigation, when known.

## Sensitive files

Never commit or attach:

- `attendance.db3` or database backups containing real records;
- `.pfx`, `.p12`, `.cer` private-key material, keystores or signing tokens;
- passwords, access tokens or cloud signing credentials;
- diagnostic logs containing identifiable information.

## Privacy model

The application is designed to keep working data on the device unless the user explicitly exports or shares it. Removing the application may remove local data. A complete user-facing backup and restore workflow remains planned work.
