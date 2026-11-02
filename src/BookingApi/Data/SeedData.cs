using BookingApi.Domain;

namespace BookingApi.Data;

// HasData 的種子資料必須明確指定主鍵，EF Core 才能在之後的 Migration 比對新增、修改或刪除哪些資料
public static class SeedData
{
    public static readonly Provider[] Providers =
    [
        new() { Id = 1, Name = "王小美", IsActive = true },
        new() { Id = 2, Name = "林雅婷", IsActive = true },
        new() { Id = 3, Name = "陳志明", IsActive = true },
    ];

    public static readonly Service[] Services =
    [
        new() { Id = 1, Name = "精油按摩", Price = 1200m, DurationMinutes = 60 },
        new() { Id = 2, Name = "臉部保養", Price = 1500m, DurationMinutes = 60 },
        new() { Id = 3, Name = "肩頸放鬆", Price = 900m, DurationMinutes = 60 },
    ];

    public static readonly Customer[] Customers =
    [
        new() { Id = 1, Name = "張家豪", Phone = "0912000001" },
        new() { Id = 2, Name = "李宜蓁", Phone = "0912000002" },
        new() { Id = 3, Name = "黃柏翰", Phone = "0912000003" },
        new() { Id = 4, Name = "吳佩珊", Phone = "0912000004" },
        new() { Id = 5, Name = "劉冠廷", Phone = "0912000005" },
    ];
}
