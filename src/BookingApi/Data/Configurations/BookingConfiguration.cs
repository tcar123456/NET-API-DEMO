using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingApi.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.StartTime).HasColumnType("datetime2");
        builder.Property(b => b.EndTime).HasColumnType("datetime2");
        builder.Property(b => b.CreatedAt).HasColumnType("datetime2");
        builder.Property(b => b.CancelledAt).HasColumnType("datetime2");

        builder.Property(b => b.RowVersion).IsRowVersion();

        // 預約是歷史紀錄，不允許因為刪除師傅、服務或客戶而連帶刪除預約
        builder.HasOne(b => b.Provider)
            .WithMany()
            .HasForeignKey(b => b.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Service)
            .WithMany()
            .HasForeignKey(b => b.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Customer)
            .WithMany()
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // 防止重複預約的核心：同一師傅、同一開始時間，只能有一筆 Active 預約。
        // 篩選條件只納入 Active，已取消的預約不佔用唯一性，該時段可以再被預約。
        // 1 對應 BookingStatus.Active，篩選條件是 SQL 字串，無法直接引用 enum。
        builder.HasIndex(b => new { b.ProviderId, b.StartTime })
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_Bookings_Provider_Start_Active");

        // 查詢某客戶在一段期間內的預約並依時間排序（Day 5 效能調校）：
        // CustomerId 等值 + StartTime 範圍可直接 Seek，且索引已依 StartTime 排序，不需要再 Sort；
        // INCLUDE 查詢會用到的欄位，不必逐筆回叢集索引查找（Key Lookup）。
        // 第一個欄位是 CustomerId，也能支援外鍵查詢，EF Core 因此不再另外建立 IX_Bookings_CustomerId。
        builder.HasIndex(b => new { b.CustomerId, b.StartTime })
            .IncludeProperties(b => new { b.ProviderId, b.ServiceId, b.EndTime, b.Status })
            .HasDatabaseName("IX_Bookings_Customer_Start");
    }
}
