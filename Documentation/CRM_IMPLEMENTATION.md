# SkyOps CRM implementation

The CRM uses the existing MySQL database configured by `ConnectionStrings:SkyOpsDBconnection`. Run `Database/crm_schema.sql` against that database before enabling the durable CRM APIs.

## User secrets

Do not put SMTP passwords in `appsettings.json`, Angular, or source control.

```powershell
dotnet user-secrets init
dotnet user-secrets set "EmailNotification:Password" "<legacy-notification-password>"
dotnet user-secrets set "ConnectionStrings:SkyOpsDBconnection" "<mysql-connection-string>"
```

The CRM SMTP settings API encrypts the supplied password with ASP.NET Core Data Protection before storing it in `SmtpConfigurations.EncryptedPassword`. The password is never returned by `GET /api/settings/smtp`.

## API surface

- `GET /api/crm/emails`
- `GET /api/crm/emails/{id}`
- `GET /api/crm/cases/{caseId}`
- `GET /api/crm/cases/{caseId}/timeline`
- `POST /api/crm/cases/{caseId}/messages`
- `POST /api/crm/cases/{caseId}/actions`
- `GET /api/crm/customers/{customerId}`
- `POST /api/crm/cases/{caseId}/action-links`
- `POST /api/customer-action/{token}`
- `POST /api/email/send`
- `POST /api/settings/smtp/test`
- `GET /api/settings/smtp`
- `PUT /api/settings/smtp`

CRM messages and actions are committed to MySQL before `ReceiveMessage`, `ReceiveAction`, and `CaseUpdated` are published to the `/hubs/crm` SignalR case group.

Secure action links contain a random opaque token. Only its SHA-256 hash is stored, it expires, and it is consumed once inside a transaction after action permission validation.
