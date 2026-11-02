# 專案規劃：預約系統後端 API

> 技術選型請見 [TECH_STACK.md](./TECH_STACK.md)
> AI 協作規則請見 [CLAUDE.md](./CLAUDE.md)

---

## 一、專案目標

做一個「美容／按摩店家預約系統」的後端 API，重點不在功能多，而在以下四件事做紮實：

1. **並發控制**：100 個請求同時搶同一個時段，只有 1 筆成功。
2. **分層與依賴注入**：Controller → Service → Repository，透過介面溝通。
3. **MS SQL 設計與調校**：schema 設計、索引、執行計畫比較。
4. **可執行、可閱讀**：一個指令跑起來，README 說明設計決策。

---

## 二、專案結構

```
BookingApi/                         ← Git 根目錄，CLAUDE.md 放這裡
├── CLAUDE.md
├── PLAN.md
├── TECH_STACK.md
├── README.md
├── docker-compose.yml
├── .env.example                    ← 範例環境變數（.env 本身不進版控）
├── .gitignore
├── BookingApi.sln
├── src/
│   └── BookingApi/
│       ├── Controllers/
│       ├── Services/               ← 商業邏輯（介面 + 實作）
│       ├── Repositories/           ← 資料存取（介面 + 實作）
│       ├── Domain/                 ← Entity、Enum
│       ├── Dtos/                   ← Request / Response 物件
│       ├── Data/                   ← DbContext、Entity 設定、Seed
│       ├── Exceptions/             ← 自訂例外
│       ├── Middleware/             ← 全域錯誤處理
│       ├── Migrations/
│       └── Program.cs
├── tests/
│   └── BookingApi.Tests/
├── tools/
│   └── ConcurrencyTester/          ← 並發測試用的 Console 程式
└── scripts/
    └── perf/                       ← 效能調校用的 SQL 腳本
```

---

## 三、業務規則

為了讓專案聚焦，刻意簡化以下規則，並在 README 說明：

| 規則 | 內容 |
|---|---|
| 營業時間 | 每天 10:00～19:00 |
| 時段 | 固定 60 分鐘一格，開始時間必須是整點（10:00、11:00…18:00） |
| 服務長度 | 每個服務都佔 1 格（60 分鐘） |
| 時區 | 一律以台灣時間（UTC+8）儲存，使用 `datetime2` |
| 預約限制 | 不能預約過去的時間；不能超出營業時間；時段必須對齊整點 |
| 取消 | 軟取消：狀態改為 Cancelled，不刪除資料，保留歷史 |
| 重複預約 | 同一位師傅、同一開始時間，只能有一筆「有效」預約 |

> 討論點（寫進 README）：如果服務長度不一（30／60／90 分鐘），唯一索引無法防止「時段重疊」。
> 可行做法是拆成「每 30 分鐘一格」的時段佔用表，每格各自有唯一索引，一筆預約佔用多格，在同一個交易中寫入。

---

## 四、資料表設計

### Providers（師傅）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | int, PK, Identity | |
| Name | nvarchar(50), NOT NULL | |
| IsActive | bit, NOT NULL | 預設 1 |

### Services（服務項目）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | int, PK, Identity | |
| Name | nvarchar(50), NOT NULL | |
| Price | decimal(10,2), NOT NULL | |
| DurationMinutes | int, NOT NULL | 本專案固定 60 |

### Customers（客戶）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | int, PK, Identity | |
| Name | nvarchar(50), NOT NULL | |
| Phone | varchar(20), NOT NULL | |

### Bookings（預約）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | int, PK, Identity | |
| ProviderId | int, FK → Providers | |
| ServiceId | int, FK → Services | |
| CustomerId | int, FK → Customers | |
| StartTime | datetime2, NOT NULL | |
| EndTime | datetime2, NOT NULL | |
| Status | tinyint, NOT NULL | 1 = Active，2 = Cancelled |
| CreatedAt | datetime2, NOT NULL | |
| CancelledAt | datetime2, NULL | |
| RowVersion | rowversion | 樂觀鎖用（取消預約時） |

