# Sabre Queue Analysis MCP — All User Prompts

A consolidated reference of every user prompt that shaped this project, in chronological order.

---

## Prompt 1 — Initial MCP Server (Section 1–12)

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

Step 1: Receive Queue 7 text dataset.
Step 2: Parse all PNRs.
Step 3: Analyze all flight segments.
Step 4: Identify actionable segments.

Action Mapping:

TK → Review Schedule Change (calculate Delay Minutes / Delay Hours when OLD and NEW times exist)
HX → Remove Cancelled Segment (Recommendation Only)
UN → Rebook Required
UC → Confirmation Required
US → Resell Required
WL → Monitor Waitlist
HK → No Action Required

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

What this triggered:
- Full MCP server scaffold (.NET 9)
- `Models/Queue7Models.cs`, `Services/Queue7Parser.cs`, `Services/Queue7Processor.cs`
- `Tools/Queue7AnalysisTools.cs` exposing `parse_queue_text`, `parse_segments`, `queue_processor`
- `Program.cs` hosting MCP at `http://localhost:5007/mcp`
- `README.md`, `mcp.json`

---

## Prompt 2 — Data Folder (Section 9)

```text
i have the data in data folderd
```

What this triggered:
- Parser updated to read from `data/QUEUE 7.txt`
- Support for status-count values (`TK1`, `HK1`, `HX1`, `UN1`)
- Support for repeated `PNR:` blocks with `OLD` / `NEW` schedule-change lines

---

## Prompt 3 — Change Documentation (Section 1–12)

```text
create a document change including all prompts
```

What this triggered:
- `CHANGE_DOCUMENTATION.md` created with all prompts and change details

---

## Prompt 4 — MySQL Persistence + 15-Min Polling (Section 13)

```text
i want to update in the my sql database if any chanes in shedule and automatilcy ervey 15 mins and action to take
```

What this triggered:
- `MySqlConnector` package added
- `appsettings.json` with `ConnectionStrings:DefaultConnection` and `Queue7Polling` settings
- `Repositories/QueueActionRepository.cs` — upserts rows into `Queue7RecommendedActions`
- `BackgroundJobs/Queue7PollingService.cs` — background worker, 15-minute interval
- `Services/Queue7PollingOptions.cs` — polling configuration model
- `Program.cs` updated to register repository and hosted service
- `Tools/Queue7AnalysisTools.cs` updated with `store_recommended_actions` tool

---

## Prompt 5 — Manual Store Trigger (Section 14)

```text
store recommended actions
```

What this triggered:
- `POST /store-recommended-actions` endpoint added to `Program.cs`
- Immediately analyzes data file and stores recommendations in MySQL without waiting for the polling cycle

---

## Prompt 6 — Automatic Polling Logs + Change Detection (Section 15)

```text
create log and if any change time / new segment added in data it automaticly update in the database every 15 min i dont want to call tool to exicute the process
```

What this triggered:
- `Queue7Polling:LogFilePath` setting added
- File logging to `logs/queue7-polling.log`
- MySQL `Queue7ProcessingLogs` table created automatically
- Processing log rows written for every scan (updated, no-change, missing-file)
- `Queue7PollingService` uses `IHostEnvironment.ContentRootPath` for stable paths
- Exception handling added so one failed scan does not stop the worker permanently

---

## Prompt 7 — Sabre SOAP API Source (Section 16)

```text
i want to change getting api
https://webservices.platform.sabre.com
[SabreCommandLLSRQ SOAP envelope using HostCommand Q/7]
dont pnr remove from the queue
```

What this triggered:
- `Services/Queue7TextSource.cs` added — sends `SabreCommandLLSRQ` SOAP envelope
- `Queue7Polling:Source` setting added (`SabreApi` or `File`)
- `Queue7Polling:SabreApi` settings for endpoint, `BinarySecurityToken`, party IDs, conversation ID, host command
- `Queue7PollingService` reads text through `Queue7TextSource`
- `HttpClient` registered in `Program.cs`
- Only `Q/7` is allowed as the automatic host command; any other command is rejected

