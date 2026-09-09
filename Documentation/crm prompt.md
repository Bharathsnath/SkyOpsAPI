You are a senior Angular + ASP.NET Core enterprise application developer.

I am building a Travel Operations CRM called "SkyOps Control Center".

I need you to build a production-quality Gmail-like Email + CRM interface inside my existing SkyOps application.

TECH STACK
- Frontend: Angular
- Backend: ASP.NET Core Web API
- Database: SQL Server
- Real-time updates: SignalR
- Email: SMTP using MailKit
- UI should be responsive and enterprise-grade
- Do not use Gmail APIs. Use SMTP for sending emails.
- Keep SMTP credentials strictly on the backend.

==================================================
1. GMAIL-LIKE EMAIL UI
==================================================

Create an Email/CRM screen similar to Gmail.

Layout:

LEFT SIDEBAR
- SkyOps logo
- Compose
- Inbox
- Starred
- Snoozed
- Sent
- Drafts
- All Mail
- Pending Action
- Follow Up
- Operations
- Customer (CTEM)
- Agent (Remark)
- Trash
- Labels

Labels:
- Schedule Change
- Cancellation
- Refund
- Rebooking
- Name Correction
- Ticketing
- Baggage
- Seat
- Customer Complaint

TOP BAR
- Global email search
- Search by:
  - PNR
  - Customer
  - Email
  - Subject
  - Case ID
  - Agent
- Refresh
- Settings
- User profile

CENTER EMAIL LIST

Each email row should display:

- Checkbox
- Star
- Sender
- Subject
- Preview
- Email type
- PNR
- CRM Case ID
- Priority
- Action status
- Time

Email types:

AGENT
Source = Remark email

CUSTOMER
Source = CTEM email

OPERATIONS
Internal operations email

SYSTEM
SkyOps generated notification

Tabs:

- All
- Agent (Remark)
- Customer (CTEM)
- Operations
- Unassigned

==================================================
2. EMAIL READING SCREEN
==================================================

When an email is selected, display:

Subject
Sender
Sender email
To
CC
Date/time
Attachments
PNR
CRM Case ID

Example:

Action Required: Flight Change – PNR UGHTIJ

Customer:
Vikas Kapoor

PNR:
UGHTIJ

Issue:
Schedule Change

CRM:
CRM-00125

The email body should be rendered as HTML.

==================================================
3. CRM ACTION BUTTONS
==================================================

The email should NOT be a simple email viewer.

The email can contain actions.

For Agent/Remark emails:

- Accept
- Reschedule
- Cancel
- Request More Details
- Send to Operations
- Contact Customer
- Add Follow-up
- Close Case

For Customer/CTEM emails:

- Accept New Flight
- Request Alternate Flight
- Request Cancellation
- Contact Agent
- Confirm
- Reject

When a user clicks an action:

Frontend calls:

POST /api/crm/cases/{caseId}/actions

Request:

{
    "actionCode": "ALTERNATE_FLIGHT",
    "source": "CUSTOMER_EMAIL"
}

Backend stores the action.

==================================================
4. VERY IMPORTANT — ACTION MUST APPEAR IN CRM CHAT
==================================================

Every action taken from an email must automatically appear in the CRM conversation/timeline.

Example:

SYSTEM
CRM case created automatically.

12:30 PM

AGENT - Seshadri
Customer requested an alternate flight.

12:35 PM

OPERATIONS - Arun
Checking available flights.

12:40 PM

CUSTOMER - Vikas Kapoor
Requested earliest available flight.

12:45 PM

SYSTEM
Customer action received from CTEM email.

12:45 PM

CUSTOMER ACTION
Request Alternate Flight

The chat should update in real time using SignalR.

No page refresh should be required.

==================================================
5. CRM CHAT
==================================================

Inside the email reading screen create:

CRM Conversation

Message types:

- AGENT
- CUSTOMER
- OPERATIONS
- SYSTEM

Each message should show:

- Avatar
- Name
- Role
- Message
- Timestamp
- Action badge if applicable

Example:

[VK] Vikas Kapoor
Customer

Requested alternate flight in the morning.

