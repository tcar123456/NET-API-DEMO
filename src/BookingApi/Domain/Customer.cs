namespace BookingApi.Domain;

public class Customer
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Phone { get; set; }
}
