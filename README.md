# 預約系統後端 API

[![CI](https://github.com/tcar123456/NET-API-DEMO/actions/workflows/ci.yml/badge.svg)](https://github.com/tcar123456/NET-API-DEMO/actions/workflows/ci.yml)

美容／按摩店家的預約系統後端，使用 **ASP.NET Core 8 Web API + EF Core 8 + SQL Server 2022**。

這是一個 C# / .NET 的練習與展示專案，功能刻意精簡，重點放在：

1. **並發控制**：100 個請求同時搶同一個時段，只有 1 筆成功，並以實驗證明
2. **分層與依賴注入**：Controller → Service → Repository，透過介面溝通，可單元測試
3. **MS SQL 設計**：唯一篩選索引、樂觀鎖（rowversion）、Migration 管理 schema
4. **一個指令跑起來**：Docker Compose 同時啟動 API、資料庫與 Redis

---

## 快速開始（Docker）

需要：Docker Desktop

```powershell
git clone https://github.com/tcar123456/NET-API-DEMO.git
cd NET-API-DEMO

# 建立 .env，並填入 MSSQL_SA_PASSWORD（規則見檔案內說明）
Copy-Item .env.example .env

docker compose up -d --build
```

啟動後（第一次需要下載映像檔，約數分鐘）：

| 項目 | 位址 |
|---|---|
| Swagger UI | http://localhost:8080/swagger |
| API | http://localhost:8080/api/... |
| SQL Server（SSMS） | `localhost,14330`，帳號 `sa`，密碼為 `.env` 中的設定，勾選「信任伺服器憑證」 |
| Redis | 只在 Compose 內部網路使用，不對主機開放 |

API 在開發環境啟動時會自動建立資料庫、套用 Migration，並寫入種子資料（3 位師傅、3 個服務、5 位客戶）。

```powershell
docker compose down      # 停止，保留資料
docker compose down -v   # 停止並刪除資料庫資料
```

---

## 本機開發（不使用 Docker 執行 API）

需要：.NET 8 SDK、一個 SQL Server（例如用 Docker 單獨啟動）

```powershell
# 連線字串存在 User Secrets（不在專案資料夾內，不會進版控）
dotnet user-secrets set 'ConnectionStrings:BookingDb' 'Server=localhost,1433;Database=BookingDb;User Id=sa;Password=<你的密碼>;TrustServerCertificate=True' --project src/BookingApi

dotnet run --project src/BookingApi     # http://localhost:5120/swagger
dotnet test                             # 單元測試
```

沒有設定 Redis 時，API 改用記憶體快取，其他行為相同。若要在本機使用 Redis：

```powershell
docker run -d --name booking-redis -p 6379:6379 redis:7-alpine
dotnet user-secrets set 'ConnectionStrings:Redis' 'localhost:6379' --project src/BookingApi
```

每次 push 到 `main` 或開 Pull Request，GitHub Actions 會自動建置、執行單元測試，並確認 Dockerfile 可以建置（`.github/workflows/ci.yml`）。

---

## API

| 方法 | 路徑 | 說明 | 成功 | 錯誤 |
|---|---|---|---|---|
| GET | `/api/providers` | 師傅列表 | 200 | |
| GET | `/api/services` | 服務列表 | 200 | |
| GET | `/api/providers/{id}/available-slots?date=2026-11-20` | 某師傅某天的可預約時段 | 200 | 400、404 |
| POST | `/api/bookings` | 建立預約（可帶 `Idempotency-Key` 標頭） | 201 + `Location` | 400、404、409、422 |
| GET | `/api/bookings/{id}` | 查單筆預約 | 200 | 404 |
| POST | `/api/bookings/{id}/cancel` | 取消預約 | 200 | 404、409 |
| GET | `/api/customers/{id}/bookings?page=1&pageSize=20` | 客戶預約紀錄（分頁） | 200 | 400、404 |
| GET | `/api/reports/monthly-revenue?year=2026&month=11` | 某月各師傅的預約數、取消數與營收（Dapper） | 200 | 400 |

**建立預約**

```http
POST /api/bookings
Content-Type: application/json

{
  "providerId": 1,
  "serviceId": 2,
  "customerId": 3,
  "startTime": "2026-11-20T14:00:00"
}
```

**錯誤回應**一律是 [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) ProblemDetails（`application/problem+json`）：

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "The time slot has already been booked.",
  "instance": "/api/bookings",
  "traceId": "00-..."
}
```

`src/BookingApi/BookingApi.http` 有全部 API 的範例請求，可在 Visual Studio 或 VS Code 直接送出。

### 業務規則

| 規則 | 內容 |
|---|---|
| 營業時間 | 每天 10:00～19:00，固定 60 分鐘一格，開始時間必須是整點（10:00～18:00） |
| 時區 | 一律為台灣時間（UTC+8），存為 `datetime2`；`startTime` 不可帶時區（`Z`、`+08:00` 回 400） |
| 預約限制 | 不能預約過去；不能超出營業時間；同一師傅同一時段只能有一筆有效預約 |
| 取消 | 軟取消：狀態改為 Cancelled，保留資料；取消後該時段可再被預約 |

---

## 設計決策與理由

### 1. 用「唯一篩選索引」防止重複預約，而不是鎖

```sql
CREATE UNIQUE INDEX UX_Bookings_Provider_Start_Active
    ON Bookings (ProviderId, StartTime)
    WHERE Status = 1;   -- 只限制有效預約，已取消的不佔用時段
```

- **「先查再寫」在並發下不安全**：兩個請求可能同時查到「沒人預約」，然後都寫入。這不是理論，下方實驗實際測到每次都有請求通過應用程式的檢查
- **由資料庫保證唯一**：不論有幾台 API 伺服器、幾個請求同時進來，資料庫都只會讓一筆成功，其餘收到錯誤 2601，API 轉成 409
- **為什麼不用悲觀鎖**（例如 `SELECT ... WITH (UPDLOCK, HOLDLOCK)` 或 Serializable 交易）：鎖需要明確的交易範圍，持有期間會讓其他請求排隊等待，寫錯容易造成死結；唯一索引不需要額外的交易設計，衝突時立即失敗
- **篩選條件 `WHERE Status = 1`**：讓取消後的時段可以再被預約，同時保留取消紀錄

Service 仍保留「先查再寫」，目的是在一般情況下回傳清楚的錯誤訊息；**正確性由索引保證**。

### 2. 取消預約用 RowVersion 樂觀鎖

`Bookings.RowVersion`（SQL Server `rowversion`）在每次更新時自動改變。EF Core 產生的 UPDATE 會帶上讀取時的版本：

```sql
UPDATE Bookings SET Status = 2, ... WHERE Id = @id AND RowVersion = @originalVersion
```

兩個請求同時讀到「有效」並同時取消時，第二個 UPDATE 影響 0 筆，EF Core 拋出 `DbUpdateConcurrencyException`，API 轉成 409。

樂觀鎖假設衝突少見，不在讀取時上鎖，只在寫入時檢查；適合「讀多寫少、衝突時讓使用者重試即可」的情境。

### 3. 分層與介面

| 層 | 職責 |
|---|---|
| `Controllers/` | 只處理 HTTP：接收 DTO、呼叫 Service、回傳狀態碼 |
| `Services/` | 商業規則：營業時間、整點、不可預約過去、重複預約 |
| `Repositories/` | 資料存取，唯一接觸 EF Core 的地方 |
| `Middleware/` | 把例外統一轉成 ProblemDetails |

- 每一層只依賴下一層的**介面**：Service 的單元測試可以用 NSubstitute 替換 Repository，不需要資料庫
- Repository 與 Service 註冊為 **Scoped**：它們依賴 `AppDbContext`，而 DbContext 會追蹤變更且非執行緒安全，必須每個請求一份
- 「現在時間」透過注入 `TimeProvider` 取得，測試時可固定時間
- API 不直接回傳 Entity，一律轉成 DTO（`record`），避免暴露內部欄位與 schema

### 4. 取消用 `POST /cancel`，而不是 `DELETE`

取消是**狀態變更**，資料會保留；用 `DELETE` 會讓呼叫方以為資料被刪除。重複取消回 409，而不是當作成功。

### 5. 報表用 Dapper + 手寫 T-SQL，其餘用 EF Core

`GET /api/reports/monthly-revenue` 由 `ReportRepository` 以 Dapper 執行手寫的 T-SQL：

```sql
SELECT p.Id AS ProviderId, p.Name AS ProviderName,
       COUNT(CASE WHEN b.Status = @Active THEN 1 END)    AS BookingCount,
       COUNT(CASE WHEN b.Status = @Cancelled THEN 1 END) AS CancelledCount,
       COALESCE(SUM(CASE WHEN b.Status = @Active THEN s.Price END), 0) AS Revenue
FROM dbo.Providers AS p
LEFT JOIN dbo.Bookings AS b
    ON b.ProviderId = p.Id AND b.StartTime >= @From AND b.StartTime < @To
LEFT JOIN dbo.Services AS s ON s.Id = b.ServiceId
WHERE p.IsActive = 1 OR b.Id IS NOT NULL
GROUP BY p.Id, p.Name
ORDER BY Revenue DESC, p.Id;
```

**為什麼這裡選 Dapper**

| | EF Core（預約的增刪改查） | Dapper（報表） |
|---|---|---|
| 適合 | 以 Entity 為單位讀寫、需要變更追蹤與 Migration | 唯讀的彙總查詢，結果是扁平的統計列 |
| SQL | 由 LINQ 產生，複雜的 GROUP BY 不易預測最後的 SQL | 自己寫，送到資料庫的就是這段，可直接貼到 SSMS 看執行計畫 |
| 成本 | 有變更追蹤與 LINQ 翻譯的開銷 | 只做「執行 SQL → 對應到物件」，額外開銷很小 |
| 風險 | 欄位改名時編譯期就會發現 | SQL 是字串，改名要靠測試才發現 |

**寫法上的重點**

- `LEFT JOIN` 並把期間條件放在 `ON`：當月沒有預約的師傅也會出現（數量 0、營收 0）。若把期間條件放在 `WHERE`，這些師傅會被過濾掉
- 條件彙總 `COUNT(CASE WHEN ...)`：一次掃描同時算出有效數與取消數
- 期間用半開區間 `[當月 1 日, 下月 1 日)`，不需要處理每月天數與 23:59:59 的邊界
- Dapper 預設把 `DateTime` 當成 SQL 的 `datetime`；欄位是 `datetime2`，因此以 `DbType.DateTime2` 明確指定參數型別
- 連線由 `ISqlConnectionFactory` 建立（Singleton，只保存連線字串），每次查詢 `using` 一個新連線，由 SqlClient 連線池重複使用實體連線

**在 100 萬筆資料上**（`BookingPerfDb`，查 2026-10，200 位師傅）：邏輯讀取 4,951、平均 42 ms。因為沒有以 `StartTime` 開頭的索引，SQL Server 改為掃描最窄、且包含所需欄位的 `IX_Bookings_Customer_Start`。SQL Server 建議加上 `(ProviderId, StartTime) INCLUDE (ServiceId, Status)`（預估改善 89.7%）；報表通常每月執行少數幾次，目前**沒有**為它新增索引，以免增加每次建立預約的寫入成本。

**已知限制**：營收以目前的 `Services.Price` 計算；若服務調價，過去月份的營收也會跟著改變。正式系統應在預約時把價格寫入預約資料。

### 6. 建立預約的冪等性（Idempotency-Key）

網路逾時後，用戶端不知道第一次請求是否成功，只能重送。帶上 `Idempotency-Key` 標頭，重送就不會重複建立預約：

```http
POST /api/bookings
Idempotency-Key: 7eb1cd17-dc1d-4c9b-b5d4-e264e9a60a4d
```

| 情況 | 回應 |
|---|---|
| 新的 Key | 正常處理，記錄結果 |
| 同一個 Key、相同內容，第一次已完成 | **201，回傳第一次的結果**（同一筆預約），加上 `Idempotent-Replayed: true` 標頭 |
| 同一個 Key、相同內容，第一次還在處理 | 409，請稍後重試 |
| 同一個 Key、**不同內容** | 422 |
| 第一次處理失敗（例如時段已被預約） | 釋放 Key，重送會再處理一次（失敗沒有留下任何資料，重新處理是安全的） |
| 沒帶 Key | 與原本行為相同 |

**怎麼保證同一個 Key 同時送來多次也只建立一筆**

1. 先把 Key 寫進 `IdempotencyRecords`（Key 是主鍵）：同時到達的請求只有一個能寫入，其餘收到主鍵衝突（2627）
2. 搶到的請求建立預約，再把回應內容存回這一筆紀錄
3. 沒搶到的請求讀取紀錄：內容雜湊不同 → 422；尚未完成 → 409；已完成 → 回放

與 Day 3 的唯一索引是同一個思路：**由資料庫的唯一約束決定誰先到**，而不是「先查再寫」。

**實作細節**

- 請求內容以 SHA-256 雜湊比對，雜湊的是反序列化後的 DTO，因此 JSON 的空白、欄位順序不同但內容相同，仍視為同一個請求
- 主鍵衝突後，失敗的實體仍被 `DbContext` 追蹤，必須設為 `Detached`，否則之後建立預約的 `SaveChanges` 會再次嘗試寫入它
- 完成與釋放用 `ExecuteUpdateAsync` / `ExecuteDeleteAsync` 直接送出 SQL，不需要先讀取；並使用 `CancellationToken.None`，用戶端中斷連線時仍會完成記錄或清理

### 7. 可預約時段快取（Redis）

`GET /api/providers/{id}/available-slots` 是最常被呼叫的查詢，加上 cache-aside 快取：

1. 先查快取 `booking:slots:{師傅}:{日期}`，有就直接用
2. 沒有就查資料庫，再寫入快取（存活 5 分鐘）
3. 建立或取消預約成功後，刪除該師傅、該日期的快取

**快取的是「已被預約的開始時間」，不是「可預約時段」**：可預約時段會隨現在時間改變（今天已過去的時段要排除），快取原始資料、每次再計算，結果才不會過時。

**Redis 故障時不影響 API**

| 情況 | 行為 |
|---|---|
| Redis 正常 | 查快取，約幾毫秒 |
| Redis 停機 | 改查資料庫並記錄警告；每次快取操作最多等 500 ms |
| 停機後 30 秒內 | 斷路器開啟，直接略過快取，不再等待逾時 |
| 30 秒後 | 再試一次 Redis，恢復後自動重新使用快取 |

實測（Compose，停掉 Redis 容器）：未加斷路器前，每次查詢約 11 秒、建立預約約 6 秒；加上斷路器與 500 ms 上限後，第一次查詢 0.5 秒，之後 3～4 ms，建立預約 0.2 秒。

**為什麼用 `IDistributedCache`，而不是直接用 StackExchange.Redis**

- 程式只依賴 ASP.NET Core 內建的介面：有設定 Redis 連線字串時用 Redis，沒有時用記憶體快取，本機開發與單元測試都不需要 Redis
- 預約的正確性不依賴快取：重複預約由資料庫唯一索引保證，快取只影響「查詢顯示」

**已知限制**：讀寫交錯時（讀取請求查到舊資料、寫入請求刪除快取後，讀取請求才把舊資料寫回快取），快取可能短暫顯示已被預約的時段為可預約；最多維持 5 分鐘（快取存活時間），且送出預約時仍會被資料庫擋下。

### 8. 其他

- 外鍵一律 `Restrict`：預約是歷史紀錄，不會因為刪除師傅或客戶而被連帶刪除
- 欄位型別與長度全部用 Fluent API 設定（`Data/Configurations/`），Entity 類別保持乾淨
- 自動套用 Migration 只在開發環境；正式環境應在部署流程中單獨執行，避免多台機器同時改 schema

---

## 並發測試結果

`tools/ConcurrencyTester` 會建立 100 個請求，全部準備好後同時放行，統計各狀態碼與錯誤訊息：

```powershell
dotnet run --project tools/ConcurrencyTester                                   # 100 個請求搶同一時段
dotnet run --project tools/ConcurrencyTester -- --scenario cancel              # 100 個請求取消同一筆預約
dotnet run --project tools/ConcurrencyTester -- --scenario idempotency         # 100 個帶同一個 Idempotency-Key 的相同請求
dotnet run --project tools/ConcurrencyTester -- --base-url http://localhost:8080   # 對 Docker 版本測試
```

409 的訊息可以看出是哪一層擋下的：

| 訊息 | 擋下的地方 |
|---|---|
| `Provider 1 is already booked at ...` | Service 的「先查再寫」 |
| `The time slot has already been booked.` | 資料庫唯一索引 |
| `The booking was modified by another request...` | RowVersion |

**結果**（100 個並發請求；API 與 SQL Server 2022 皆在同一台開發機）

| 情境 | 第 1 次 | 第 2 次 | 第 3 次 |
|---|---|---|---|
| 建立預約 | 201 × 1、409 × 99（25 個由唯一索引擋下） | 201 × 1、409 × 99（2） | 201 × 1、409 × 99（7） |
| 取消預約 | 200 × 1、409 × 99（29 個由 RowVersion 擋下） | 200 × 1、409 × 99（4） | 200 × 1、409 × 99（2） |
| **對照：拿掉唯一索引** | **201 × 56** | **201 × 61** | **201 × 68** |
| 同一個 Idempotency-Key | 只建立 1 筆；409（處理中）× 99 | 只建立 1 筆；409 × 99 | 只建立 1 筆；201 回放 × 2、409 × 97 |

- 唯一索引存在時，**每一次**都有請求通過了應用程式的檢查，最後才被索引擋下
- 拿掉索引（`scripts/experiments/day3-1-drop-unique-index.sql`）後，同一師傅、同一時段在資料庫中出現 56～68 筆有效預約；以 `day3-2-restore-unique-index.sql` 清理並還原後，結果回到 1 筆成功
- Docker Compose 版本（API 在容器內）同樣測得 201 × 1、409 × 99 與 200 × 1、409 × 99

---

## 效能調校

**情境**：查某位客戶近半年的預約，依時間由新到舊排序（`scripts/perf/02-customer-bookings-query.sql`）

```sql
SELECT Id, ProviderId, ServiceId, StartTime, EndTime, Status
FROM dbo.Bookings
WHERE CustomerId = @CustomerId AND StartTime >= @From AND StartTime < @To
ORDER BY StartTime DESC;
```

**資料**：獨立的 `BookingPerfDb`，以集合式 T-SQL（`GENERATE_SERIES`）產生 **100 萬筆**預約、200 位師傅、10,000 位客戶；每位客戶 100 筆，查詢結果 34 筆

**三階段比較**（SQL Server 2022；數值為 6 次執行的平均，來自 `sys.dm_exec_query_stats`）

| 階段 | 可用的索引 | 執行計畫 | 邏輯讀取 | 平均耗時 | 平均 CPU |
|---|---|---|---|---|---|
| 1. 無索引 | 無 | Clustered Index **Scan**（平行 12 條執行緒）→ Sort | **8,484** | 8.4 ms | 62.5 ms |
| 2. 單欄外鍵索引 | `IX_Bookings_CustomerId (CustomerId)` | Index **Seek**（100 列）→ **Key Lookup × 100** → Sort | **約 310** | 0.21 ms | 0.21 ms |
| 3. 複合覆蓋索引 | `IX_Bookings_Customer_Start (CustomerId, StartTime) INCLUDE (ProviderId, ServiceId, EndTime, Status)` | 只有一個 Index **Seek**（反向讀取） | **4** | 0.04 ms | 0.04 ms |

**怎麼解讀**

- **階段 1**：沒有能用的索引，只能把 100 萬筆全部掃過一遍，再排序。SQL Server 的「遺漏索引建議」提出的正好是階段 3 的索引，預估改善 99.4%
- **階段 2**：能直接找到這位客戶的 100 筆，但索引裡只有 `CustomerId`，其他欄位（包括用來過濾的 `StartTime`）都要回到叢集索引逐筆取得：**Key Lookup 執行了 100 次**，過濾後剩 34 筆，最後還要排序
- **階段 3**：索引先依 `CustomerId`、再依 `StartTime` 排序，可以直接定位到「這位客戶、這段期間」的範圍；`ORDER BY StartTime DESC` 以**反向讀取**索引完成，不需要 Sort；需要的欄位都在索引裡（INCLUDE），不需要 Key Lookup

**代價**

- 空間：覆蓋索引 38.1 MB；同樣結構的單欄索引（`IX_Bookings_ServiceId`）為 13.6 MB，因此估計比單欄索引多約 24 MB（以 100 萬筆計）
- 寫入：每次新增或修改預約都要多維護索引中的 INCLUDE 欄位
- 只 INCLUDE 這個查詢真正用到的欄位；如果查詢之後加欄位、又沒有更新索引，就會再出現 Key Lookup

**變更方式**：索引透過新的 Migration `AddCustomerStartIndex` 加入。新索引的第一個欄位就是 `CustomerId`，已能支援外鍵查詢，EF Core 因此在同一個 Migration 中自動移除重複的 `IX_Bookings_CustomerId`

**執行計畫檔**：`docs/perf/stage1-no-index.sqlplan`、`stage2-single-column-index.sqlplan`、`stage3-covering-index.sqlplan`（實際執行計畫，可直接用 SSMS 開啟成圖形）

重現步驟見 [scripts/perf/README.md](scripts/perf/README.md)。

---

## 已知限制

- **固定時段長度**：每個服務都佔 60 分鐘。若服務長度不一（30／60／90 分鐘），`(ProviderId, StartTime)` 的唯一索引無法防止「時段重疊」。可行做法是拆出「每 30 分鐘一格」的時段佔用表，每格各自有唯一索引，一筆預約在同一個交易中寫入多格
- **未實作身分驗證與授權**
- **時區簡化**：只支援台灣時間，且不帶時區資訊；跨時區營運需改存 UTC 或 `datetimeoffset`
- **分頁使用 OFFSET**：頁數很深時效能下降，資料量大時可改用 keyset（游標）分頁
- **冪等性**
  - Key 不分用戶端（沒有身分驗證），不同用戶端使用相同的 Key 會互相影響；正式系統應以「使用者 + Key」為範圍
  - 紀錄不會過期；正式系統應定期清除（例如保留 24 小時）
  - 建立預約與記錄結果是兩次寫入：若程序剛好在兩者之間中止，該 Key 會一直停在「處理中」（409）。可用同一個交易包住，或讓處理中的紀錄在一段時間後失效

---

## 如果流量放大一百倍

1. **快取**：可預約時段已用 Redis 快取；流量更大時可把快取延伸到師傅、服務列表，並改用 Redis 叢集
2. **讀寫分離**：查詢走唯讀副本，寫入走主資料庫
3. **建立預約改由訊息佇列排隊處理**，削峰填谷，API 先回「受理中」
4. **依業務拆分服務**：預約、通知、報表各自獨立部署與擴展
5. 唯一索引的保證在多台 API 伺服器下依然成立；若資料庫本身成為瓶頸，可依店家或師傅分片（sharding），同一師傅的資料落在同一個分片，唯一索引仍然有效

---

## 專案結構

```
├── docker-compose.yml
├── .env.example
├── .github/workflows/ci.yml GitHub Actions：建置、測試、Docker 映像檔
├── src/BookingApi/
│   ├── Controllers/        HTTP 端點
│   ├── Services/           商業規則
│   ├── Repositories/       資料存取（EF Core；報表用 Dapper）
│   ├── Caching/            可預約時段快取（IDistributedCache，Redis 或記憶體）
│   ├── Domain/             Entity、Enum、業務常數
│   ├── Dtos/               Request / Response
│   ├── Data/               AppDbContext、Fluent API 設定、種子資料
│   ├── Exceptions/         NotFound / BusinessRule / Conflict
│   ├── Middleware/         全域例外處理 → ProblemDetails
│   ├── Migrations/
│   └── Dockerfile
├── tests/BookingApi.Tests/ xUnit + NSubstitute 單元測試
├── tools/ConcurrencyTester/ 並發測試工具
├── scripts/
│   ├── experiments/        唯一索引對照實驗的 SQL 腳本
│   └── perf/               效能調校：產生 100 萬筆資料、量測查詢、切換索引
└── docs/perf/              三階段的實際執行計畫（.sqlplan）
```

## 技術選型

| 類別 | 選擇 | 理由 |
|---|---|---|
| 執行環境 | .NET 8（LTS） | 與多數既有企業系統的版本一致；小型 Web API 在 .NET 8 與 .NET 10 的寫法幾乎相同，升級成本低 |
| Web 框架 | ASP.NET Core Controller | 企業既有系統多為 Controller 架構，也較能呈現分層與依賴注入 |
| ORM | EF Core 8 | 一般 CRUD 開發快，並以 Migration 管理 schema |
| 輕量查詢 | Dapper | 報表等唯讀彙總查詢，手寫 T-SQL、結果可預期 |
| 資料庫 | SQL Server 2022（Docker） | 唯一篩選索引、rowversion、執行計畫分析 |
| API 文件 | Swagger UI（Swashbuckle，.NET 8 範本內建） | 不需要額外套件 |
| 測試 | xUnit + NSubstitute | 以假的 Repository 只測 Service 的商業規則 |
| 快取 | Redis（`IDistributedCache`） | 加速最常用的時段查詢；沒有 Redis 時自動改用記憶體快取 |
| 容器 | Docker Compose | 一個指令同時啟動 API、資料庫與 Redis |
| CI | GitHub Actions | push 與 PR 時自動建置、測試、建置 Docker 映像檔 |

**刻意不使用**

| 技術 | 原因 |
|---|---|
| Minimal API | 見上方 Controller 的理由 |
| MediatR / CQRS | 對這個規模的專案是過度設計 |
| AutoMapper | 手寫 Entity → DTO 的對應較透明 |
| 拆成多個專案（Clean Architecture） | 單一專案內以資料夾分層即可；專案變大時再依層拆分 |
| 身分驗證（JWT） | 不是本專案的重點，列為已知限制 |
