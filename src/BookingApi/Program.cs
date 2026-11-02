using BookingApi.Data;
using BookingApi.Middleware;
using BookingApi.Repositories;
using BookingApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// 連線字串來自 User Secrets（開發）或環境變數（容器），不寫在 appsettings.json
var connectionString = builder.Configuration.GetConnectionString("BookingDb")
    ?? throw new InvalidOperationException("Connection string 'BookingDb' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// Repository 與 Service 依賴 Scoped 的 AppDbContext，因此同樣以 Scoped 註冊：每個 HTTP 請求一份
builder.Services.AddScoped<IProviderRepository, ProviderRepository>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IBookingService, BookingService>();

// 透過注入取得現在時間，單元測試可以換成固定時間
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers(options =>
{
    // 預設會把 action 名稱結尾的 Async 去掉，導致 CreatedAtAction(nameof(GetByIdAsync)) 找不到路由
    options.SuppressAsyncSuffixInActionNames = false;
});

// 讓沒有內容的錯誤回應（例如路由不存在的 404）也以 ProblemDetails 格式回傳
builder.Services.AddProblemDetails();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.

// 放在最前面，才能攔截後續所有 middleware 與 controller 拋出的例外
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
