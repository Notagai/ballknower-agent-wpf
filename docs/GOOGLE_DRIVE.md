# Google Drive Integration

Ballknower can connect to one Google Drive account and let the agent search, read, and make user-confirmed changes to Drive files.

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

During development, Google may restrict OAuth access to accounts listed as test users for the Cloud project.

## Connection behavior

- **Connect Google Drive** starts OAuth explicitly from Settings.
- **Test Connection** checks the current Drive authorization.
- **Disconnect** removes the local encrypted OAuth token data.
- If the agent tries to use Drive while it is not connected, the Drive tools return a clear error telling the user to connect Drive in Settings.
- OAuth failures and Drive API failures are shown in Settings instead of being silently ignored.
- The current authorization requests the broad Drive scope so the agent can see the user's Drive rather than only files explicitly selected for the app.

## Read vs. write behavior

Ballknower can read/search Drive without a confirmation dialog. **Every Drive mutation is confirmation-gated in the application before the Google API request is made.**

Supported confirmed changes currently include:

- Create a text file.
- Update a supported plain-text file.
- Rename a file.
- Move a file to another Drive folder.
- Delete a file.

The confirmation dialog shows the requested operation and relevant file ID/name/content summary before execution. Cancelling the dialog returns a failed tool result to the model, and Ballknower does not retry the operation automatically.

Google Workspace files such as Docs are not modified as plain text by the current write tool. They can still be renamed, moved, or deleted when the connected account has permission.

## Local security

The OAuth refresh-token data is stored locally using Windows DPAPI with the current Windows user scope. OAuth client JSON files and token data should not be committed to the repository.

The current implementation requests the Google Drive `drive` scope because the agent needs broad Drive visibility plus write capability. Google classifies `drive` and `drive.readonly` as restricted scopes. Public distribution therefore requires careful review of Google's OAuth verification and restricted-scope requirements. Google's current documentation also says that if restricted-scope data is stored on servers or transmitted, a security assessment is required; Ballknower's current token/data storage is local. citeturn0search0turn0search4

For a public release, this broad-access design should be documented clearly in the consent screen and privacy documentation. A narrower `drive.file` design is another option, but it would not provide the same "see all of Drive" behavior. citeturn0search0

## Current Drive tools

### `drive_search`

Searches Drive files by name and returns file metadata.

### `drive_read`

Reads a selected file. Google Docs are exported as plain text. Other non-Google files are read up to the current 2 MB limit. Unsupported Google Workspace file types return an error.

### `drive_write`

Makes a Drive change after application-level user confirmation. Supported operations are `create_text`, `update_text`, `rename`, `move`, and `delete`.

## Troubleshooting

### OAuth client file not found

Make sure the JSON file is in:

`%LOCALAPPDATA%\Ballknower\`

and uses either supported filename pattern.

### Google says access is denied / app is limited to test users

Add the Google account you are using with Ballknower to the OAuth app's configured **Test users** while the app is in testing mode.

### School or managed Google account cannot connect

A Google Workspace administrator can restrict third-party OAuth applications or API access. Ballknower cannot override those organization policies.

### Google says the app is unverified

That can happen during development when the OAuth app requests scopes that require verification. Review Google's current OAuth verification requirements before distributing the app publicly.

See Google's Drive authorization guidance for the current scope and verification requirements.