[Action: ALTERNATE_FLIGHT]

[AR] Arun
Operations

Checking availability.

[SYSTEM]

Customer action received from CTEM email.

Chat composer:

[ Type a message... ]

Buttons:

- Attachment
- Emoji
- Send

API:

POST /api/crm/cases/{caseId}/messages

==================================================
6. RELATED PNR PANEL
==================================================

On the right side show:

RELATED PNR

PNR:
UGHTIJ

Customer:
Vikas Kapoor

Mobile:
+91 XXXXX XXXXX

Email:
customer@email.com

Journey:
BOM → KUL

Issue:
Schedule Change

CRM Case:
CRM-00125

Status:
OPS PENDING

SLA:
25 minutes remaining

Button:

[ Open CRM Case ]

Also display:

- Emails in thread
- Actions taken
- Open follow-ups
- Attachments
- Tags

==================================================
7. CUSTOMER 360
==================================================

Clicking the customer should open Customer 360.

Display:

Customer name
Email
Mobile
Company
Total bookings
Open cases
Closed cases
Recent PNRs
Recent CRM cases
Email history
Communication history

==================================================
8. SMTP CONFIGURATION
==================================================

Create:

Settings → Email Configuration

UI:

Email Provider

- Gmail
- Microsoft 365
- Custom SMTP

Fields:

SMTP Host
SMTP Port
Username
Password
From Email
From Name
Enable SSL/TLS

Buttons:

[ Test Connection ]
[ Save Configuration ]

Example Gmail configuration:

Host:
smtp.gmail.com

Port:
587

Security:
STARTTLS

Username:
company@gmail.com

Password:
APP PASSWORD

IMPORTANT:

Never expose SMTP password to Angular.

Never store SMTP password in frontend code.

Angular should call:

POST /api/settings/smtp/test

POST /api/settings/smtp

Backend should securely store credentials.

Use encrypted storage or a production secret manager.

==================================================
9. ASP.NET CORE SMTP
==================================================

Use MailKit.

Create:

ISmtpService

SmtpService

SmtpOptions

EmailController

Endpoints:

POST /api/email/send

POST /api/settings/smtp/test

GET /api/settings/smtp

PUT /api/settings/smtp

SMTP service must support:

- Gmail
- Microsoft 365
- Custom SMTP
- SSL/TLS
- Authentication
- HTML email
- Attachments

==================================================
10. CRM EMAIL TABLES
==================================================

Create SQL Server tables:

CrmCases
CrmEmails
CrmMessages
CrmActionEvents
CrmCaseAssignments
CrmCaseComments
CrmTasks
CrmFollowUps
CrmAttachments
CrmCaseStatusHistory
CrmNotifications
SmtpConfigurations

CrmCases:

Id
CaseNumber
Pnr
CustomerId
CaseType
Priority
Status
AgentId
OperationUserId
CreatedAt
UpdatedAt
ClosedAt
SlaDueAt

CrmEmails:

Id
CaseId
MessageId
FromEmail
ToEmail
Cc
Subject
BodyHtml
EmailType
Pnr
IsRead
IsStarred
ReceivedAt

CrmMessages:

Id
CaseId
SenderType
SenderId
Message
CreatedAt

CrmActionEvents:

Id
CaseId
MessageId
ActorType
ActorId
ActionCode
ActionSource
ActionData
CreatedAt

==================================================
11. EMAIL TYPES
==================================================

Remark Email:

Recipient = Agent

CTEM Email:

Recipient = Customer

Operations Email:

Recipient = Operations team

The system must know who is allowed to perform each action.

Example:

Agent:
- Reschedule
- Cancel
- Send to Operations
- Contact Customer

Customer:
- Accept
- Alternate Flight
- Cancel Request
- Contact Agent

Operations:
- Rebook
- Ticket
- Cancel
- Queue action
- Complete operation

==================================================
12. SECURE EMAIL ACTION LINKS
==================================================

Do NOT expose:

/action/UGHTIJ

Instead generate secure one-time tokens.

Example:

/customer-action/{secureToken}

/agent-action/{secureToken}

Store:

