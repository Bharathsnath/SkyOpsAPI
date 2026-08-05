# Feat:databaseconnectsformDBcredentials

This document captures the prompt trail and implementation notes for the database credential flow, connection-string loading, and singleton reload behavior.

## Relevant Prompts

### Prompt 4 - MySQL Persistence + 15-Min Polling

```text
i want to update in the my sql database if any chanes in shedule and automatilcy ervey 15 mins and action to take
```

Impact:
- Added MySQL persistence for queue analysis output.
- Introduced background polling so database writes happen automatically on a schedule.
- Established the first shared credential-backed database usage pattern.

### Prompt 6 - Automatic Polling Logs + Change Detection

```text
create log and if any change time / new segment added in data it automaticly update in the database every 15 min i dont want to call tool to exicute the process
```

Impact:
- Added durable logging for background processing.
- Reinforced the need for repeatable database access without manual trigger calls.
- Increased reliance on shared connection credentials across long-lived services.

### Prompt 7 - Sabre SOAP API Source

```text
i want to change getting api
https://webservices.platform.sabre.com
[SabreCommandLLSRQ SOAP envelope using HostCommand Q/7]
dont pnr remove from the queue
```

Impact:
- Switched queue text acquisition to an API-driven source.
- Kept queue processing analysis-only.
- Added more configuration-driven behavior that depends on refreshed credentials and settings.

### Prompt 13 - Database Credentials Reload

```text
now i change database in froent end the singleton shoude be reloaded
```

Impact:
- Identified that singleton credential stores were only loading once at startup.
- Added a reload path so frontend database changes refresh cached credential data.
- Updated long-lived repositories to rebuild their cached connection strings after credential reload.

### Prompt 14 - Separate Connection Store

```text
dont chnage credentail store  i want supprate
```

Impact:
- Preserved the existing PCC credential store unchanged.
- Added a dedicated singleton for connection credential loading from `skyops.wpset_credentialdetails`.
- Kept the connection credential flow separate from the PCC lookup flow.

### Prompt 15 - Separate Connection Model

```text
create and use connection credential model
```

Impact:
- Introduced a dedicated `ConnectionCredential` model.
- Switched the connection store to use the new model instead of `PccCredential`.
- Made the connection credential flow clearer and easier to maintain.

### Prompt 16 - Database-Backed Connections

```text
now i want database connections take for this singleton
```

Impact:
- Changed the singleton to assemble full connection strings from credential rows.
- Mapped connection groups like `master`, `transaction`, and `log` into named connection strings.
- Enabled repositories to resolve database connections from the singleton instead of relying only on `appsettings.json`.

### Prompt 17 - Database Connection Prompt Update

```text
update prompts  and code changesin file name Feat:databaseconnectsformDBcredentials
```

Impact:
- Refreshed this documentation file with the newer prompt trail.
- Captured the code changes that moved database connection resolution into the singleton-backed credential store.
- Kept the feature history aligned with the current codebase state.

### Prompt 11 - Documentation File Request

```text
append documentation with all prompts create file name Feat:databaseconnectsformDBcredentials
```

Impact:
- Created the feature documentation file for the database credential and reload work.
- Collected the relevant prompt history into one place for later reference.

### Prompt 12 - Include Prompt 11 and 12

```text
i want prompt 11,12 also
```

Impact:
- Requested that the missing prompt entries be added to the documentation.
- Confirmed the documentation should include the recent database reload and prompt-log extension steps.

## Implementation Notes

- `ConnectionCredentialStore` now exposes a reload notification when fresh rows are loaded.
- `SettingsService` reloads credential stores after successful create, update, or delete operations.
- `QueueActionRepository` and `DashboardRepository` refresh their cached connection strings when the credential store reloads.
- `ConnectionCredentialStore` builds MySQL connection strings from grouped rows where `TagName` values are `server`, `port`, `database`, `user`, and `password`.
- `QueueActionRepository`, `DashboardRepository`, `UserRepository`, and `SettingsRepository` now prefer the singleton-provided connection strings and fall back to `appsettings.json` only if needed.
- `ConnectionCredentialStore` loads only rows with `ServiceType = 'CON'` from `skyops.wpset_credentialdetails`.

## Files Updated

- [Interfaces/IConnectionCredentialStore.cs](Interfaces/IConnectionCredentialStore.cs)
- [Services/CredentialStore/ConnectionCredentialStore.cs](Services/CredentialStore/ConnectionCredentialStore.cs)
- [Services/SettingsService.cs](Services/SettingsService.cs)
- [Repositories/QueueActionRepository.cs](Repositories/QueueActionRepository.cs)
- [Repositories/DashboardRepository.cs](Repositories/DashboardRepository.cs)
- [Models/ConnectionCredential.cs](Models/ConnectionCredential.cs)
- [Repositories/UserRepository.cs](Repositories/UserRepository.cs)
- [Repositories/SettingsRepository.cs](Repositories/SettingsRepository.cs)