**索引**

| 名稱 | 內容 | 目的 |
|---|---|---|
| UX_Bookings_Provider_Start_Active | UNIQUE (ProviderId, StartTime) WHERE Status = 1 | **防止重複預約的核心** |
| ~~IX_Bookings_CustomerId~~ | (CustomerId) | EF Core 依慣例為外鍵自動建立（Day 1 Migration）；第 5 天作為「單欄索引」階段的對照，之後由 `AddCustomerStartIndex` Migration 移除（已被下方複合索引取代） |
| IX_Bookings_ServiceId | (ServiceId) | EF Core 依慣例為外鍵自動建立 |
| IX_Bookings_Customer_Start | (CustomerId, StartTime) INCLUDE (ProviderId, ServiceId, EndTime, Status) | 查客戶預約紀錄；第 5 天以 `AddCustomerStartIndex` Migration 加入，同時支援 CustomerId 外鍵 |

> ProviderId 沒有單獨的外鍵索引：EF Core 判斷 `UX_Bookings_Provider_Start_Active` 的第一個欄位已是 ProviderId，因此不另外建立。

### IdempotencyRecords（加分版才建立）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Key | nvarchar(100), PK | 用戶端送來的 Idempotency-Key |
| RequestHash | varchar(64), NOT NULL | 請求內容的雜湊，同 Key 不同內容要拒絕 |
| StatusCode | int, NOT NULL | 第一次的回應狀態碼 |
| ResponseBody | nvarchar(max), NOT NULL | 第一次的回應內容 |
| CreatedAt | datetime2, NOT NULL | |

---

## 五、API 設計

| 方法 | 路徑 | 說明 | 成功 | 可能的錯誤 |
|---|---|---|---|---|
| GET | /api/providers | 師傅列表 | 200 | |
| GET | /api/services | 服務列表 | 200 | |
| GET | /api/providers/{id}/available-slots?date=2026-11-20 | 某師傅某天的可預約時段 | 200 | 404 |
| POST | /api/bookings | 建立預約 | 201 + Location | 400、404、409 |
| GET | /api/bookings/{id} | 查單筆預約 | 200 | 404 |
| POST | /api/bookings/{id}/cancel | 取消預約 | 200 | 404、409 |
| GET | /api/customers/{id}/bookings?page=1&pageSize=20 | 客戶預約紀錄（分頁） | 200 | 404 |

**設計說明**

- 取消用 `POST /cancel` 而不是 `DELETE`：取消是狀態變更，資料保留；用 DELETE 會讓呼叫方誤以為資料被刪除。
- 錯誤一律回傳 ProblemDetails 格式（RFC 9457）。
- 409 Conflict 用在兩種情況：時段已被預約、取消時預約已被他人修改或已取消。

**建立預約 Request 範例**

```json
{
  "providerId": 1,
  "serviceId": 2,
  "customerId": 3,
  "startTime": "2026-11-20T14:00:00"
}
```

---

## 六、每日進度

> 每天結束前：commit、push，並用自己的話寫下當天學到的 3 個 C# 概念（寫在 `notes/` 或 commit 說明都可以）。

### Day 0：環境建置（約半天）✅ 2026-11-02 完成

**任務**
- [x] 安裝 .NET 8 SDK、Docker Desktop，確認 `dotnet --list-sdks` 有 8.0.x（8.0.422）
- [x] 安裝 SSMS（SSMS 22.11.3，透過 `winget install Microsoft.SQLServerManagementStudio.22`）
- [x] 用 `global.json` 鎖定 SDK 版本為 8.0.422
- [x] 安裝 `dotnet-ef` 全域工具（指定 8.x：`dotnet tool install --global dotnet-ef --version 8.*`，已裝 8.0.33）
- [x] 用 CLI 建立方案與專案（指令見下方）
- [x] `git init`、建立 `.gitignore`（`dotnet new gitignore`）、推上 GitHub（https://github.com/tcar123456/NET-API-DEMO）
- [x] 用 Docker 跑起 SQL Server，用 SSMS 連得進去（容器名稱 `booking-sql`，SQL Server 2022 CU27，`localhost,1433`）

