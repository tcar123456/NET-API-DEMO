using BookingApi.Domain;
using BookingApi.Dtos;
using BookingApi.Exceptions;
using BookingApi.Repositories;
using BookingApi.Services;
using BookingApi.Tests.TestDoubles;
using NSubstitute;

namespace BookingApi.Tests.Services;

public class BookingServiceTests
{
    // 測試中的「現在」：2026-11-20 13:30（台灣時間）
    private static readonly DateTime Now = new(2026, 11, 20, 13, 30, 0);

    private readonly IBookingRepository _bookingRepository = Substitute.For<IBookingRepository>();
    private readonly IProviderRepository _providerRepository = Substitute.For<IProviderRepository>();
    private readonly IServiceRepository _serviceRepository = Substitute.For<IServiceRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IIdempotencyRepository _idempotencyRepository = Substitute.For<IIdempotencyRepository>();
    private readonly BookingService _sut;

    private readonly Provider _provider = new() { Id = 1, Name = "Provider", IsActive = true };
    private readonly Service _service = new() { Id = 2, Name = "Service", Price = 1000m, DurationMinutes = 60 };
    private readonly Customer _customer = new() { Id = 3, Name = "Customer", Phone = "0912000000" };

    // 透過 IBookingRepository.Add 傳入的預約，用來檢查 Service 建立的內容
    private Booking? _addedBooking;

    public BookingServiceTests()
    {
        _providerRepository.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _serviceRepository.GetByIdAsync(_service.Id, Arg.Any<CancellationToken>()).Returns(_service);
        _customerRepository.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);

