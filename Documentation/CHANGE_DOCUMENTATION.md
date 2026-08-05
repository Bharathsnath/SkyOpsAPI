# Sabre Queue 7 Analysis MCP - Change Documentation

Date: 2026-06-01

## 1. Title

Created an analysis-only Sabre Queue 7 MCP server and documented all prompts, parser changes, and verification steps.

## 2. User Prompts Included

### Prompt 1

```text
You are a Sabre Queue 7 Analysis MCP Assistant.

Your responsibility is to analyze Queue 7 PNR data provided as text and identify reservation issues requiring airline servicing action.

Current Mode:

* ANALYSIS ONLY
* DO NOT remove PNRs from queue
* DO NOT execute QN automatically
* DO NOT execute QR/7
* DO NOT execute QXI
* DO NOT execute ER or IG automatically
* DO NOT perform any Sabre updates
* Only analyze and recommend actions

Available MCP Tools:

1. parse_queue_text

   * Parses Queue 7 mock dataset text.
   * Extracts PNR information.

2. parse_segments

   * Extracts:

     * Flight Number
     * Carrier
     * Date
     * Origin
     * Destination
     * Status Code
     * Departure Time
     * Arrival Time

3. queue_processor

   * Determines required servicing action.

Queue Status Rules:

HK = Confirmed
TK = Schedule Change
HX = Airline Cancelled
UN = Flight Cancelled / Unavailable
UC = Unable Confirm
US = Unable Sell
WL = Waitlisted
NO = No Action Required

Processing Workflow:

Step 1:
Receive Queue 7 text dataset.

Step 2:
Parse all PNRs.

Step 3:
Analyze all flight segments.

Step 4:
Identify actionable segments.

Action Mapping:

TK
Action:
Review Schedule Change

If old and new times exist:
Calculate:
Delay Minutes
Delay Hours

HX
Action:
Remove Cancelled Segment (Recommendation Only)

UN
Action:
Rebook Required

UC
Action:
Confirmation Required

US
Action:
Resell Required

WL
Action:
Monitor Waitlist

HK
Action:
No Action Required

Important:

* Never modify Sabre records.
* Never remove items from queue.
* Never advance queue automatically.
* Never invent PNR information.
* Only use information found in the supplied dataset.
* Report all actionable segments.
* Separate informational findings from actionable findings.

Required JSON Output:

{
"queue": 7,
"pnr": "ABC123",
"requiresAction": true,
"actions": [
{
"segment": 1,
"flight": "AI379",
"status": "TK",
"action": "Review Schedule Change",
"delayMinutes": 120
}
],
"informational": [],
"summary": "1 schedule change detected."
}

If no action is required:

{
"queue": 7,
"pnr": "ABC123",
"requiresAction": false,
"actions": [],
"informational": [
{
"segment": 1,
"flight": "AI379",
"status": "HK"
}
],
"summary": "No queue action required."
}

Future Processing Mode:

Store recommended actions only.

Example:

{
"segment": 2,
"flight": "AI379",
"status": "HX",
"recommendedFutureCommand": "Remove segment after agent review"
}

The assistant must act as a Queue 7 analyst and recommendation engine only.
D:\Seshadrinath-Workbench\Saber QtestMCP create in this folder
```

### Prompt 2

```text
i have the data in data folderd
```

### Prompt 3

```text
create a document change including all  prompts
```

## 3. Why

The project folder was initially empty and needed a working MCP server for Queue 7 analysis. The user also supplied Queue 7 data in the `data` folder, so the parser needed to support the exact dataset format rather than only a generic mock format.

The main safety requirement is strict analysis-only behavior. The server recommends servicing actions but does not execute Sabre commands, advance queues, remove segments, end records, ignore records, or update PNRs.

## 4. What Changed

- added: `SaberQtestMCP.csproj` as a .NET 9 MCP web project.
- added: `Program.cs` to host the MCP server at `http://localhost:5007/mcp`.
- added: `mcp.json` with the local MCP endpoint configuration.
- added: `README.md` describing safety mode, tools, and run instructions.
- added: `Models/Queue7Models.cs` for parsed queue, PNR, segment, action, informational, and analysis result models.
- added: `Tools/Queue7AnalysisTools.cs` exposing `parse_queue_text`, `parse_segments`, and `queue_processor`.
- added: `Services/Queue7Parser.cs` to parse Queue 7 text and flight segments.
- added: `Services/Queue7Processor.cs` to map statuses to recommendation-only actions.
- added: parser support for status-count values such as `TK1`, `HK1`, `HX1`, and `UN1`.
- added: parser support for repeated `PNR:` blocks where itinerary lines and schedule-change `OLD` / `NEW` lines are separated.
- added: delay minute and delay hour calculation for `TK` schedule changes when old and new times are available.
- added: `recommendedFutureCommand` text values as recommendations only.
- added: `CHANGE_DOCUMENTATION.md` containing all prompts and change details.

