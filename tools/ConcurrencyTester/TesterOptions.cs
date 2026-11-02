namespace ConcurrencyTester;

public record TesterOptions(string Scenario, int Requests, string BaseUrl, int ProviderId)
{
    public static TesterOptions Parse(string[] args)
    {
        var options = new TesterOptions(Scenario: "create", Requests: 100, BaseUrl: "http://localhost:5120", ProviderId: 1);

        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            var value = args[i + 1];
            options = args[i] switch
            {
                "--scenario" => options with { Scenario = value },
                "--requests" => options with { Requests = int.Parse(value) },
                "--base-url" => options with { BaseUrl = value },
                "--provider" => options with { ProviderId = int.Parse(value) },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'."),
            };
        }

        return options;
    }
}

public record RequestResult(int StatusCode, string? Detail);

public record SlotDto(DateTime StartTime, DateTime EndTime);
