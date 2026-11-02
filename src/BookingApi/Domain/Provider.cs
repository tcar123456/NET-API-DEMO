namespace BookingApi.Domain;

public class Provider
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}