**建立專案指令**（在 Git 根目錄執行）

```powershell
dotnet new sln -n BookingApi
dotnet new webapi -n BookingApi -o src/BookingApi --use-controllers
dotnet new xunit -n BookingApi.Tests -o tests/BookingApi.Tests
dotnet new console -n ConcurrencyTester -o tools/ConcurrencyTester
dotnet sln add src/BookingApi tests/BookingApi.Tests tools/ConcurrencyTester
dotnet add tests/BookingApi.Tests reference src/BookingApi
dotnet new gitignore
```

**完成標準**：`dotnet build` 成功；`dotnet run --project src/BookingApi` 後能看到範本內建的 API。

---

### Day 1：Domain、資料庫與 Migration ✅ 2026-11-02 完成

**任務**
- [x] 刪掉範本內建的 WeatherForecast
- [x] 建立 Entity：Provider、Service、Customer、Booking，以及 BookingStatus enum
- [x] 建立 `AppDbContext`，用 Fluent API 設定欄位長度、型別、外鍵（每個 Entity 一個 `IEntityTypeConfiguration`，外鍵一律 `Restrict`）
- [x] 設定唯一篩選索引 `UX_Bookings_Provider_Start_Active`
- [x] 設定 RowVersion
- [x] 連線字串放在 User Secrets（`dotnet user-secrets`，鍵名 `ConnectionStrings:BookingDb`），不寫進 appsettings.json
- [x] 建立第一個 Migration 並套用（`InitialCreate`）
- [x] 寫 Seed：3 位師傅、3 個服務、5 位客戶（用 `HasData`，種子資料隨 Migration 寫入）

**學習重點**：class 與 record、enum、nullable reference types、EF Core 的 Fluent API、Migration 運作方式

**完成標準**：SSMS 中看得到 4 張表與唯一索引，且索引有 `WHERE [Status] = 1` 條件。

**驗證紀錄**：4 張表與種子資料筆數正確；索引的 filter 為 `([Status]=(1))`；在交易中測試（測完 ROLLBACK）：同時段第二筆 Active 被擋下（錯誤 2601），取消後同時段可再預約。
另外發現：用 `sqlcmd` 手動寫入有篩選索引的表時，必須加 `-I`（`QUOTED_IDENTIFIER ON`），否則會出現錯誤 1934；EF Core 與 SSMS 預設就是 ON，不受影響。

---

### Day 2：分層、API 與錯誤處理 ✅ 2026-11-02 完成

**任務**
- [x] 建立 DTO（Request / Response），不直接回傳 Entity（DTO 用 `record`）
- [x] 建立 Repository 介面與實作（Provider、Service、Customer、Booking）
- [x] 建立 Service 介面與實作，商業規則（營業時間、整點、不可過去）寫在 Service（`ICatalogService`、`IBookingService`）
- [x] 建立 Controller，完成第五節的所有 API（取消與並發處理可先留簡單版本）
- [x] 在 `Program.cs` 用 DI 註冊 Service 與 Repository，並理解為什麼用 Scoped
- [x] 建立自訂例外（NotFound、BusinessRule、Conflict）與全域錯誤處理 Middleware，統一回傳 ProblemDetails
- [x] 確認 Swagger UI 能顯示所有 API（範本內建，不另加套件）
- [x] 所有資料庫操作都用 async，並傳遞 CancellationToken

**學習重點**：介面、依賴注入與三種生命週期、async/await、例外處理、HTTP 狀態碼

**完成標準**：可以用 Swagger UI 走完「查時段 → 建立預約 → 查詢 → 取消」的完整流程；錯誤回應格式一致。