## 5. Expected Outcome

The MCP server should accept Queue 7 text, parse all PNRs, identify all actionable flight segments, and return JSON results with separate `actions` and `informational` arrays.

Expected status behavior:

- `TK`: `Review Schedule Change`; calculate delay when `OLD` and `NEW` times exist.
- `HX`: `Remove Cancelled Segment (Recommendation Only)`.
- `UN`: `Rebook Required`.
- `UC`: `Confirmation Required`.
- `US`: `Resell Required`.
- `WL`: `Monitor Waitlist`.
- `HK`: informational only, no action required.
- `NO`: informational only, no action required.

## 6. Architecture Flow

1. A client sends Queue 7 text to one of the MCP tools.
2. `Queue7AnalysisTools` receives the request.
3. `Queue7Parser` extracts PNR blocks and flight segment fields.
4. `Queue7Parser` merges repeated PNR blocks and attaches schedule-change `OLD` / `NEW` times to matching flights.
5. `Queue7Processor` maps segment statuses to analysis-only recommendations.
6. The server returns JSON with queue number, PNR, action flag, action findings, informational findings, and summary.

## 7. Files Affected

- `SaberQtestMCP.csproj`
- `Program.cs`
- `mcp.json`
- `README.md`
- `Models/Queue7Models.cs`
- `Services/Queue7Parser.cs`
- `Services/Queue7Processor.cs`
- `Tools/Queue7AnalysisTools.cs`
- `CHANGE_DOCUMENTATION.md`

## 8. Build Verification

Command:

```powershell
dotnet build
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Note: one rebuild initially failed because a previously running server process was locking `bin\Debug\net9.0\SaberQtestMCP.exe`. After stopping the process, the build succeeded.

Root endpoint check:

```powershell
Invoke-WebRequest -Uri http://localhost:5007/ -UseBasicParsing
```

Expected response:

```text
Sabre Queue 7 Analysis MCP Running - analysis only
```

## 9. Data Folder

Detected dataset:

```text
data\QUEUE 7.txt
```

Observed format examples:

```text
PNR: PNR001
1 AI379 Y 15DEC MAA DEL TK1
2 AI062 Y 15DEC DEL BOM HK1
PNR: PNR001
AI379 MAA DEL
OLD: 0800 1050
NEW: 0930 1220
```

Parser changes were made to support this exact structure.

## 10. Risks And Watch Outs

- The parser is tuned for the supplied Queue 7 text format and common Sabre-like segment lines.
- Very different Queue 7 layouts may require additional parsing patterns.
- No live airline inventory check is performed.
- No live Sabre PNR state is validated.
- No Sabre commands are executed by design.

## 11. How To Verify

1. Run `dotnet build`.
2. Run `dotnet run`.
3. Connect an MCP client to `http://localhost:5007/mcp`.
4. Call `queue_processor` using the contents of `data\QUEUE 7.txt`.
5. Confirm `TK` records include `Review Schedule Change` and delay values where `OLD` / `NEW` times exist.
6. Confirm `HX` and `UN` records return recommendation-only actions.
7. Confirm `HK` records appear in `informational` with no action required.

## 12. Rollback

To roll back this work, remove the created MCP project files from `D:\Seshadrinath-Workbench\Saber QtestMCP`.

Because the target folder was empty before the project was created, rollback does not need to preserve older application code.

## 13. MySQL Persistence Update

### User Prompt

```text
i want to update in the my sql database if any chanes in shedule and automatilcy ervey 15 mins and action to take
```

### What Changed

- added: `MySqlConnector` package reference.
- added: `appsettings.json` with `ConnectionStrings:DefaultConnection` and `Queue7Polling` settings.
- added: `Repositories/QueueActionRepository.cs` to create and upsert rows into MySQL.
- added: `BackgroundJobs/Queue7PollingService.cs` to analyze `data/QUEUE 7.txt` every 15 minutes.
- added: `Services/Queue7PollingOptions.cs` for polling configuration.
- updated: `Program.cs` to register the repository and hosted polling service.
- updated: `Tools/Queue7AnalysisTools.cs` with `store_recommended_actions` for manual MySQL persistence.
- updated: `README.md` with MySQL setup and polling configuration.

### Database Table

The app creates this table automatically when a valid MySQL connection string is configured:

```text
Queue7RecommendedActions
```

Stored fields include:

- Queue number
- PNR
- Segment number
- Flight
- Status code
- Recommended action
- Delay minutes
- Delay hours
- Recommended future command
- Summary
- Source file
- First seen time
- Last seen time
- Occurrence count
- Active flag

