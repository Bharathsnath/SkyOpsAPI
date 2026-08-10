-- SQL migration: create ADM tables
-- Run this against your TransDB or LogDB as appropriate

CREATE TABLE IF NOT EXISTS adm_sales_audit
(
    Id BIGINT PRIMARY KEY AUTO_INCREMENT,
    Pnr VARCHAR(10) NOT NULL,
    TicketNo VARCHAR(20),
    AgencyPcc VARCHAR(10),
    TicketDate DATE,
    TicketAmount DECIMAL(12,2),
    Agent VARCHAR(20),
    CreatedDate DATETIME,
    UNIQUE KEY uq_adm_sales_audit_pnr (Pnr)
);

CREATE TABLE IF NOT EXISTS adm_analysis
(
    Id BIGINT PRIMARY KEY AUTO_INCREMENT,
    SalesAuditId BIGINT,
    Pnr VARCHAR(10),
    TicketNo VARCHAR(20),
    TicketPcc VARCHAR(10),
    BookingPcc VARCHAR(10),
    TicketMarket VARCHAR(30),
    BookingMarket VARCHAR(30),
    IsCrossBorder TINYINT(1),
    ChurnedSegmentCount INT,
    IsChurnedSegment TINYINT(1),
    MarriedSegmentCount INT,
    IsMarriedSegment TINYINT(1),
    RiskScore INT,
    Remarks VARCHAR(255),
    TransactionId VARCHAR(50) NULL,
    CreatedDate DATETIME,
    UNIQUE KEY uq_adm_analysis_pnr (Pnr)
);

-- PCC to Market master table (replaces hardcoded dictionary)
CREATE TABLE IF NOT EXISTS adm_pcc_market
(
    Id INT PRIMARY KEY AUTO_INCREMENT,
    Pcc VARCHAR(10) NOT NULL,
    Market VARCHAR(50) NOT NULL,
    UNIQUE KEY uq_adm_pcc_market_pcc (Pcc)
);

-- Seed initial PCC market mappings
INSERT IGNORE INTO adm_pcc_market (Pcc, Market) VALUES
    ('3A78', 'India'),
    ('8FR2', 'UAE'),
    ('4ABC', 'USA');

CREATE TABLE IF NOT EXISTS HistoryItenary
(
    Id           BIGINT       PRIMARY KEY AUTO_INCREMENT,
    PccCode      VARCHAR(20)  NOT NULL,
    HostCommand  VARCHAR(500) NOT NULL,
    ResponseText MEDIUMTEXT   NULL,
    UplId        VARCHAR(100) NULL,
    Pnr          VARCHAR(10)  NULL,
    ExecutedAt   DATETIME     NOT NULL,
    CONSTRAINT chk_hi_command CHECK (HostCommand LIKE '*HI%')
);

-- Migration: add Pnr column if table already exists
-- ALTER TABLE HistoryItenary ADD COLUMN Pnr VARCHAR(10) NULL;
