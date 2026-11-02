-- Day 5 階段 2：還原單欄外鍵索引 IX_Bookings_CustomerId（與 InitialCreate Migration 的定義相同）
SET QUOTED_IDENTIFIER ON;

IF DB_NAME() <> N'BookingPerfDb'
BEGIN
    RAISERROR(N'This script only runs in BookingPerfDb.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_CustomerId' AND object_id = OBJECT_ID(N'dbo.Bookings'))
BEGIN
    CREATE INDEX [IX_Bookings_CustomerId] ON [dbo].[Bookings] ([CustomerId]);
    PRINT 'Created IX_Bookings_CustomerId';
END
