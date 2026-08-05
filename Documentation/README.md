# Sabre Queue 7 Analysis MCP

Analysis-only MCP server for reviewing Sabre Queue 7 PNR text and recommending servicing actions.

## Safety mode

This server does not execute Sabre commands and does not modify PNRs. It only parses supplied text and returns recommendations.

It intentionally does not implement:

- `QN`
- `QR/7`
- `QXI`
- `ER`
- `IG`
- Segment removal
- Any Sabre update command

## Tools

- `parse_queue_text`: Parses Queue 7 text and extracts PNR blocks.
- `parse_segments`: Extracts flight segment data from PNR text.
- `queue_processor`: Analyzes segments and returns required recommendations.
- `store_recommended_actions`: Analyzes supplied Queue 7 text and stores recommendation-only action findings in MySQL.

## MySQL persistence

Recommended actions can be saved to MySQL every 15 minutes from:

```text
Sabre SOAP API using Q/7
```

Set your database connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=sabre_queue;user=root;password=your_password;"
  }
}
```

The application creates this table automatically if it does not exist:

```text
Queue7RecommendedActions
```

Only recommendation rows are stored. No Sabre commands are executed.

Polling settings:

```json
{
  "Queue7Polling": {
    "Enabled": true,
    "Source": "SabreApi",
    "DataFilePath": "data/QUEUE 7.txt",
    "IntervalMinutes": 15,
    "LogFilePath": "logs/queue7-polling.log",
    "SabreApi": {
      "Endpoint": "https://webservices.platform.sabre.com",
      "BinarySecurityToken": "",
      "ConversationId": "",
      "FromPartyId": "com.abacus.SWSSession",
      "ToPartyId": "webservices.sabre.com",
      "HostCommand": "Q/7"
    }
  }
}
```

Set `Queue7Polling:SabreApi:BinarySecurityToken` or the `SABRE_SECURITY_TOKEN` environment variable before running. Keep `HostCommand` as `Q/7`; the app rejects other automatic host commands.

Automatic behavior:

- The app starts the Queue 7 polling service when `dotnet run` starts.
- It calls SabreCommandLLSRQ with `Q/7` every 15 minutes.
- If schedule times change, the matching MySQL recommendation row is updated.
- If a new actionable segment is added, a new MySQL recommendation row is inserted.
- If the source has not changed, action updates are skipped and a processing log is written.
- It does not execute `QN`, `QR/7`, `QXI`, `ER`, `IG`, segment removal, queue advancement, or PNR updates.

Processing logs are written to:

```text
logs/queue7-polling.log
```

The app also creates this MySQL log table automatically:

```text
Queue7ProcessingLogs
```

Manual trigger, optional for testing only:

```powershell
Invoke-RestMethod -Uri http://localhost:5007/store-recommended-actions -Method Post
```

## Run

```powershell
dotnet run
```

MCP endpoint:

```text
http://localhost:5007/mcp
```

# Dashboard API

A comprehensive collection of dashboard API endpoints for monitoring and analyzing flight operations, queue performance, PCC performance, and more.

## Base URL

```
http://localhost:5007

 ```

## Overview

The Dashboard API provides a suite of endpoints designed to support operational visibility and decision-making across multiple levels of an organization — from executive leadership to frontline operations staff. It covers:

- **Executive & Management Views** — High-level KPIs and strategic metrics
    
- **Operational Monitoring** — Real-time operational status and agent activity
    
- **Flight Operations** — Flight status tracking, delay analysis, and impact assessment
    
- **Queue & PCC Performance** — Granular performance metrics by queue and Pseudo City Code
    
- **PNR Analysis** — Passenger Name Record lookup and analysis
    
- **Critical Alerts** — Urgent issues and anomalies requiring immediate attention
    
- **Recommendations** — Actionable suggestions based on operational analytics
    

## Endpoints

| Name | Method | Path | Query Params |
| --- | --- | --- | --- |
| Executive Dashboard | GET | `/api/dashboard/executive` | — |
| Queue Performance | GET | `/api/dashboard/queue-performance` | `queue` |
| PCC Performance | GET | `/api/dashboard/pcc-performance` | `pcc` |
| Flight Status | GET | `/api/dashboard/flight-status` | `status` |
| Critical Dashboard | GET | `/api/dashboard/critical` | — |
| Delay Analysis | GET | `/api/dashboard/delay-analysis` | — |
| Flight Impact | GET | `/api/dashboard/flight-impact` | — |
| PNR Analysis | GET | `/api/dashboard/pnr-analysis` | `pnr` |
| Recommendations | GET | `/api/dashboard/recommendations` | — |
| Operational Dashboard | GET | `/api/dashboard/operational` | — |
| Management Dashboard | GET | `/api/dashboard/management` | — |

## Getting Started

1. Ensure the Dashboard API server is running at `http://localhost:5007`
    
2. Select the desired endpoint from the collection
    
3. For parameterized endpoints (Queue Performance, PCC Performance, Flight Status, PNR Analysis), update the query parameter values as needed
    
4. Send the request to retrieve the dashboard data