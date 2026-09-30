# Google Drive Integration

Ballknower can connect to a single Google Drive account and let the agent search and read files through dedicated tools.

## Setup

Google Drive is optional. Ballknower does not start Google authentication automatically.

1. Create a Google Cloud project and enable the Google Drive API.
2. Configure the OAuth consent screen for the project.
3. Create an OAuth 2.0 client with application type **Desktop app**.
4. Download the OAuth client JSON.
5. Put the JSON file in:

   `%LOCALAPPDATA%\Ballknower\`

   Ballknower accepts either the conventional:

   `google-client-secret.json`

   or Google's downloaded Desktop client filename:

   `client_secret_*.apps.googleusercontent.com.json`

6. Open Ballknower and go to **Settings → Google Drive**.
7. Click **Connect Google Drive** and complete the Google sign-in flow.
8. Ballknower runs a Drive API connection test after authentication. The connection is only considered usable when that test succeeds.

## Connection behavior

- **Connect Google Drive** starts OAuth explicitly from Settings.
- **Test Connection** checks the current Drive authorization.
- **Disconnect** removes the local encrypted OAuth token data.
- If the agent tries to use Drive while it is not connected, the Drive tools return a clear error telling the user to connect Drive in Settings.
- OAuth failures and Drive API failures are shown in Settings instead of being silently ignored.

## Local security

The OAuth refresh-token data is stored locally using Windows DPAPI with the current Windows user scope. OAuth client JSON files and token data should not be committed to the repository.

The current implementation requests the Google Drive `drive.readonly` scope because the initial Drive tools need to search existing files and read their contents. Google classifies `drive.readonly` as a restricted scope. Public distribution therefore requires careful review of Google's OAuth verification and restricted-scope requirements.

For a public release, consider redesigning the integration around narrower access such as `drive.file` plus Google Picker where the product requirements allow it.

## Current Drive tools

### `drive_search`

Searches Drive files by name and returns file metadata.

### `drive_read`

Reads a selected file. Google Docs are exported as plain text. Other non-Google files are read up to the current 2 MB limit. Unsupported Google Workspace file types return an error.

## Troubleshooting

### OAuth client file not found

Make sure the JSON file is in:

`%LOCALAPPDATA%\Ballknower\`

and uses either supported filename pattern.

### School or managed Google account cannot connect

A Google Workspace administrator can restrict third-party OAuth applications or API access. Ballknower cannot override those organization policies.

### Google says the app is unverified

That can happen during development when the OAuth app requests scopes that require verification. Review Google's current OAuth verification requirements before distributing the app publicly.

See Google's Drive authorization guidance for the current scope and verification requirements.
