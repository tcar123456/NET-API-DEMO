using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ConcurrencyTester;

// 用法：
//   dotnet run --project tools/ConcurrencyTester                       同時送出 100 個搶同一時段的建立預約請求
//   dotnet run --project tools/ConcurrencyTester -- --scenario cancel  同時送出 100 個取消同一筆預約的請求
//   其他參數：--requests 100  --base-url http://localhost:5120  --provider 1

var options = TesterOptions.Parse(args);
using var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };

var slot = await FindAvailableSlotAsync(client, options.ProviderId);
Console.WriteLine($"Target : provider {options.ProviderId}, slot {slot:yyyy-MM-dd HH:mm}");
Console.WriteLine($"Scenario: {options.Scenario}, {options.Requests} concurrent requests");
Console.WriteLine();

var results = options.Scenario switch
{
    "create" => await RunCreateScenarioAsync(client, options, slot),
    "cancel" => await RunCancelScenarioAsync(client, options, slot),
    _ => throw new ArgumentException($"Unknown scenario '{options.Scenario}'. Use 'create' or 'cancel'."),
};

PrintSummary(results);

// 預期：剛好 1 個請求成功，其餘都是 409
var successCount = results.Count(r => r.StatusCode is 200 or 201);
var conflictCount = results.Count(r => r.StatusCode == 409);
var passed = successCount == 1 && conflictCount == options.Requests - 1;
Console.WriteLine(passed ? "RESULT: PASS (exactly 1 success)" : "RESULT: FAIL");
return passed ? 0 : 1;

static async Task<List<RequestResult>> RunCreateScenarioAsync(HttpClient client, TesterOptions options, DateTime slot)
{
    return await SendConcurrentlyAsync(options.Requests, i => client.PostAsJsonAsync("/api/bookings", new
    {
        providerId = options.ProviderId,
        serviceId = 1,
        customerId = i % 5 + 1,
        startTime = slot.ToString("yyyy-MM-ddTHH:mm:ss"),
    }));
}

static async Task<List<RequestResult>> RunCancelScenarioAsync(HttpClient client, TesterOptions options, DateTime slot)
{
    // 先建立一筆預約，再讓所有請求同時取消它
    var createResponse = await client.PostAsJsonAsync("/api/bookings", new
    {
        providerId = options.ProviderId,
        serviceId = 1,
        customerId = 1,
        startTime = slot.ToString("yyyy-MM-ddTHH:mm:ss"),
    });
    createResponse.EnsureSuccessStatusCode();
    var booking = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
    var bookingId = booking.GetProperty("id").GetInt32();
    Console.WriteLine($"Created booking {bookingId} to cancel");

    return await SendConcurrentlyAsync(options.Requests, _ => client.PostAsync($"/api/bookings/{bookingId}/cancel", null));
}

static async Task<List<RequestResult>> SendConcurrentlyAsync(int count, Func<int, Task<HttpResponseMessage>> send)
{
    // 所有 Task 先建立好並在 gate 前等待，放行後才同時送出，讓請求盡量在同一瞬間抵達
    var gate = new TaskCompletionSource();
    var tasks = Enumerable.Range(0, count).Select(async i =>
    {
        await gate.Task;
        using var response = await send(i);
        return new RequestResult((int)response.StatusCode, await ReadDetailAsync(response));
    }).ToList();

    var stopwatch = Stopwatch.StartNew();
    gate.SetResult();
    var results = await Task.WhenAll(tasks);
    stopwatch.Stop();

    Console.WriteLine($"Elapsed: {stopwatch.ElapsedMilliseconds} ms");
    return results.ToList();
}

static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
{
    if (response.IsSuccessStatusCode)
    {
        return null;
    }

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    return problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
}

// 從明天開始往後找第一個空時段，每次執行都會用掉一個時段，因此不會撞到上一次的結果
static async Task<DateTime> FindAvailableSlotAsync(HttpClient client, int providerId)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
    for (var day = 1; day <= 60; day++)
    {
        var date = today.AddDays(day);
        var slots = await client.GetFromJsonAsync<List<SlotDto>>(
            $"/api/providers/{providerId}/available-slots?date={date:yyyy-MM-dd}");
        if (slots is { Count: > 0 })
        {
            return slots[0].StartTime;
        }
    }

    throw new InvalidOperationException("No available slot found in the next 60 days.");
}

static void PrintSummary(List<RequestResult> results)
{
    Console.WriteLine();
    Console.WriteLine("Status  Count  Detail");
    foreach (var group in results
        .GroupBy(r => (r.StatusCode, r.Detail))
        .OrderBy(g => g.Key.StatusCode))
    {
        Console.WriteLine($"{group.Key.StatusCode,-7} {group.Count(),-6} {group.Key.Detail ?? "-"}");
    }

    Console.WriteLine();
}
