-- Day 5 效能調校：在 BookingPerfDb 產生 100 萬筆預約
--
-- 前提：已建立 BookingPerfDb 並套用 InitialCreate Migration（見同資料夾的 README.md）
-- 寫法：每張表都是一個 INSERT ... SELECT 搭配 GENERATE_SERIES（SQL Server 2022），不用迴圈逐筆新增
--
-- 資料分布：
--   師傅 200 位、客戶 10,000 位
--   第 n 筆預約：師傅 = n % 200，時段 = 該師傅的第 n / 200 個時段（每天 10:00～18:00 共 9 格）
--     → 每個 (師傅, 時段) 只出現一次，不會違反 UX_Bookings_Provider_Start_Active
--   日期範圍約 2025-05-01 ～ 2026-11-07；80% Active、20% Cancelled
--   客戶 = (n × 5761) % 10000：5761 與 10000 互質，每位客戶剛好 100 筆，平均分散在整段期間
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() <> N'BookingPerfDb'
BEGIN
    RAISERROR(N'This script only runs in BookingPerfDb.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.Bookings)
BEGIN
    RAISERROR(N'Bookings already has data. Recreate BookingPerfDb to start over.', 16, 1);
    RETURN;
END

DECLARE @ProviderCount int = 200;
DECLARE @CustomerCount int = 10000;
DECLARE @BookingCount int = 1000000;
DECLARE @FirstDay datetime2 = '2025-05-01';
DECLARE @SlotsPerDay int = 9;
DECLARE @OpeningHour int = 10;

DECLARE @ExistingProviders int = (SELECT COUNT(*) FROM dbo.Providers);
DECLARE @ExistingCustomers int = (SELECT COUNT(*) FROM dbo.Customers);

INSERT INTO dbo.Providers (Name, IsActive)
SELECT CONCAT(N'Provider ', FORMAT(value, '000')), 1
FROM GENERATE_SERIES(@ExistingProviders + 1, @ProviderCount);

INSERT INTO dbo.Customers (Name, Phone)
SELECT CONCAT(N'Customer ', FORMAT(value, '00000')), CONCAT('09', FORMAT(value, '00000000'))
FROM GENERATE_SERIES(@ExistingCustomers + 1, @CustomerCount);

-- 以 0 起算的序號對應實際的 Id，不假設 IDENTITY 一定連號
WITH ProviderIndex AS
(
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) - 1 AS Idx FROM dbo.Providers
),
CustomerIndex AS
(
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) - 1 AS Idx FROM dbo.Customers
),
Generated AS
(
    SELECT
        value AS N,
        value % @ProviderCount AS ProviderIdx,
        CAST((CAST(value AS bigint) * 5761) % @CustomerCount AS int) AS CustomerIdx,
        DATEADD(HOUR, @OpeningHour + (value / @ProviderCount) % @SlotsPerDay,
            DATEADD(DAY, (value / @ProviderCount) / @SlotsPerDay, @FirstDay)) AS StartTime,
        CASE WHEN value % 10 < 8 THEN 1 ELSE 2 END AS Status
    FROM GENERATE_SERIES(0, @BookingCount - 1)
)
INSERT INTO dbo.Bookings (ProviderId, ServiceId, CustomerId, StartTime, EndTime, Status, CreatedAt, CancelledAt)
SELECT
    p.Id,
    1 + g.N % 3,
    c.Id,
    g.StartTime,
    DATEADD(MINUTE, 60, g.StartTime),
    g.Status,
    DATEADD(DAY, -7, g.StartTime),
    CASE WHEN g.Status = 2 THEN DATEADD(DAY, -1, g.StartTime) END
FROM Generated AS g
JOIN ProviderIndex AS p ON p.Idx = g.ProviderIdx
JOIN CustomerIndex AS c ON c.Idx = g.CustomerIdx;

-- 大量寫入後更新統計資訊，讓最佳化工具依實際分布選擇執行計畫
UPDATE STATISTICS dbo.Bookings WITH FULLSCAN;

SELECT
    (SELECT COUNT(*) FROM dbo.Providers) AS Providers,
    (SELECT COUNT(*) FROM dbo.Customers) AS Customers,
    (SELECT COUNT(*) FROM dbo.Bookings) AS Bookings,
    (SELECT MIN(StartTime) FROM dbo.Bookings) AS FirstStart,
    (SELECT MAX(StartTime) FROM dbo.Bookings) AS LastStart;
