using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingApi.Data.Configurations;

public class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .HasMaxLength(50)
            .IsRequired();

        // 資料庫預設值為 1。bool 的 CLR 預設值是 false，EF Core 預設會把 false 視為「沒設定」而改用資料庫預設值，
        // 導致無法新增 IsActive = false 的資料。把 sentinel 設為 true，EF Core 只在值為 true 時省略此欄位。
        builder.Property(p => p.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        builder.HasData(SeedData.Providers);
    }
}
