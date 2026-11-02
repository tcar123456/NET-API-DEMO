-- Day 5 效能調校：量測「某客戶近半年預約，依時間排序」
--
-- 在 SSMS 執行：
--   1. 資料庫選 BookingPerfDb
--   2. 開啟「包含實際執行計畫」（Ctrl+M）
--   3. 執行後看「訊息」頁籤的 logical reads 與 elapsed time，以及「執行計畫」頁籤
--
-- 用 sp_executesql 傳參數，與 EF Core 送出的參數化查詢相同，執行計畫可以重複使用
SET STATISTICS IO, TIME ON;

EXEC sp_executesql
    N'SELECT Id, ProviderId, ServiceId, StartTime, EndTime, Status
      FROM dbo.Bookings
      WHERE CustomerId = @CustomerId
        AND StartTime >= @From
        AND StartTime < @To
      ORDER BY StartTime DESC;',
    N'@CustomerId int, @From datetime2, @To datetime2',
    @CustomerId = 4242,
    @From = '2026-05-02',
    @To = '2026-11-03';

SET STATISTICS IO, TIME OFF;
