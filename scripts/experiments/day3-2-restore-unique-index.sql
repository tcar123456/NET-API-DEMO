-- Day 3 對照實驗，步驟 2：清理重複預約並還原唯一篩選索引
-- 有重複的 Active 預約時無法建立唯一索引，因此先把重複的資料改為 Cancelled（軟取消，不刪除），
-- 每組 (ProviderId, StartTime) 只保留 Id 最小的那筆 Active。
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

PRINT '--- Duplicate Active bookings before cleanup';
SELECT ProviderId, StartTime, COUNT(*) AS ActiveCount
FROM dbo.Bookings
WHERE Status = 1
GROUP BY ProviderId, StartTime
HAVING COUNT(*) > 1
ORDER BY StartTime;

WITH Ranked AS
(
    SELECT Status, CancelledAt,
           ROW_NUMBER() OVER (PARTITION BY ProviderId, StartTime ORDER BY Id) AS RowNumber
    FROM dbo.Bookings
    WHERE Status = 1
)
UPDATE Ranked
-- 容器的時區是 UTC，換算成台灣時間（UTC+8）以符合專案的時間規則
SET Status = 2, CancelledAt = DATEADD(HOUR, 8, SYSUTCDATETIME())
WHERE RowNumber > 1;
PRINT CONCAT('Cancelled duplicate bookings: ', @@ROWCOUNT);

-- 與 InitialCreate Migration 建立的定義相同
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Bookings_Provider_Start_Active' AND object_id = OBJECT_ID(N'dbo.Bookings'))
BEGIN
    CREATE UNIQUE INDEX [UX_Bookings_Provider_Start_Active]
        ON [dbo].[Bookings] ([ProviderId], [StartTime])
        WHERE [Status] = 1;
    PRINT 'Restored UX_Bookings_Provider_Start_Active';
END

SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE name = N'UX_Bookings_Provider_Start_Active';