**實作決定**
- 現在時間透過注入 `TimeProvider`（.NET 8 內建）取得，換算為 UTC+8，Day 3 單元測試可換成固定時間
- `startTime` 必須是不帶時區的台灣時間；帶 `Z` 或 `+08:00` 一律回 400，避免時區混用
- 停用的師傅（`IsActive = false`）視同不存在，回 404
- 建立預約保留「先查再寫」，提供一般情況下清楚的 409 訊息；並發時的保證由唯一索引負責，例外轉換留到 Day 3
- 取消預約目前沒有捕捉 `DbUpdateConcurrencyException`（RowVersion 衝突會回 500），留到 Day 3
- 分頁 `pageSize` 上限 100；排序為 `StartTime` 降冪、`Id` 降冪
- `AddProblemDetails()` + `UseStatusCodePages()` 讓不存在的路由也回 ProblemDetails
- `SuppressAsyncSuffixInActionNames = false`：action 名稱保留 `Async` 結尾，`CreatedAtAction` 才找得到路由

**驗證紀錄**：以腳本呼叫全部 7 支 API，共 26 種情境（正常流程與錯誤情況）。建立預約回 201 並帶 `Location`；同時段重複預約回 409；取消後時段重新開放；重複取消回 409；非整點、超出營業時間、過去時間、帶時區的時間都回 400；不存在的師傅、服務、客戶、預約回 404；缺少 `date` 參數回 400；所有錯誤皆為 `application/problem+json`。Swagger 文件列出全部 7 支 API 及其狀態碼。

---

### Day 3：並發控制與測試（本專案最重要的一天）✅ 2026-11-02 完成

**任務**
- [x] 建立預約時，捕捉違反唯一索引的例外（`DbUpdateException` 內層的 `SqlException`，錯誤碼 2601 或 2627），轉成 409 Conflict（在 `BookingRepository.SaveChangesAsync` 轉換）
- [x] 取消預約時使用 RowVersion 做樂觀鎖，捕捉 `DbUpdateConcurrencyException` 轉成 409
- [x] 完成 `tools/ConcurrencyTester`：用 `HttpClient` 同時送出 100 個搶同一時段的請求，統計 201 與 409 的數量（另有 `--scenario cancel`：100 個請求同時取消同一筆預約）
- [x] 執行測試，確認結果是 1 筆成功、99 筆 409，截圖或記錄輸出
- [x] **實驗對照**：暫時拿掉唯一索引、只靠「先查再寫」，再跑一次，觀察是否出現重複預約。記錄結果後恢復索引（腳本在 `scripts/experiments/`）
- [x] 寫 xUnit 單元測試：Service 的商業規則（過去時間、超出營業時間、未對齊整點、找不到師傅）

**學習重點**：為什麼「先查再寫」在並發下不安全、唯一約束、悲觀鎖與樂觀鎖的差異、單元測試與模擬

**完成標準**：並發測試穩定只有 1 筆成功；實驗對照有記錄；單元測試全部通過。

**實作決定**
- 取消預約的樂觀鎖只在伺服器端使用「讀取時的 RowVersion」，用戶端不需傳送版本號。預約唯一可修改的動作是取消，「用戶端看到的是舊資料」的情況已由「已取消就回 409」處理；RowVersion 負責的是兩個請求同時讀到 Active、同時寫入的競態
- 409 訊息分三種，可看出是哪一層擋下：`Provider 1 is already booked at ...`（Service 先查再寫）、`The time slot has already been booked.`（唯一索引）、`The booking was modified by another request...`（RowVersion）
- 單元測試的「現在時間」用自製的 `FixedTimeProvider` 固定，不另外加套件；Repository 用 NSubstitute 6.2.0 模擬

**並發測試結果（2026-11-02，100 個並發請求，API 與 SQL Server 2022 皆在本機）**