---

## Prompt 8 — Multi-Queue Support + Allowed Commands (Section 16 continued)

Inline change description applied after the SOAP source was added.

What this triggered:

`Queue7PollingOptions.cs`:
- `AllowedHostCommands` static `HashSet` containing `Q/7`, `Q/379`, `Q/62`, `I`, `QXI`
- `QueuePollingEntry` record and `Queues` list for independent per-queue configuration

`Queue7TextSource.cs`:
- `GetQueueTextForCommandAsync(string hostCommand, ...)` — validates command, resolves data file by convention, uses dynamic host command in SOAP envelope
- Hardcoded `Q/7` removed from SOAP envelope

`Queue7Parser.cs`:
- `ParseQueueText` accepts `int queueNumber = 7`, passes it to `ParsedQueueResult`

`Queue7Processor.cs`:
- `ProcessPnr` and `ProcessQueueText` accept `int queueNumber = 7`, stamp it on `QueueAnalysisResult`
- `BuildScheduleChangeAction` adds `QueueRecommendation: "Review Schedule Change"`
- HX adds `QueueRecommendation: "Remove Segment Later"`
- UN adds `QueueRecommendation: "Rebook Later"`

`Models/Queue7Models.cs`:
- `ActionFinding` gains `QueueRecommendation` and `RequiresAction` fields

`Tools/Queue7AnalysisTools.cs`:
- All four MCP tools accept `int queueNumber = 7`

`BackgroundJobs/Queue7PollingService.cs`:
- Iterates `GetQueueEntries()` list (from `Queues` config or legacy `SabreApi.HostCommand`)
- Per-queue hash tracking via `Dictionary<string, string>` keyed by host command
- All command validation goes through `AllowedHostCommands`

`Program.cs`:
- `/store-recommended-actions` accepts optional `?queue=` param (7, 379, or 62; defaults to 7)
- Rejects any queue not in `AllowedHostCommands`

`appsettings.json`:
- `Queues` array added with entries for `Q/7`, `Q/379`, and `Q/62`

---

## Safety Contract (all prompts)

The following Sabre commands are never executed by this application:

- `QN`
- `QR/7`
- `QXI`
- `ER`
- `IG`
- Segment removal
- Queue advancement
- PNR updates

The application stores recommendation rows only. All analysis is read-only.

---

## Prompt 9 — Log Master DB Usage to Flight Log DB (log_flightqueue)

```text
i want log master db usage in logxml db
log_flightqueue add in this
```

What this triggered:

`Services/PccCredentialStore.cs`:
- Reads `LogDBConnection` connection string from `appsettings.json`
- After each `LoadAsync` call (success or failure), inserts a log row into `log_flightqueue` table in the `wcprdb2bukxmllogdb` database
- `LogMasterDbUsageAsync` private method added:
  - Auto-creates `log_flightqueue` table if it does not exist (columns: `Id`, `Operation`, `Status`, `Details`, `CreatedAt`)
  - Inserts operation name, status (`Success`/`Failed`), and details (row count or error message)
  - Failures in log writing emit a warning but do not interrupt credential loading

`appsettings.json`:
- `ConnectionStrings:LogDBConnection` used (`server=192.168.10.113;port=26033;database=wcprdb2bukxmllogdb;user=wcusr;password=wcusr123;`)

Table created automatically:

```sql
CREATE TABLE IF NOT EXISTS log_flightqueue (
    Id BIGINT AUTO_INCREMENT PRIMARY KEY,
    Operation VARCHAR(100),
    Status VARCHAR(50),
    Details TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

---

## Safety Contract (all prompts)

The following Sabre commands are never executed by this application:

- `QN`
- `QR/7`
- `QXI`
- `ER`
- `IG`
- Segment removal
- Queue advancement
- PNR updates

The application stores recommendation rows only. All analysis is read-only.
