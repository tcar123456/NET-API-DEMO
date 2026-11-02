# 多階段建置：用 SDK 映像檔編譯，只把編譯結果複製到較小的 ASP.NET 執行環境映像檔
# 建置內容（context）是 Git 根目錄，見 docker-compose.yml

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先只複製 csproj 並還原套件：程式碼改動但套件沒變時，這一層可以直接用快取
COPY src/BookingApi/BookingApi.csproj src/BookingApi/
RUN dotnet restore src/BookingApi/BookingApi.csproj

COPY src/BookingApi/ src/BookingApi/
RUN dotnet publish src/BookingApi/BookingApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8 映像檔內建非 root 使用者，降低容器被入侵時的權限
USER $APP_UID

# .NET 8 映像檔預設監聽 8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookingApi.dll"]