| 情境 | 第 1 次 | 第 2 次 | 第 3 次 |
|---|---|---|---|
| 建立預約（有唯一索引） | 201 × 1、409 × 99（74 被先查再寫擋下，25 被唯一索引擋下） | 201 × 1、409 × 99（97 / 2） | 201 × 1、409 × 99（92 / 7） |
| 取消預約（RowVersion） | 200 × 1、409 × 99（70 已取消，29 被 RowVersion 擋下） | 200 × 1、409 × 99（95 / 4） | 200 × 1、409 × 99（97 / 2） |
| **拿掉唯一索引**（只靠先查再寫） | **201 × 56**、409 × 44 | **201 × 61**、409 × 39 | **201 × 68**、409 × 32 |

- 有唯一索引時，每次都有部分請求「通過先查再寫的檢查」，最後由唯一索引擋下：這證明只靠應用程式檢查不安全
- 拿掉唯一索引後，資料庫中同一時段分別出現 56、61、68 筆 Active 預約（以 SQL 直接查詢確認）
- 還原：`day3-2-restore-unique-index.sql` 將 182 筆重複預約軟取消，重建索引（filter `([Status]=(1))`），再跑一次回到 201 × 1、409 × 99
- 預約 Id 會跳號：違反唯一索引的 INSERT 失敗時，IDENTITY 值已被使用、不會退回

**單元測試**：`BookingServiceTests` 共 26 個（含 Theory 的各組資料），全部通過。涵蓋過去時間、營業時間外（9、19、23 點）、非整點、帶時區、第一與最後時段、師傅／服務／客戶不存在、停用師傅、重複時段不寫入、建立內容（EndTime、CreatedAt、Status）、可預約時段排除過去與已預約、取消與重複取消、分頁參數驗證。

---

### Day 4：Docker Compose、README 與收尾 ✅ 2026-11-02 完成

**任務**
- [x] 為 API 寫 Dockerfile（多階段建置；執行時使用 .NET 映像檔內建的非 root 使用者 `app`）
- [x] 寫 `docker-compose.yml`：API + SQL Server，SA 密碼從 `.env` 讀取
- [x] 提供 `.env.example`，確認 `.env` 已被 `.gitignore` 排除
- [x] API 啟動時自動套用 Migration 與 Seed（僅限開發環境）
- [x] 從零開始 `docker compose up`，確認別人照 README 就能跑起來
- [x] 撰寫 README（內容見第七節；效能調校一節標註為尚未進行）

**學習重點**：容器化、環境變數與機密管理

**完成標準**：在一台乾淨的環境 clone 下來，照 README 5 分鐘內可以跑起來並打到 API。

**實作決定**
- Compose 的 SQL Server 對主機開 `14330`，與本機開發用的 `booking-sql`（1433）分開，兩者互不影響
- SQL Server 加上健康檢查，API 以 `depends_on: condition: service_healthy` 等資料庫可連線後才啟動
- Compose 中 API 以 `Development` 執行，因此會自動套用 Migration 並開啟 Swagger UI
- `.env.example` 的密碼留空，未設定時 Compose 直接報錯並提示，避免使用範例密碼
- 資料庫資料放在具名 volume `sqlserver-data`：`docker compose down` 保留資料，`down -v` 才刪除
- `.dockerignore` 排除 `bin/`、`obj/`、`.env`、測試與工具專案，`global.json` 也不會被複製進建置映像檔

**驗證紀錄**：把 Git 會追蹤的檔案（不含 `bin/`、`obj/`、`.env`）複製到暫存資料夾模擬乾淨的 clone，依 `.env.example` 建立 `.env` 後執行 `docker compose up -d --build`：50 秒內完成建置與啟動；API 日誌顯示自動套用 `InitialCreate`；師傅列表、Swagger UI、可預約時段皆正常；容器以 `app` 使用者執行；對容器版本跑並發測試，建立 201 × 1、409 × 99，取消 200 × 1、409 × 99；重啟 API 後資料仍在。測試後以 `down -v --rmi local` 清除。
限制：驗證是在同一台開發機上模擬，基底映像檔可能已有快取，沒有在另一台全新的電腦上測試。

