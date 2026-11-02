using BookingApi.Domain;
using BookingApi.Dtos;
using BookingApi.Exceptions;
using BookingApi.Repositories;

namespace BookingApi.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IProviderRepository _providerRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly TimeProvider _timeProvider;

    public BookingService(
        IBookingRepository bookingRepository,
        IProviderRepository providerRepository,
        IServiceRepository serviceRepository,
        ICustomerRepository customerRepository,
        TimeProvider timeProvider)
    {
        _bookingRepository = bookingRepository;
        _providerRepository = providerRepository;
        _serviceRepository = serviceRepository;
        _customerRepository = customerRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<TimeSlotResponse>> GetAvailableSlotsAsync(
        int providerId, DateOnly date, CancellationToken cancellationToken)
    {
        await GetActiveProviderAsync(providerId, cancellationToken);

        var dayStart = date.ToDateTime(new TimeOnly(BookingRules.OpeningHour, 0));
        var dayEnd = date.ToDateTime(new TimeOnly(BookingRules.ClosingHour, 0));

        var bookedStartTimes = (await _bookingRepository.GetActiveStartTimesAsync(
            providerId, dayStart, dayEnd, cancellationToken)).ToHashSet();
        var now = _timeProvider.GetTaiwanNow();

        var slots = new List<TimeSlotResponse>();
        for (var start = dayStart; start < dayEnd; start = start.AddMinutes(BookingRules.SlotMinutes))
        {
            if (start <= now || bookedStartTimes.Contains(start))
            {
                continue;
            }

            slots.Add(new TimeSlotResponse(start, start.AddMinutes(BookingRules.SlotMinutes)));
        }

        return slots;
    }

    public async Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        ValidateStartTime(request.StartTime);

        var provider = await GetActiveProviderAsync(request.ProviderId, cancellationToken);

        var service = await _serviceRepository.GetByIdAsync(request.ServiceId, cancellationToken)
            ?? throw new NotFoundException($"Service {request.ServiceId} was not found.");

        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException($"Customer {request.CustomerId} was not found.");

        // 先查再寫：一般情況下提供清楚的錯誤訊息。
        // 並發時兩個請求可能同時通過這個檢查，最終由資料庫唯一索引擋下（Day 3 處理）。
        if (await _bookingRepository.HasActiveBookingAsync(provider.Id, request.StartTime, cancellationToken))
        {
            throw new ConflictException(
                $"Provider {provider.Id} is already booked at {request.StartTime:yyyy-MM-dd HH:mm}.");
        }

        var booking = new Booking
        {
            ProviderId = provider.Id,
            ServiceId = service.Id,
            CustomerId = customer.Id,
            StartTime = request.StartTime,
            EndTime = request.StartTime.AddMinutes(service.DurationMinutes),
            Status = BookingStatus.Active,
            CreatedAt = _timeProvider.GetTaiwanNow(),
        };

        _bookingRepository.Add(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(booking.Id, cancellationToken);
    }

    public async Task<BookingResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var booking = await _bookingRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Booking {id} was not found.");

        return BookingResponse.FromEntity(booking);
    }

    public async Task<BookingResponse> CancelAsync(int id, CancellationToken cancellationToken)
    {
        var booking = await _bookingRepository.GetByIdForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Booking {id} was not found.");

        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException($"Booking {id} is already cancelled.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = _timeProvider.GetTaiwanNow();
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        return BookingResponse.FromEntity(booking);
    }

    public async Task<PagedResponse<BookingResponse>> GetCustomerBookingsAsync(
        int customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new BusinessRuleException("Page must be 1 or greater.");
        }

        if (pageSize < 1 || pageSize > BookingRules.MaxPageSize)
        {
            throw new BusinessRuleException($"PageSize must be between 1 and {BookingRules.MaxPageSize}.");
        }

        _ = await _customerRepository.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer {customerId} was not found.");

        var (items, totalCount) = await _bookingRepository.GetByCustomerAsync(
            customerId, page, pageSize, cancellationToken);

        return new PagedResponse<BookingResponse>(
            items.Select(BookingResponse.FromEntity).ToList(), page, pageSize, totalCount);
    }

    // 停用的師傅視同不存在，不能被查詢時段或預約
    private async Task<Provider> GetActiveProviderAsync(int providerId, CancellationToken cancellationToken)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId, cancellationToken);
        if (provider is null || !provider.IsActive)
        {
            throw new NotFoundException($"Provider {providerId} was not found.");
        }

        return provider;
    }

    private void ValidateStartTime(DateTime startTime)
    {
        // 帶 Z 或 +08:00 的時間會被解析成 Utc / Local，與「一律存台灣時間」的規則混用容易出錯，直接拒絕
        if (startTime.Kind != DateTimeKind.Unspecified)
        {
            throw new BusinessRuleException(
                "StartTime must be Taiwan local time without a time zone offset, e.g. 2026-11-20T14:00:00.");
        }

        if (startTime.TimeOfDay.Ticks % TimeSpan.TicksPerHour != 0)
        {
            throw new BusinessRuleException("StartTime must be on the hour, e.g. 14:00.");
        }

        var closingTime = startTime.Date.AddHours(BookingRules.ClosingHour);
        if (startTime.Hour < BookingRules.OpeningHour || startTime.AddMinutes(BookingRules.SlotMinutes) > closingTime)
        {
            throw new BusinessRuleException(
                $"StartTime must be between {BookingRules.OpeningHour}:00 and {BookingRules.ClosingHour - 1}:00.");
        }

        if (startTime <= _timeProvider.GetTaiwanNow())
        {
            throw new BusinessRuleException("Cannot book a time in the past.");
        }
    }
}