### Polling Behavior

The background service checks the configured data file every 15 minutes:

```json
{
  "Queue7Polling": {
    "Enabled": true,
    "DataFilePath": "data/QUEUE 7.txt",
    "IntervalMinutes": 15
  }
}
```

If the file content has not changed since the last scan, MySQL update is skipped.

If the file content has changed, the service:

1. Reads the Queue 7 text file.
2. Runs the existing analysis-only queue processor.
3. Stores action findings in MySQL.
4. Updates existing matching rows instead of creating duplicates.

### Safety Boundary

This update still does not execute Sabre commands. The database stores recommended actions only.

No implementation was added for:

- `QN`
- `QR/7`
- `QXI`
- `ER`
- `IG`
- Segment removal
- Sabre host updates

### Verification

Command:

```powershell
dotnet build
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Required Setup

Update `appsettings.json` with a real MySQL connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=sabre_queue;user=root;password=your_password;"
  }
}
```

If `DefaultConnection` is empty, the app still runs but skips MySQL persistence and logs a warning.

## 14. Manual Store Trigger

### User Prompt

```text
store recommended actions
```

### What Changed

- added: `POST /store-recommended-actions` in `Program.cs`.
- purpose: immediately analyze the configured Queue 7 data file and store recommendation rows in MySQL without waiting for the 15-minute polling cycle.

### Execution Result

Command:

```powershell
Invoke-RestMethod -Uri http://localhost:5007/store-recommended-actions -Method Post
```

Result:

```json
{
  "queue": 7,
  "pnrCount": 50,
  "savedActionCount": 41,
  "databaseConfigured": true,
  "message": "Recommended actions stored in MySQL."
}
```

### Safety Boundary

This endpoint stores recommendation rows only. It does not execute Sabre commands or modify PNRs.

## 15. Automatic Polling Logs And Change Updates

### User Prompt

```text
create log and if any change time  / new segment  added   in data it automaticly update in the database every 15 min i dont want to call tool to exicute the process
```

### What Changed

- added: `Queue7Polling:LogFilePath` setting in `appsettings.json`.
- added: file logging to `logs/queue7-polling.log`.
- added: MySQL processing log table creation for `Queue7ProcessingLogs`.
- added: processing log inserts for updated, no-change, and missing-file scan outcomes.
- updated: `Queue7PollingService` to use `IHostEnvironment.ContentRootPath` for stable data and log paths.
- updated: `Queue7PollingService` with exception handling so one failed scan does not permanently stop the worker.

### Automatic Behavior

When the application is running, no tool call is required.

Every 15 minutes the background worker:

1. Reads `data/QUEUE 7.txt`.
2. Calculates a file hash.
3. If the file changed, parses all Queue 7 PNRs.
4. Updates existing MySQL rows when schedule times or delay values change.
5. Inserts new MySQL rows when new actionable segments are added.
6. Writes a file log entry.
7. Writes a MySQL processing log entry.

If the file did not change, the worker skips action upserts and still writes a processing log.

### Log Locations

File log:

```text
logs/queue7-polling.log
```

MySQL log table:

```text
Queue7ProcessingLogs
```

Recommendation table:

```text
Queue7RecommendedActions
```

### Safety Boundary

The automatic worker still stores recommendation rows only. It does not call any MCP tool, does not execute Sabre commands, and does not modify Sabre PNRs.

## 16. Sabre SOAP Queue Source

### User Prompt

```text
i want to change getting api 
https://webservices.platform.sabre.com
[SabreCommandLLSRQ SOAP envelope using HostCommand Q/7]
dont pnr remove from the queue
```

### What Changed

- added: `Services/Queue7TextSource.cs`.
- added: Sabre SOAP source support for `https://webservices.platform.sabre.com`.
- added: `Queue7Polling:Source` setting.
- added: `Queue7Polling:SabreApi` settings for endpoint, token, party IDs, conversation ID, and host command.
- updated: `Queue7PollingService` now reads Queue 7 text through `Queue7TextSource`.
- updated: automatic polling can use Sabre API source or local file source.
- updated: `Program.cs` registers `HttpClient` for the Sabre Queue 7 text source.

### Safety Boundary

The automatic Sabre request only allows this host command:

```text
Q/7
```

The source rejects any other automatic command. The application still does not execute:

- `QN`
- `QR/7`
- `QXI`
- `ER`
- `IG`
- Segment removal
- Queue advancement
- PNR updates

### Required Configuration

Set the token in one of these places:

```json
{
  "Queue7Polling": {
    "SabreApi": {
      "BinarySecurityToken": "your-current-sabre-session-token"
    }
  }
}
```

Or set the environment variable:

```powershell
$env:SABRE_SECURITY_TOKEN = "your-current-sabre-session-token"
```

