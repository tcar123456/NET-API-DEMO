# 技術組合（Tech Stack）

> 專案：預約系統後端 API（.NET 練習專案）
> 目的：為準進科技「軟體工程師（C# / .NET）」面試準備，展示 C#、OOP、MS SQL、API 設計與並發控制能力。

---

## 一、選型總覽

| 類別 | 選擇 | 對應職缺要求 |
|---|---|---|
| 執行環境 | .NET 8（LTS） | .NET 6 / 8 開發經驗 |
| 語言 | C# | 熟悉 C# 與 OOP |
| Web 框架 | ASP.NET Core Web API（Controller 寫法） | API / service 撰寫經驗 |
| ORM | Entity Framework Core | MS SQL 開發經驗 |
| 輕量查詢 | Dapper（加分項） | T-SQL |
| 資料庫 | SQL Server 2022 Developer（Docker） | MS SQL、schema 設計、效能調校 |
| API 文件 | Swashbuckle 產生 OpenAPI + Swagger UI（.NET 8 範本內建） | — |
| 測試 | xUnit + NSubstitute | — |
| 容器 | Docker / Docker Compose | Docker（加分條件） |
| CI | GitHub Actions（加分項） | CI/CD |
| 快取 | Redis（加分項） | Redis（加分條件） |
| 版控 | Git + GitHub，GitHub Flow | Git |

---

## 二、各項選型理由

### .NET 8，而不是 .NET 10

- 職缺要求為 .NET 6 / 8 開發經驗，使用 .NET 8 與職缺技術棧直接對應。
- 企業既有系統多數仍在 .NET 6 / 8，用 .NET 8 練習的寫法與範本結構最接近實際工作環境。
- 已知限制：.NET 8 的官方支援於 2026-11-10 結束，之後的 LTS 版本是 .NET 10（支援至 2028-11）。小型 Web API 在兩者的寫法幾乎相同，升級成本低。
- 面試時可延伸提問：「團隊是否有從 .NET 8 升級到 .NET 10 的計畫？」

### Controller 寫法，而不是 Minimal API

- 企業既有系統（特別是從 .NET Framework 升級上來的）大多是 Controller 架構。
- Controller 較能展示 OOP、分層與依賴注入。

### Swagger UI，而不是 Scalar

- .NET 8 的 webapi 範本已內建 Swashbuckle 與 Swagger UI，不需要另外加套件。
- .NET 5 到 .NET 8 的 webapi 範本都預設使用 Swashbuckle，既有系統中很常見。

### Entity Framework Core + Dapper 並用

- 一般 CRUD 用 EF Core：開發快、有 Migration 管理 schema。
- 報表或需要精準控制 SQL 的查詢用 Dapper + 手寫 T-SQL：效能可預期、能展示 T-SQL 能力。
- 面試時能說明「什麼情況用哪一個」，比只會其中一種更有說服力。

### SQL Server 以 Docker 執行

- 跟職缺使用的 MS SQL 一致。
- 透過 Docker Compose，看專案的人一個指令就能把 API 與資料庫一起跑起來。
- 替代方案：Windows 上的 Visual Studio 內建 SQL Server LocalDB，開發階段可先用 LocalDB，最後再補 Docker Compose。

### 單一 API 專案內分層，而不是拆成多個專案

- 以資料夾分層：Controllers → Services → Repositories → Data。
- 3～4 天的練習專案，拆成多個專案（Clean Architecture）會花太多時間在結構上。
- README 中說明「若專案變大，會如何拆分」，作為面試討論點。

### NSubstitute

- 用來在單元測試中模擬 Repository 介面，只測 Service 的商業邏輯。
- 語法比 Moq 直覺，適合初學。

---

## 三、套件清單

### API 專案（src/BookingApi）

| 套件 | 用途 | 階段 |
|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | EF Core 連 SQL Server | 基本版 |
| Microsoft.EntityFrameworkCore.Design | Migration 設計時工具 | 基本版 |
| Swashbuckle.AspNetCore | 產生 OpenAPI 文件與 Swagger UI（.NET 8 範本內建） | 基本版 |
| Dapper | 輕量查詢 | 加分版 |
| Microsoft.Data.SqlClient | Dapper 連線用 | 加分版 |
| StackExchange.Redis | Redis 快取 | 加分版 |

### 測試專案（tests/BookingApi.Tests）

| 套件 | 用途 |
|---|---|
| xunit | 測試框架（範本內建） |
| NSubstitute | 模擬相依物件 |

### 全域工具

| 工具 | 安裝方式 | 用途 |
|---|---|---|
| dotnet-ef | `dotnet tool install --global dotnet-ef --version 8.*` | 建立與套用 Migration |

> 套件版本以 8.x 的最新穩定版為準，EF Core 的主版本號需與 .NET 版本一致（.NET 8 → EF Core 8）。
> 不指定版本時，NuGet 會安裝最新主版本（例如 EF Core 10），它需要 .NET 10 執行環境，無法用在 .NET 8 專案。

---

## 四、開發環境

| 項目 | 建議 |
|---|---|
| 作業系統 | Windows |
| IDE | Visual Studio 2022（17.8 以上支援 .NET 8），或 VS Code + C# Dev Kit，或 JetBrains Rider |
| .NET SDK | .NET 8 SDK（用 `dotnet --list-sdks` 確認有 8.0.x） |
| 資料庫工具 | SQL Server Management Studio（SSMS），用來看執行計畫 |
| 容器 | Docker Desktop（需啟用 WSL 2） |
| API 測試 | Swagger UI、或 VS 內建的 `.http` 檔 |

---

## 五、刻意不使用的技術

| 技術 | 不使用的原因 |
|---|---|
| Minimal API | 見上方「Controller 寫法」說明 |
| MediatR / CQRS | 對練習專案過度設計，會模糊重點 |
| AutoMapper | 手寫對應較透明，也能練習 C# 語法 |
| Microservices | 本專案是單體；在 README 中討論「如何拆分」即可 |
| 身分驗證（JWT） | 不是本專案重點；README 中標註為未來擴充 |