---

### 加分版（有時間再做，建議依序挑 1～2 項）

#### Day 5：MS SQL 效能調校（最推薦，職缺特別強調）✅ 2026-11-02 完成

- [x] 在 `scripts/perf/` 寫 T-SQL 腳本，以集合式寫法（不用迴圈逐筆）塞入約 100 萬筆預約（`01-seed-perf-data.sql`，`GENERATE_SERIES`，31 秒）
  - 注意：要避開唯一索引衝突，例如用較多的師傅數量分散時段，或讓大部分歷史資料狀態為 Cancelled（做法：200 位師傅，每個 (師傅, 時段) 只出現一次）
- [x] 針對「查某客戶近半年預約、依時間排序」的查詢，做三階段比較。每個階段都開啟 `SET STATISTICS IO, TIME ON` 與「包含實際執行計畫」，記錄邏輯讀取次數、耗時、執行計畫截圖：
  1. **無索引**：在實驗腳本中暫時 `DROP INDEX IX_Bookings_CustomerId`（預期看到 Scan）
  2. **單欄外鍵索引**：重建 `IX_Bookings_CustomerId`，恢復 Migration 的原狀（預期看到 Seek + Key Lookup + Sort）
  3. **複合覆蓋索引**：加上 `IX_Bookings_Customer_Start (CustomerId, StartTime) INCLUDE (...)`（預期只有 Seek，沒有 Key Lookup 與 Sort）
  - 預期結果為推測，實際執行計畫取決於資料量與分布，以實測為準（實測三個階段都符合預期）
  - 階段 1 只在實驗用的 T-SQL 腳本中進行，結束後必須確認 `IX_Bookings_CustomerId` 已恢復；正式 schema 的變更（加入 `IX_Bookings_Customer_Start`）要透過新的 Migration
- [x] 把三階段比較與截圖寫進 README，說明 Key Lookup 與覆蓋索引的差異（以 `docs/perf/*.sqlplan` 實際執行計畫檔取代截圖，可用 SSMS 開啟成圖形）

**實作決定**
- 使用獨立的 `BookingPerfDb`（同一個 `booking-sql` 容器），不污染開發用的 `BookingDb`；會修改資料或索引的腳本都會先檢查資料庫名稱
- 查詢用 `sp_executesql` 參數化，與 EF Core 送出的查詢形式相同
- 數據取自 `sys.dm_exec_query_stats`（6 次執行平均）；實際執行計畫取自 `sys.dm_exec_query_plan_stats`（需開啟 `LAST_QUERY_PLAN_STATS`）
- INCLUDE 欄位只放這個查詢用到的：ProviderId、ServiceId、EndTime、Status（Id 是叢集索引鍵，本來就在每個非叢集索引中）

**結果**（100 萬筆，查詢回傳 34 筆）

| 階段 | 執行計畫 | 邏輯讀取 | 平均耗時 | 平均 CPU |
|---|---|---|---|---|
| 1. 無索引 | Clustered Index Scan（平行）→ Sort | 8,484 | 8.4 ms | 62.5 ms |
| 2. 單欄外鍵索引 | Index Seek（100 列）→ Key Lookup × 100 → Sort | 約 310 | 0.21 ms | 0.21 ms |
| 3. 複合覆蓋索引 | Index Seek（反向讀取，Ordered） | 4 | 0.04 ms | 0.04 ms |

- 階段 1 的遺漏索引建議與階段 3 的索引完全相同（EQUALITY: CustomerId、INEQUALITY: StartTime、INCLUDE: ProviderId, ServiceId, EndTime, Status），預估改善 99.4%
- `AddCustomerStartIndex` Migration 由 EF Core 自動產生：新增複合索引，並移除被取代的 `IX_Bookings_CustomerId`；在 100 萬筆上套用耗時 0.9 秒；已套用到 `BookingPerfDb` 與開發用的 `BookingDb`
- 覆蓋索引佔 38.1 MB；同結構的單欄索引 `IX_Bookings_ServiceId` 為 13.6 MB（`IX_Bookings_CustomerId` 移除前未量測大小，以此估計多約 24 MB）
- 套用後單元測試 26 個通過、API 與並發測試（201 × 1、409 × 99）正常

