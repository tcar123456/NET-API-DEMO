-- Day 3 對照實驗，步驟 1：暫時拿掉防止重複預約的唯一篩選索引
-- 拿掉後只剩 Service 的「先查再寫」，用來觀察並發下是否會出現重複預約。
-- 實驗結束後務必執行 day3-2-restore-unique-index.sql 還原。
SET QUOTED_IDENTIFIER ON;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Bookings_Provider_Start_Active' AND object_id = OBJECT_ID(N'dbo.Bookings'))
BEGIN
    DROP INDEX [UX_Bookings_Provider_Start_Active] ON [dbo].[Bookings];
    PRINT 'Dropped UX_Bookings_Provider_Start_Active';
END
