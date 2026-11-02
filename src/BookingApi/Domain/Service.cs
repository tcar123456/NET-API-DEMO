namespace BookingApi.Domain;

public class Service
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }
}
