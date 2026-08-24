# Prompt: Build an ADM Analysis 

---

# Objective

Build a background service/API that:

1. Reads today's ticketed PNRs from Sabre using DQB*
2. Saves all ticketed PNRs into a database
3. Opens every PNR
4. Runs multiple ADM validation rules
5. Stores all findings
6. Displays the results in a dashboard

The solution must be scalable, asynchronous, production-ready, and follow Clean Architecture.



# Sabre Commands

## Read today's ticketed PNRs

DQB*

Continue to the next page until the report ends.

DQB*MD

Stop when:

END OF REPORT

Example:

SALES AUDIT REPORT

PNR-QDTIGU

0983929957505

Extract:

* PNR
* Ticket Number
* Ticket Amount
* Ticket Date
* Agency PCC
* Agent
* Time

Save into SalesAudit table.

---

# Processing Flow

Start Session

↓

Execute DQB*

↓

Read report

↓iF( END OF REPORT)--------------|
                                 |
Execute DQB*MD                   |    
                                 |
↓                                |
                                 |
Repeat until END OF REPORT       |
                                 |
↓                                |
            |--------------------
Extract all PNRs

↓

Save SalesAudit

↓

Loop through every PNR

↓

Open PNR

*{PNR}

Cross Border Ticket Detection Run ADM Rules
*HI
Married Segment Detection Run ADM Rules
Changed Segment Detection Run ADM Rules
↓

↓

Save Results

↓

Generate Dashboard

---

# Rule 1

## Cross Border Ticket Detection

Open

*{PNR}

Extract Ticketed PCC

Example

T-06AUG-3A78*AWS

or

3A78.3A78*AWS

Determine

Ticketed PCC

Determine

Original Booking PCC

using:

* PNR History
* Received From
* Original Booking History
* AAA History
* Creation History

Maintain a PCC Market Master

Example

3A78 = India

8FR2 = UAE

4ABC = USA

Logic

If

Ticketed Market != Booking Market

then

IsCrossBorder = True

Store

TicketPCC

BookingPCC

TicketMarket

BookingMarket

Risk Score

Remarks

---

# Rule 2

Changed Segment Detection

Execute

*HI(command)

Example

AS AI1818 SXRDEL

AS AI2727 DELCCU

AS AI1818 SXRDEL

AS AI2727 DELCCU

AS AI1818 SXRDEL

AS AI2727 DELCCU

Create a unique segment key

FlightNo

Date

Origin

Destination

Count how many times every segment appears.

If

Same segment appears two or more times

Mark

Changed Segment

Store

ChangedSegmentCount

Segment Details

Remarks

---

# Rule 3

Married Segment Detection

Read all AS history (*HI). 

Group every itinerary modification.

Example

Set 1

AI1818 SXR DEL

AI2727 DEL CCU

Set 2

AI1818 SXR MYR

AI2727 DEL MAA

Set 3

AI1818 MAA DEL

AI2727 COK CCU

Create itinerary signatures.

Count unique itinerary groups.

If

Different itinerary groups > 2

Mark

Married Segment

Store

MarriedSegmentCount

Remarks

---

# Rule 4

History Parser

Create a parser capable of reading

*HI

and extracting

Flight Number

Date

Origin

Destination

Status

Booking Class

Segment Number

Action

Timestamp

Agent

PCC

Create reusable objects.

---

# Rule 5

Risk Score

Every rule contributes to ADM Risk.

Example

Cross Border

40 Points

Changed Segment

30 Points

Married Segment

30 Points

Final Score

0-100

Risk Levels

0-20

Low

21-50

Medium

51-80

High

81-100

Critical

---

# Database

Create tables

SalesAudit

AdmAnalysis

AdmAnalysisDetails


HistorySegments

ApplicationLogs

Use proper indexes.

# API Endpoints

POST

/api/adm-analysis/run

Runs complete analysis.

GET

/api/adm-analysis

Returns all analyzed PNRs.

GET

/api/adm-analysis/{pnr}

Returns complete analysis.

GET

/api/adm-analysis/dashboard

Returns summary metrics.

---
IN BACKGOUND POLLING EVERY 4 HRS