        _sut = new BookingService(
            _bookingRepository,
            _providerRepository,
            _serviceRepository,
            _customerRepository,
            _idempotencyRepository,
            FixedTimeProvider.AtTaiwanTime(Now));
    }

    // ---------- CreateAsync：時間規則 ----------

    [Theory]
    [InlineData(0, 13)]     // 今天 13:00，現在是 13:30，已過去
    [InlineData(-1, 14)]    // 昨天
    public async Task CreateAsync_PastTime_ThrowsBusinessRuleException(int dayOffset, int hour)
    {
        var startTime = Now.Date.AddDays(dayOffset).AddHours(hour);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(startTime), CancellationToken.None));

        Assert.Contains("past", exception.Message);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(19)]
    [InlineData(23)]
    public async Task CreateAsync_OutsideBusinessHours_ThrowsBusinessRuleException(int hour)
    {
        var startTime = Now.Date.AddDays(1).AddHours(hour);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(startTime), CancellationToken.None));

        Assert.Contains("between", exception.Message);
    }

    [Theory]
    [InlineData(30, 0)]
    [InlineData(0, 1)]
    public async Task CreateAsync_NotOnTheHour_ThrowsBusinessRuleException(int minute, int second)
    {
        var startTime = Now.Date.AddDays(1).AddHours(14).AddMinutes(minute).AddSeconds(second);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(startTime), CancellationToken.None));

        Assert.Contains("on the hour", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_TimeWithTimeZone_ThrowsBusinessRuleException()
    {
        var startTime = DateTime.SpecifyKind(Now.Date.AddDays(1).AddHours(14), DateTimeKind.Utc);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(startTime), CancellationToken.None));
    }

    [Theory]
    [InlineData(10)]    // 第一個時段
    [InlineData(18)]    // 最後一個時段
    public async Task CreateAsync_FirstAndLastSlot_Succeeds(int hour)
    {
        var startTime = Now.Date.AddDays(1).AddHours(hour);
        SetUpSuccessfulSave();

        var response = await _sut.CreateAsync(Request(startTime), CancellationToken.None);

        Assert.Equal(startTime, response.StartTime);
    }

    [Fact]
    public async Task CreateAsync_NextSlotToday_Succeeds()
    {
        // 現在 13:30，14:00 仍是未來
        var startTime = Now.Date.AddHours(14);
        SetUpSuccessfulSave();

        var response = await _sut.CreateAsync(Request(startTime), CancellationToken.None);

        Assert.Equal(startTime, response.StartTime);
    }

    // ---------- CreateAsync：資料是否存在 ----------

    [Fact]
    public async Task CreateAsync_ProviderNotFound_ThrowsNotFoundException()
    {
        var request = Request(Tomorrow(14)) with { ProviderId = 99 };

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(request, CancellationToken.None));

        Assert.Contains("Provider 99", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_InactiveProvider_ThrowsNotFoundException()
    {
        _provider.IsActive = false;

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ServiceNotFound_ThrowsNotFoundException()
    {
        var request = Request(Tomorrow(14)) with { ServiceId = 99 };

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(request, CancellationToken.None));

        Assert.Contains("Service 99", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_CustomerNotFound_ThrowsNotFoundException()
    {
        var request = Request(Tomorrow(14)) with { CustomerId = 99 };

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(request, CancellationToken.None));

        Assert.Contains("Customer 99", exception.Message);
    }

    // ---------- CreateAsync：重複預約與寫入內容 ----------

    [Fact]
    public async Task CreateAsync_SlotAlreadyBooked_ThrowsConflictAndDoesNotSave()
    {
        _bookingRepository
            .HasActiveBookingAsync(_provider.Id, Tomorrow(14), Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), CancellationToken.None));

        _bookingRepository.DidNotReceive().Add(Arg.Any<Booking>());
        await _bookingRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_SavesActiveBookingWithEndTimeAndCreatedAt()
    {
        SetUpSuccessfulSave();

        await _sut.CreateAsync(Request(Tomorrow(14)), CancellationToken.None);

        Assert.NotNull(_addedBooking);
        Assert.Equal(Tomorrow(14), _addedBooking.StartTime);
        Assert.Equal(Tomorrow(15), _addedBooking.EndTime);
        Assert.Equal(BookingStatus.Active, _addedBooking.Status);
        Assert.Equal(Now, _addedBooking.CreatedAt);
        await _bookingRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---------- GetAvailableSlotsAsync ----------

    [Fact]
    public async Task GetAvailableSlotsAsync_ExcludesPastAndBookedSlots()
    {
        // 今天現在 13:30：10~13 點已過去；15 點已被預約 → 剩 14、16、17、18
        _bookingRepository
            .GetActiveStartTimesAsync(_provider.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateTime> { Now.Date.AddHours(15) });

        var slots = await _sut.GetAvailableSlotsAsync(
            _provider.Id, DateOnly.FromDateTime(Now), CancellationToken.None);

        Assert.Equal(new[] { 14, 16, 17, 18 }, slots.Select(s => s.StartTime.Hour));
        Assert.All(slots, s => Assert.Equal(s.StartTime.AddMinutes(60), s.EndTime));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_FutureDateWithNoBookings_ReturnsAllNineSlots()
    {
        _bookingRepository
            .GetActiveStartTimesAsync(_provider.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateTime>());

        var slots = await _sut.GetAvailableSlotsAsync(
            _provider.Id, DateOnly.FromDateTime(Now.AddDays(1)), CancellationToken.None);

        Assert.Equal(9, slots.Count);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ProviderNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.GetAvailableSlotsAsync(99, DateOnly.FromDateTime(Now), CancellationToken.None));
    }

    // ---------- CancelAsync ----------

    [Fact]
    public async Task CancelAsync_ActiveBooking_SetsCancelledStatusAndTime()
    {
        var booking = BookingWithDetails(BookingStatus.Active);
        _bookingRepository.GetByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var response = await _sut.CancelAsync(booking.Id, CancellationToken.None);

        Assert.Equal("Cancelled", response.Status);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(Now, booking.CancelledAt);
        await _bookingRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ThrowsConflictException()
    {
        var booking = BookingWithDetails(BookingStatus.Cancelled);
        _bookingRepository.GetByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CancelAsync(booking.Id, CancellationToken.None));

        await _bookingRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_BookingNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CancelAsync(99, CancellationToken.None));
    }

    // ---------- GetCustomerBookingsAsync ----------

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetCustomerBookingsAsync_InvalidPaging_ThrowsBusinessRuleException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.GetCustomerBookingsAsync(_customer.Id, page, pageSize, CancellationToken.None));
    }

    // ---------- CreateAsync：Idempotency-Key ----------

    [Fact]
    public async Task CreateAsync_WithoutIdempotencyKey_DoesNotUseIdempotencyStore()
    {
        SetUpSuccessfulSave();

        var result = await _sut.CreateAsync(Request(Tomorrow(14)), idempotencyKey: null, CancellationToken.None);

        Assert.False(result.IsReplay);
        await _idempotencyRepository.DidNotReceive().TryReserveAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankIdempotencyKey_ThrowsBusinessRuleException(string key)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), key, CancellationToken.None));

        await _idempotencyRepository.DidNotReceive().TryReserveAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_IdempotencyKeyTooLong_ThrowsBusinessRuleException()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), new string('k', 101), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_NewIdempotencyKey_ReservesCreatesAndStoresResponse()
    {
        SetUpSuccessfulSave();
        var reserved = CaptureReservation(succeeds: true);

        var result = await _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None);

        Assert.False(result.IsReplay);
        Assert.Equal("key-1", reserved()?.Key);
        Assert.Equal(64, reserved()?.RequestHash.Length);
        await _idempotencyRepository.Received(1).CompleteAsync(
            "key-1", 201, Arg.Is<string>(body => body.Contains("\"startTime\"")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_SameKeySameRequest_ReplaysFirstResultWithoutCreatingAgain()
    {
        // 第一次：正常建立，記下寫入的雜湊與回應內容
        SetUpSuccessfulSave();
        var reserved = CaptureReservation(succeeds: true);
        string? storedBody = null;
        _idempotencyRepository
            .CompleteAsync("key-1", 201, Arg.Do<string>(body => storedBody = body), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var first = await _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None);

        // 第二次：Key 已存在且已完成
        CaptureReservation(succeeds: false);
        _idempotencyRepository.GetAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(CompletedRecord("key-1", HashOf(reserved), storedBody));

        var second = await _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None);

        Assert.True(second.IsReplay);
        Assert.Equal(first.Booking, second.Booking);
        _bookingRepository.Received(1).Add(Arg.Any<Booking>());
    }

    [Fact]
    public async Task CreateAsync_SameKeyDifferentRequest_ThrowsIdempotencyKeyReusedException()
    {
        SetUpSuccessfulSave();
        var reserved = CaptureReservation(succeeds: true);
        await _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None);

        CaptureReservation(succeeds: false);
        _idempotencyRepository.GetAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(CompletedRecord("key-1", HashOf(reserved), "{}"));

        await Assert.ThrowsAsync<IdempotencyKeyReusedException>(
            () => _sut.CreateAsync(Request(Tomorrow(15)), "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_SameKeyStillProcessing_ThrowsConflictException()
    {
        SetUpSuccessfulSave();
        var reserved = CaptureReservation(succeeds: true);
        await _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None);

        CaptureReservation(succeeds: false);
        var processing = CompletedRecord("key-1", HashOf(reserved), responseBody: null);
        processing.StatusCode = null;
        _idempotencyRepository.GetAsync("key-1", Arg.Any<CancellationToken>()).Returns(processing);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None));

        Assert.Contains("still being processed", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_CreationFails_ReleasesIdempotencyKey()
    {
        CaptureReservation(succeeds: true);
        _bookingRepository
            .HasActiveBookingAsync(_provider.Id, Tomorrow(14), Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateAsync(Request(Tomorrow(14)), "key-1", CancellationToken.None));

        await _idempotencyRepository.Received(1).ReleaseAsync("key-1", Arg.Any<CancellationToken>());
        await _idempotencyRepository.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- 測試輔助 ----------

    // 設定 TryReserveAsync 的結果，並回傳一個可取得「最後一次寫入的紀錄」的函式
    private Func<IdempotencyRecord?> CaptureReservation(bool succeeds)
    {
        IdempotencyRecord? captured = null;
        _idempotencyRepository
            .TryReserveAsync(Arg.Do<IdempotencyRecord>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(succeeds);
        return () => captured;
    }

    private static string HashOf(Func<IdempotencyRecord?> reserved)
    {
        return reserved()?.RequestHash ?? throw new InvalidOperationException("TryReserveAsync was not called.");
    }

    private static IdempotencyRecord CompletedRecord(string key, string requestHash, string? responseBody)
    {
        return new IdempotencyRecord
        {
            Key = key,
            RequestHash = requestHash,
            StatusCode = 201,
            ResponseBody = responseBody,
            CreatedAt = Now,
        };
    }


    private CreateBookingRequest Request(DateTime startTime)
    {
        return new CreateBookingRequest(_provider.Id, _service.Id, _customer.Id, startTime);
    }

    private static DateTime Tomorrow(int hour) => Now.Date.AddDays(1).AddHours(hour);

    private Booking BookingWithDetails(BookingStatus status)
    {
        return new Booking
        {
            Id = 10,
            ProviderId = _provider.Id,
            ServiceId = _service.Id,
            CustomerId = _customer.Id,
            StartTime = Tomorrow(14),
            EndTime = Tomorrow(15),
            Status = status,
            CreatedAt = Now,
            Provider = _provider,
            Service = _service,
            Customer = _customer,
        };
    }

    // 建立成功後 Service 會用 Id 重新查詢一次完整資料，這裡回傳剛才加入的那筆
    private void SetUpSuccessfulSave()
    {
        _bookingRepository.Add(Arg.Do<Booking>(b => _addedBooking = b));
        _bookingRepository
            .GetByIdWithDetailsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var booking = _addedBooking ?? throw new InvalidOperationException("Add was not called.");
                booking.Provider = _provider;
                booking.Service = _service;
                booking.Customer = _customer;
                return booking;
            });
    }
}