Token
CaseId
Recipient
RecipientType
AllowedActions
ExpiresAt
UsedAt

Token must:

- Expire
- Be single-use where appropriate
- Be cryptographically secure
- Not expose PNR/customer information
- Validate recipient/action permissions

==================================================
13. EMAIL → ACTION → CHAT FLOW
==================================================

Implement this complete flow:

SkyOps detects PNR issue

↓

Create CRM Case

CRM-00125

↓

Send Remark email to Agent

↓

Send CTEM email to Customer

↓

Agent/Customer opens secure action page

↓

User clicks action

↓

POST /api/crm/cases/{caseId}/actions

↓

Save CrmActionEvent

↓

Create CrmMessage

↓

Publish SignalR event

↓

Agent/Operations CRM chat updates immediately

Example:

Customer clicks:

"Request Alternate Flight"

Chat automatically shows:

CUSTOMER
Requested Alternate Flight

SYSTEM
Action received from customer email.

==================================================
14. SIGNALR
==================================================

Create:

/hubs/crm

Events:

ReceiveMessage
ReceiveAction
CaseUpdated
EmailReceived
CaseAssigned
StatusChanged

Angular should subscribe to SignalR.

When customer performs an action externally, the open Agent CRM screen must update immediately.

==================================================
15. API STRUCTURE
==================================================

Create:

/api/crm/emails
/api/crm/emails/{id}
/api/crm/cases
/api/crm/cases/{caseId}
/api/crm/cases/{caseId}/messages
/api/crm/cases/{caseId}/actions
/api/crm/cases/{caseId}/timeline
/api/crm/customers/{customerId}
/api/crm/search
/api/email/send
/api/settings/smtp
/api/settings/smtp/test

==================================================
16. DESIGN REQUIREMENTS
==================================================

Make the UI feel like Gmail but do NOT copy Gmail branding.

Use SkyOps branding.

Design characteristics:

- Clean white interface
- Light gray borders
- SkyOps blue primary color
- Rounded cards
- Minimal shadows
- Compact email rows
- Gmail-style sidebar
- Gmail-style search
- Professional enterprise typography
- Clear status badges
- Responsive layout
- Keyboard-friendly
- Loading skeletons
- Empty states
- Error states
- Toast notifications

Desktop is the primary target.

Also make it usable on tablet.

==================================================
17. IMPORTANT EXISTING SKYOPS INTEGRATION
==================================================

This is not a standalone email application.

It must integrate with the existing SkyOps PNR system.

From a PNR page:

PNR
→ CRM
→ Email
→ Conversation
→ Actions
→ Operations

The existing PNR data should remain the source of truth.

CRM should reference the existing PNR instead of duplicating flight data.

==================================================
18. DELIVERABLE
==================================================

Generate the complete implementation.

Frontend:

Angular components
HTML
SCSS
TypeScript
Models
Services
SignalR service
Routing
Guards if required

Backend:

ASP.NET Core controllers
Services
DTOs
Entities
EF Core DbContext
Migrations
MailKit SMTP service
SignalR Hub
Token service
Validation
Logging
Exception handling

Database:

SQL Server tables
Relationships
Indexes
Foreign keys
Migrations

Also provide:

1. Folder structure
2. Installation commands
3. appsettings.json example
4. User Secrets configuration
5. SQL migration
6. API examples
7. Angular API service
8. SignalR implementation
9. SMTP Test Connection implementation
10. Email template for Agent/Remark
11. Email template for Customer/CTEM
12. Secure action-link implementation

backend "D:\Seshadrinath-Workbench\SkyOpsQueueIntelligence"
Frontend "D:\Seshadrinath-Workbench\SkyOps"

Do not give pseudo-code.

Give production-ready code that can be integrated into an existing Angular + ASP.NET Core SkyOps application.

Build the implementation incrementally, starting with:

PHASE 1:
Gmail-like Angular UI

PHASE 2:
CRM Case + Chat

PHASE 3:
Email action system

PHASE 4:
SMTP configuration

PHASE 5:
SignalR real-time updates

PHASE 6:
PNR integration

PHASE 7:
Security, logging and production hardening.