**重現步驟**（`<密碼>` 換成你的 SA 密碼）

1. 在 SSMS 對 `localhost,1433` 執行 `CREATE DATABASE BookingPerfDb;`
2. 只套用第一個 Migration：
   `dotnet ef database update InitialCreate --project src/BookingApi --connection 'Server=localhost,1433;Database=BookingPerfDb;User Id=sa;Password=<密碼>;TrustServerCertificate=True'`
3. 在 BookingPerfDb 依序執行（SSMS 開啟「包含實際執行計畫」）：
   - `01-seed-perf-data.sql`（產生資料）
   - 階段 1：`03-stage1-drop-customer-index.sql` → `02-customer-bookings-query.sql`
   - 階段 2：`04-stage2-restore-customer-index.sql` → `02-customer-bookings-query.sql`
4. 階段 3：用第 2 步的指令，但不指定 `InitialCreate`（套用到最新的 Migration），再執行 `02-customer-bookings-query.sql`

#### Day 6：Dapper 與 T-SQL

- [ ] 新增一支報表 API，例如「某月各師傅的預約數與營收」
- [ ] 用 Dapper + 手寫 T-SQL 實作（GROUP BY、JOIN）
- [ ] README 說明為什麼這裡選 Dapper 而不是 EF Core

#### Day 7：冪等性

- [ ] 建立預約 API 接受 `Idempotency-Key` 標頭
- [ ] 同一個 Key 重送：回傳第一次的結果，不重複建立
- [ ] 同一個 Key 但請求內容不同：回傳 422 或 409
- [ ] 寫測試驗證

#### Day 8：CI 與快取

- [ ] GitHub Actions：push 與 PR 時自動 `dotnet build` 與 `dotnet test`
- [ ] Redis 快取「可預約時段」查詢；建立或取消預約後刪除對應快取

---

## 七、README 必備內容

1. 專案簡介：解決什麼問題、做這個專案的目的
2. 快速開始：`docker compose up` 一行指令
3. API 列表與範例
4. **設計決策與理由**
   - 為什麼用唯一篩選索引防止重複預約，而不是鎖
   - 為什麼取消用 RowVersion 樂觀鎖
   - 為什麼分層、為什麼用介面
   - 為什麼取消是 POST 而不是 DELETE
5. **並發測試結果**（含拿掉索引的對照實驗）
6. 效能調校前後比較（若有做）
7. 已知限制：固定時段長度、未實作身分驗證、時區簡化
8. **如果流量放大一百倍，我會怎麼改**
   - 可預約時段查詢加快取
   - 讀寫分離
   - 建立預約改由訊息佇列排隊處理，削峰
   - 依業務拆分服務（預約、通知、報表）

---

## 八、Git 工作方式

- 分支策略：GitHub Flow，`main` 永遠可執行
- 每個任務開一個分支：`feat/booking-api`、`feat/concurrency`、`chore/docker`
- 透過 Pull Request 合併，合併前自己檢查一次 diff（可請 AI 做第一輪 review）
- Commit 訊息使用 Conventional Commits：`feat:`、`fix:`、`test:`、`docs:`、`chore:`、`refactor:`

---

## 九、面試展示準備

- [ ] 能在 3 分鐘內講完：專案做什麼 → 最核心的並發問題 → 怎麼解 → 怎麼證明
- [ ] 能打開任一檔案解釋每一段程式碼
- [ ] 能回答「如果服務長度不固定，怎麼防止時段重疊」
- [ ] 能回答「這個設計在每日上億筆資料的系統裡哪裡會先撐不住」
