-- Day 5 階段 1：拿掉 EF Core 依慣例為外鍵建立的 IX_Bookings_CustomerId，量測「完全沒有可用索引」的情況
-- 只允許在 BookingPerfDb 執行；量測完務必執行 04-stage2-restore-customer-index.sql 還原
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() <> N'BookingPerfDb'
BEGIN
    RAISERROR(N'This script only runs in BookingPerfDb.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_CustomerId' AND object_id = OBJECT_ID(N'dbo.Bookings'))
BEGIN
    DROP INDEX [IX_Bookings_CustomerId] ON [dbo].[Bookings];
    PRINT 'Dropped IX_Bookings_CustomerId';
END
