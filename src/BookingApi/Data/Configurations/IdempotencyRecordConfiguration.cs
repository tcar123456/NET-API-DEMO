using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingApi.Data.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        // Key 是主鍵：同一個 Key 同時有兩個請求時，只有一個能寫入成功，另一個收到主鍵衝突
        builder.HasKey(r => r.Key);

        builder.Property(r => r.Key)
            .HasMaxLength(100);

        builder.Property(r => r.RequestHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnType("datetime2");
    }
}
