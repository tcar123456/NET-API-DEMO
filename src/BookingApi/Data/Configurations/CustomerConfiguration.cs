using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingApi.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .HasMaxLength(50)
            .IsRequired();

        // 電話只會有數字與符號，用 varchar 而不是 nvarchar，每個字元省一半空間
        builder.Property(c => c.Phone)
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.HasData(SeedData.Customers);
    }
}
