# JWT Authentication Implementation

## Overview
JWT (JSON Web Token) authentication has been added to the Saber QtestMCP application with the following configuration:

### Protected Endpoints (Require JWT)
- **Angular Dashboard** → `api/dashboard/*` - All Dashboard Controller endpoints
- **API Controllers** → `api/queue/*` - All Queue Controller endpoints

### Unprotected Endpoints (No JWT Required)
- **Queue7PollingService** - Background job continues to work without authentication
- **SabreSessionService** - Continues to operate without JWT validation
- **Repositories** - Data layer operates without authentication
- **MCP Server** → `/mcp/*` - Model Context Protocol endpoints
- **Health Check** → `GET /` - Root endpoint

## Configuration

### 1. NuGet Packages Added
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.2.0" />
```

### 2. JWT Settings (appsettings.json)
```json
{
  "Jwt": {
    "SecretKey": "your-super-secret-key-change-this-in-production-at-least-32-characters-long",
    "ExpiryMinutes": 60,
    "Issuer": "SaberQtestMCP",
    "Audience": "SaberQtestMCP"
  }
}
```

⚠️ **IMPORTANT**: Change `SecretKey` to a strong secret in production (minimum 32 characters).

## API Endpoints

### 1. Login Endpoint
**POST** `/api/auth/login`

Request:
```json
{
  "username": "your-username",
  "password": "your-password"
}
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "your-username",
  "expiresAt": "2026-06-15T14:30:00Z"
}
```

### 2. Refresh Token Endpoint
**POST** `/api/auth/refresh`

Header:
```
Authorization: Bearer {token}
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "your-username",
  "expiresAt": "2026-06-15T14:30:00Z"
}
```

### 3. Protected Dashboard Endpoints (Require JWT)
All dashboard endpoints now require authentication:
- `GET /api/dashboard/executive`
- `GET /api/dashboard/queue-performance`
- `GET /api/dashboard/pcc-performance`
- `GET /api/dashboard/flight-status`
- `GET /api/dashboard/critical`
- `GET /api/dashboard/delay-analysis`
- `GET /api/dashboard/flight-impact`
- `GET /api/dashboard/pnr-analysis`
- `GET /api/dashboard/recommendations`
- `GET /api/dashboard/operational`
- `GET /api/dashboard/management`

### 4. Protected Queue Endpoints (Require JWT)
- `GET /queue-summary?queue=7`
- `GET /delay-summary?queue=7`

## Usage Flow

### Step 1: Authenticate
```bash
curl -X POST http://localhost:5007/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'
```

### Step 2: Use Token to Access Protected Resources
```bash
curl -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  http://localhost:5007/api/dashboard/executive
```

### Step 3: Refresh Token Before Expiration
```bash
curl -X POST http://localhost:5007/api/auth/refresh \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

## Implementation Details

### Files Created
1. **Models/AuthRequest.cs** - Login request model
2. **Models/AuthResponse.cs** - Login response model
3. **Interfaces/IAuthService.cs** - Authentication service interface
4. **Services/AuthService.cs** - JWT token generation and validation
5. **Controllers/AuthController.cs** - Login and refresh endpoints

### Files Modified
1. **Program.cs** - Added JWT authentication middleware
2. **SaberQtestMCP.csproj** - Added JWT NuGet packages
3. **Controllers/DashboardController.cs** - Added `[Authorize]` attribute
4. **Controllers/QueueController.cs** - Added `[Authorize]` attribute
5. **appsettings.json** - Added JWT configuration

## Token Claims
Each JWT token includes the following claims:
- `sub` (Subject) - Username
- `nameid` (Name Identifier) - Username
- `iss` (Issuer) - "SaberQtestMCP"
- `aud` (Audience) - "SaberQtestMCP"
- `exp` (Expiration) - Token expiration time
- `iat` (Issued At) - Token creation time

## Security Notes

1. **Production Secret Key**: Change the JWT secret key in `appsettings.json` to a strong, randomly generated string (minimum 32 characters).

2. **User Validation**: Currently, `AuthService.ValidateCredentials()` accepts any non-empty username/password combination for demonstration. In production:
   - Implement proper user database lookup
   - Hash passwords using bcrypt, PBKDF2, or Argon2
   - Implement account lockout after failed attempts
   - Add rate limiting

3. **Token Expiration**: Tokens expire after 60 minutes (configurable via `Jwt:ExpiryMinutes`).

4. **HTTPS**: Use HTTPS in production to prevent token interception.

5. **Environment-Specific Secrets**: Store production secrets in environment variables or Azure Key Vault, not in appsettings.json.

## Testing

### Using Postman or ThunderClient

1. **Get Token**
   - POST: `http://localhost:5007/api/auth/login`
   - Body: `{"username":"testuser","password":"testpass"}`
   - Copy the `token` value

2. **Call Protected Endpoint**
   - GET: `http://localhost:5007/api/dashboard/executive`
   - Header: `Authorization: Bearer {token}`

### Disabling Auth for Specific Endpoints

If you need to disable authorization for specific endpoints, use the `[AllowAnonymous]` attribute:

```csharp
[AllowAnonymous]
[HttpGet("public-endpoint")]
public async Task<IActionResult> PublicEndpoint()
{
    // This endpoint will not require JWT
}
```

## Troubleshooting

### 401 Unauthorized
- Ensure token is included in Authorization header
- Token format should be: `Bearer {token}`
- Check token expiration

### 403 Forbidden
- User is authenticated but not authorized for the resource
- Check role-based access control (RBAC) if implemented

### Invalid Token Error
- Secret key may not match
- Token may have expired
- Token may have been modified

## Future Enhancements

1. **Role-Based Access Control (RBAC)**: Add roles (Admin, User, Viewer) to fine-tune permissions
2. **Database User Store**: Replace in-memory validation with database-backed user authentication
3. **Password Hashing**: Implement bcrypt or PBKDF2 for secure password storage
4. **Rate Limiting**: Add login attempt rate limiting
5. **Token Refresh Strategy**: Implement refresh token rotation for enhanced security
6. **API Keys**: Add support for API key authentication for service-to-service communication
