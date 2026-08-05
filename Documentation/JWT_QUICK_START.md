# JWT Authentication - Quick Start Guide

## 🚀 Getting Started

### 1. Start the Application
```bash
dotnet run
```

Application runs on: `http://localhost:5007`

### 2. Get a JWT Token
curl -X POST http://localhost:5007/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}```bash
'
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "admin",
  "expiresAt": "2026-06-15T15:30:45Z"
}
```

### 3. Use the Token to Access Protected Endpoints
```bash
# Dashboard endpoint
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5007/api/dashboard/executive

# Queue endpoint
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5007/api/queue/queue-summary?queue=7
```

## 📋 Protected vs Unprotected Endpoints

| Type | Endpoint | JWT Required |
|------|----------|:-------------:|
| Auth | `/api/auth/login` | ❌ |
| Auth | `/api/auth/refresh` | ✅ |
| Dashboard | `/api/dashboard/*` | ✅ |
| Queue | `/api/queue/*` | ✅ |
| MCP | `/mcp/*` | ❌ |
| Health | `GET /` | ❌ |

## 🔧 Configuration

**File:** `appsettings.json`

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

### Change Secret Key for Production
```bash
# Generate a secure random key (on Linux/Mac):
openssl rand -base64 32

# Or use PowerShell (on Windows):
[Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Maximum 256) }))
```

## 📝 Testing with Postman / Thunder Client

### Create Request 1: Login
- **Method:** POST
- **URL:** `http://localhost:5007/api/auth/login`
- **Headers:** `Content-Type: application/json`
- **Body:**
```json
{
  "username": "admin",
  "password": "admin123"
}
```

### Create Request 2: Get Dashboard (Using Token)
- **Method:** GET
- **URL:** `http://localhost:5007/api/dashboard/executive`
- **Headers:** `Authorization: Bearer {{token}}`
  (Replace {{token}} with the token from Login response)

## 🔄 Token Refresh
```bash
curl -X POST http://localhost:5007/api/auth/refresh \
  -H "Authorization: Bearer YOUR_CURRENT_TOKEN"
```

## ❌ Error Responses

### 401 Unauthorized (No Token or Invalid Token)
```json
{
  "error": "Unauthorized",
  "message": "Invalid or expired token"
}
```

### 403 Forbidden (Token Valid but Not Authorized)
```json
{
  "error": "Forbidden",
  "message": "Access denied"
}
```

## ⚙️ Implementation Summary

| Component | Purpose | Location |
|-----------|---------|----------|
| `AuthService` | Generates and validates JWT tokens | Services/AuthService.cs |
| `AuthController` | Login and refresh endpoints | Controllers/AuthController.cs |
| `AuthRequest` | Login request model | Models/AuthRequest.cs |
| `AuthResponse` | Login response model | Models/AuthResponse.cs |
| `IAuthService` | Authentication interface | Interfaces/IAuthService.cs |

## 🛠️ What's Protected?

### ✅ Now Requires JWT:
- Dashboard Controller (all endpoints)
- Queue Controller (all endpoints)

### ❌ Still No JWT Required:
- Queue7PollingService (background job)
- SabreSessionService
- Repositories (data layer)
- MCP Server endpoints
- Health check endpoint

## 📚 For More Details
See: [JWT_AUTHENTICATION.md](JWT_AUTHENTICATION.md)

## ⚠️ Production Checklist

- [ ] Change JWT secret key in appsettings.json
- [ ] Implement real user validation (use database)
- [ ] Hash passwords with bcrypt/PBKDF2
- [ ] Enable HTTPS
- [ ] Add rate limiting to login endpoint
- [ ] Implement role-based access control (RBAC)
- [ ] Add logging for authentication attempts
- [ ] Implement account lockout after failed attempts
- [ ] Store secrets in Azure Key Vault or environment variables
- [ ] Add token refresh token rotation