Sabre session tokens expire, so the token must be refreshed when Sabre invalidates the session.

What changed and why
Queue7PollingOptions.cs
Added AllowedHostCommands — a static HashSet containing Q/7, Q/379, Q/62, I, QXI. This is the single source of truth for command validation across the entire app.

Added QueuePollingEntry record and a Queues list on Queue7PollingOptions so all three queues can be configured independently in appsettings.json.

Queue7TextSource.cs
Added GetQueueTextForCommandAsync(string hostCommand, ...) — validates the command against AllowedHostCommands, resolves the correct file (QUEUE 7.txt, QUEUE 379.txt, QUEUE 62.txt) by convention for file-source mode, and passes the dynamic command into the SOAP envelope.

Removed the hardcoded Q/7 from the SOAP <HostCommand> element — it now uses the caller-supplied hostCommand.

GetQueueTextAsync now delegates to the new method using SabreApi.HostCommand.

Queue7Parser.cs
ParseQueueText now accepts int queueNumber = 7 and passes it to ParsedQueueResult instead of hardcoding 7.

Queue7Processor.cs
ProcessPnr and ProcessQueueText both accept int queueNumber = 7 and stamp it on QueueAnalysisResult.

BuildScheduleChangeAction — added QueueRecommendation: "Review Schedule Change".

HX action — added QueueRecommendation: "Remove Segment Later".

UN action — added QueueRecommendation: "Rebook Later".

Models/Queue7Models.cs
ActionFinding — added QueueRecommendation and RequiresAction (defaults true) fields per your status analysis spec.

Tools/Queue7AnalysisTools.cs
All four MCP tools now accept int queueNumber = 7, passed through to the parser and processor.

BackgroundJobs/Queue7PollingService.cs
Rewired to iterate the GetQueueEntries() list (reads from Queues config or falls back to the legacy SabreApi.HostCommand).

Per-queue hash tracking via Dictionary<string, string> _lastHashes keyed by host command.

All command validation goes through AllowedHostCommands.

Program.cs
/store-recommended-actions now accepts an optional ?queue= query param (7, 379, or 62, defaults to 7) and rejects anything not in the allowed list.

Uses Queue7TextSource.GetQueueTextForCommandAsync directly so file resolution and validation are consistent.

appsettings.json
Added the Queues array with entries for Q/7, Q/379, and Q/62 with their respective data file paths.

Executive Dashboard

Queue Performance Dashboard
PCC Performance Dashboard
Flight Status Dashboard
Critical Queue Dashboard
Delay Analysis Dashboard
Flight Impact Dashboard
PNR Analysis Dashboard
Recommendation Dashboard

Operational Dashboard (Real-Time)
-Open Critical Cases
-TK/TK1/TK2 Queue Monitor
-HX/UN Queue Monitor
-Live Queue Status
Management Dashboard
-PCC Performance Ranking
-Queue Efficiency
-Critical Issue Percentage
-SLA Compliance

Dashboard API Endpoints
Endpoint	Description
GET /api/dashboard/executive	Total actions, critical count, unique PNRs/PCCs, queue breakdown
GET /api/dashboard/queue-performance?queue=7	Status breakdown per queue, hourly activity
GET /api/dashboard/pcc-performance?pcc=3A78	PCC ranking by actions, critical %, unique PNRs
GET /api/dashboard/flight-status?status=HX	Flights by status, latest 100 entries
GET /api/dashboard/critical	HX/UN/UC cases, total critical count
GET /api/dashboard/delay-analysis	Top delays, avg/max delay minutes
GET /api/dashboard/flight-impact	Flights with multiple actions across PNRs
GET /api/dashboard/pnr-analysis?pnr=USOIYE	PNR segment details or top PNRs by action count
GET /api/dashboard/recommendations	Breakdown by action type and recommended commands
GET /api/dashboard/operational	Real-time: open critical, TK monitor, HX/UN monitor, live queue status
GET /api/dashboard/management	PCC ranking, queue efficiency, critical %, SLA metrics

there no change or new update i need email when there is any update or change or new entry dont mail all


IQueueActionRepository.cs — Changed return type of SaveRecommendedActionsAsync from Task<int> to Task<(int Saved, IReadOnlyList<QueueAnalysisResult> ChangedResults)>

QueueActionRepository.cs — UpsertActionAsync now returns int (MySQL affected rows: 1=insert, 2=update, 0=no-change). SaveRecommendedActionsAsync tracks which PNRs had actual DB changes and returns them.

Queue7PollingService.cs — Email is now sent only when changedResults.Count > 0 (i.e., there was a new insert or an update to existing data). If the queue data is unchanged, no email is sent.

QueueAnalysisService.cs — Updated to match the new tuple return type.


