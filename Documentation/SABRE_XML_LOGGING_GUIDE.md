# Sabre Request/Response Logging to wp_xmllog Database

## Overview
This feature automatically logs all Sabre SOAP API requests and responses to the `wp_xmllog` database table for auditing, debugging, and compliance tracking purposes.

## Implementation

### Components Created

#### 1. **ISabreXmlLogService Interface** (`Interfaces/ISabreXmlLogService.cs`)
Defines the contract for logging Sabre requests and responses:
- `LogSabreRequestResponseAsync()` - Main method to log request/response pairs

#### 2. **SabreXmlLogService Implementation** (`Services/SabreXmlLogService.cs`)
Implements logging logic with the following features:
- Uses `IQueueActionRepository.SaveApiLogAsync()` to persist to database
- **SOAP Envelope Cleaning**: Extracts only the relevant body content from SOAP envelopes to keep logs readable
- **Error Handling**: Gracefully handles logging failures without interrupting main operations
- **Structured Logging**: Uses ILogger for operational logging

#### 3. **Integration in QueueTextSource** (`Services/SaberQueueServices/QueueTextSource.cs`)
Modified to:
- Inject `ISabreXmlLogService` dependency
- Capture all Sabre API requests and responses
- Log both successful and failed requests with HTTP status codes
- Log status as "SUCCESS" or "FAILED" based on response status

#### 4. **Dependency Injection in Program.cs**
Registered service:
```csharp
builder.Services.AddScoped<ISabreXmlLogService, SabreXmlLogService>();
```

#### 5. **Database Configuration in appsettings.json**
Added LogDBConnection:
```json
"LogDBConnection": "server=192.168.10.113;port=26033;database=wcprdb2bukxmllogdb;user=wcusr;password=wcusr123;"
```

## Database Schema

Logs are stored in the `wp_xmllog` table with the following key fields:

| Field | Value |
|-------|-------|
| Log_UPL_VC | UPL ID (tracking) |
| Log_WorkFlow_VC | "QueuePolling" |
| Log_ModuleName_VC | "SabreQueueMCP" |
| Log_ModuleCode_VC | "QUEUE" |
| Log_ClassName_VC | "QueueTextSource" |
| Log_ProcedureName_VC | Host Command (Q/7, I, QXI, etc.) |
| Log_LogCode_VC | Status (SUCCESS/FAILED) |
| Log_LogXML_VC | Cleaned SOAP Request and Response |
| Log_Remarks_VC | PCC, Command, HTTP Status, Status |
| Log_LogDate_DT | UTC Timestamp (IST: +05:30) |

Example Remarks format:
```
PCC:AKBR|Command:Q/7|HTTP:200|Status:SUCCESS
```

## What Gets Logged

### For Every Sabre API Call:
1. **Host Command**: Q/7, Q/379, Q/62, I, QXI, etc.
2. **SOAP Request XML**: Full request sent to Sabre API
3. **SOAP Response XML**: Full response from Sabre API
4. **HTTP Status Code**: 200, 500, etc.
5. **PCC Code**: From configuration (e.g., "AKBR")
6. **Status**: SUCCESS for 2xx responses, FAILED for others
7. **Timestamp**: When the request was made (IST)

## Usage

The logging happens automatically whenever:
- Queue polling requests are made (Q/7, Q/379, Q/62)
- Navigation commands are sent (I, QXI)
- Queue analysis commands are executed

No manual configuration is required beyond setting the `LogDBConnection` connection string in `appsettings.json`.

## Error Handling

- Logging failures do NOT interrupt main queue polling operations
- If database is unavailable, warnings are logged but queue processing continues
- Errors in SOAP envelope cleaning are caught and original XML is used

## Performance Considerations

- Logging is asynchronous and non-blocking
- SOAP envelope cleaning is optimized with simple string extraction
- Database writes use parameterized queries to prevent SQL injection
- Timestamps use database-level timezone conversion (UTC to IST)

## Configuration

### Required Configuration
Ensure `LogDBConnection` is set in `appsettings.json`:
```json
"ConnectionStrings": {
  "LogDBConnection": "server=YOUR_SERVER;port=PORT;database=YOUR_LOG_DB;user=USER;password=PASSWORD;"
}
```

### Optional Configuration
- If `LogDBConnection` is empty or not configured, logging is skipped with a warning
- UPL ID can be passed through queue processing pipeline for better tracking

## Testing

To verify logging is working:

1. **Check Database**:
   ```sql
   SELECT * FROM wp_xmllog 
   WHERE Log_ModuleName_VC = 'SabreQueueMCP' 
   ORDER BY Log_LogDate_DT DESC 
   LIMIT 10;
   ```

2. **Verify Log Entries**:
   - Log_LogXML_VC should contain both Request and Response XML
   - Log_Remarks_VC should show PCC, command, HTTP status, and status
   - Log_LogDate_DT should be recent

3. **Check Logs**:
   - Application logs should show "Sabre request logged" messages
   - Any database connection errors will be logged as warnings

## Troubleshooting

### Logs Not Appearing
1. **Verify LogDBConnection**: Check `appsettings.json` for correct database connection
2. **Check Database Permissions**: Ensure user has INSERT rights on wp_xmllog table
3. **Verify Database Availability**: Test connection to wcprdb2bukxmllogdb
4. **Check Application Logs**: Look for "API log skipped because ConnectionStrings:LogDBConnection is empty" warnings

### Performance Issues
1. Check if database is responsive
2. Monitor wp_xmllog table size (may need archiving strategy)
3. Ensure database indexes are created on Log_ModuleName_VC, Log_LogDate_DT

## Future Enhancements

Potential improvements:
- Add log retention/archival strategy
- Create views for easier analysis of queue polling logs
- Add monitoring/alerting on failed requests
- Add compression for large SOAP messages
- Add filtering to skip logging for certain command types
