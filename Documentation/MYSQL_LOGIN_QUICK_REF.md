# MySQL Database Login - Quick Reference

## 🚀 Quick Start (5 minutes)

### 1. Create Users Table
Run this SQL on your `systemaudit` database:
```sql
CREATE TABLE `Users` (
  `Id` INT AUTO_INCREMENT PRIMARY KEY,
  `Username` VARCHAR(100) NOT NULL UNIQUE,
  `Email` VARCHAR(255),
  `PasswordHash` VARCHAR(255),
  `Password` VARCHAR(255),
  `IsActive` BOOLEAN DEFAULT true,
  `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `LastLogin` DATETIME
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

### 2. Insert Test User
```sql
-- Password: admin123
INSERT INTO Users (Username, PasswordHash) 
VALUES ('admin', SHA2('admin123', 256));
```

### 3. Enable in Config
Edit `appsettings.json`:
```json
{
  "Auth": {
    "UseDatabase": true,
    "UserTable": "Users"
  },
  "ConnectionStrings": {
    "SkyOpsDBconnection": "server=192.168.10.113;port=26033;database=systemaudit;user=wcusr;password=wcusr123;"
  }
}
```

### 4. Login
```bash
curl -X POST http://localhost:5007/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "admin",
  "expiresAt": "2026-06-15T16:45:00Z"
}
```

---

## 📝 Configuration Reference

| Setting | File | Value | Notes |
|---------|------|-------|-------|
| **Enable DB Auth** | appsettings.json | `"Auth": { "UseDatabase": true }` | Set to `false` for simple auth |
| **User Table** | appsettings.json | `"Auth": { "UserTable": "Users" }` | Must exist in DB |
| **Connection** | appsettings.json | `ConnectionStrings:SkyOpsDBconnection` | Your MySQL server |

---

## 🔐 Password Management

### Generate SHA256 Hash

**PowerShell:**
```powershell
$password = "mypassword123"
$hash = [BitConverter]::ToString(
  [System.Security.Cryptography.SHA256]::Create().ComputeHash(
    [System.Text.Encoding]::UTF8.GetBytes($password)
  )
).Replace("-", "").ToLower()
Write-Host "Hash: $hash"
```

**MySQL:**
```sql
SELECT SHA2('mypassword123', 256) AS PasswordHash;
```

### Insert User with Hashed Password
```sql
INSERT INTO Users (Username, Email, PasswordHash) 
VALUES ('newuser', 'newuser@example.com', SHA2('newpassword456', 256));
```

---

## 🧪 Testing

### Test with cURL
```bash
# Login
TOKEN=$(curl -s -X POST http://localhost:5007/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' \
  | jq -r '.token')

# Use token
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:5007/api/dashboard/executive
```

### Test with Postman
1. **Request 1 - Login**
   - URL: `POST http://localhost:5007/api/auth/login`
   - Body: `{"username":"admin","password":"admin123"}`
   
2. **Copy token from response**

3. **Request 2 - Dashboard (Protected)**
   - URL: `GET http://localhost:5007/api/dashboard/executive`
   - Header: `Authorization: Bearer {token_from_step2}`

---

## 🛠️ Database Queries

### List All Users
```sql
SELECT Id, Username, Email, IsActive, CreatedAt FROM Users;
```

### Find User by Username
```sql
SELECT * FROM Users WHERE Username = 'admin';
```

### Change Password
```sql
UPDATE Users SET PasswordHash = SHA2('newpassword', 256) 
WHERE Username = 'admin';
```

### Deactivate User
```sql
UPDATE Users SET IsActive = false WHERE Username = 'admin';
```

### Delete User
```sql
DELETE FROM Users WHERE Username = 'admin';
```

---

## ❌ Disable DB Authentication

Set in `appsettings.json`:
```json
{
  "Auth": {
    "UseDatabase": false
  }
}
```

Now any non-empty username/password works (for dev/testing only).

---

## 🔍 Troubleshooting

### "Invalid credentials" for existing user

1. Check password hash in DB:
```sql
SELECT Username, PasswordHash FROM Users WHERE Username = 'admin';
```

2. Verify hash matches password:
```sql
SELECT SHA2('admin123', 256) AS Expected;
-- Compare with PasswordHash from above
```

3. Re-insert with correct hash:
```sql
UPDATE Users SET PasswordHash = SHA2('admin123', 256) 
WHERE Username = 'admin';
```

### Connection refused to SkyOpsDBconnection

1. Test MySQL connection:
```bash
mysql -h 192.168.10.113 -P 26033 -u wcusr -p systemaudit
```

2. Verify connection string in appsettings.json

### Table doesn't exist error

Run the SQL from [setup_users_table.sql](data/setup_users_table.sql)

---

## 📚 Full Documentation

- [MYSQL_LOGIN_SETUP.md](MYSQL_LOGIN_SETUP.md) - Complete setup guide
- [JWT_AUTHENTICATION.md](JWT_AUTHENTICATION.md) - JWT token details
- [setup_users_table.sql](data/setup_users_table.sql) - Ready-to-run SQL

---

## 🎯 Default Test Users

| Username | Password | Hash |
|----------|----------|------|
| admin | admin123 | `SHA2('admin123', 256)` |
| operator | operator456 | `SHA2('operator456', 256)` |
| viewer | viewer789 | `SHA2('viewer789', 256)` |

Insert all three:
```sql
INSERT INTO Users (Username, Email, PasswordHash, IsActive) VALUES 
  ('admin', 'admin@example.com', SHA2('admin123', 256), true),
  ('operator', 'operator@example.com', SHA2('operator456', 256), true),
  ('viewer', 'viewer@example.com', SHA2('viewer789', 256), true);
```

---

## 📞 Related Files

- [Services/AuthService.cs](Services/AuthService.cs) - Login logic
- [Controllers/AuthController.cs](Controllers/AuthController.cs) - API endpoints
- [appsettings.json](appsettings.json) - Configuration
- [Models/AuthRequest.cs](Models/AuthRequest.cs) - Login request model
- [Models/AuthResponse.cs](Models/AuthResponse.cs) - Token response model
