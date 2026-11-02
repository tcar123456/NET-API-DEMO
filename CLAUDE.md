# CLAUDE.md

## 專案概要

預約系統後端 API（美容／按摩店家），ASP.NET Core Web API + EF Core + SQL Server。
這是 C# / .NET 的**學習與面試展示專案**，詳細規劃見 `PLAN.md`，技術選型見 `TECH\_STACK.md`。

## 關於我（開發者）

* 熟悉 TypeScript / JavaScript、Dart（Flutter）、Next.js、Firebase、Supabase
* **C#、.NET、MS SQL 是新學的**，目標是在面試時能解釋專案裡的每一行程式碼
* 解釋 C# 概念時，請盡量用 TypeScript / JavaScript 的對應概念做比較

## 常用指令

```powershell
# 建置與執行
dotnet build
dotnet run --project src/BookingApi

# 測試
dotnet test

# Migration（在 Git 根目錄執行）
dotnet ef migrations add <名稱> --project src/BookingApi
dotnet ef database update --project src/BookingApi

# 容器
docker compose up -d
docker compose down

# 並發測試
dotnet run --project tools/ConcurrencyTester
```

## 架構與分層

* `Controllers/`：只處理 HTTP（接收 DTO、呼叫 Service、回傳結果），不放商業邏輯
* `Services/`：商業規則（營業時間、整點、不可預約過去），透過介面注入
* `Repositories/`：資料存取，透過介面注入
* `Domain/`：Entity 與 Enum
* `Dtos/`：Request / Response，**不直接回傳 Entity**
* `Data/`：`AppDbContext`、Fluent API 設定、Seed
* `Middleware/`：全域例外處理，統一回傳 ProblemDetails

## 程式碼規範

* 啟用 nullable reference types，不要用 `!` 硬壓警告，除非說明理由
* 使用 file-scoped namespace
* 命名：類別、方法、屬性用 PascalCase；區域變數、參數用 camelCase；私有欄位用 `\_camelCase`；介面以 `I` 開頭
* 非同步方法名稱以 `Async` 結尾，並接收與傳遞 `CancellationToken`
* **禁止** `.Result`、`.Wait()`、`async void`（事件處理器除外）
* Service 與 Repository 以 Scoped 註冊
* 設定欄位長度與型別一律用 Fluent API，不用 Data Annotation
* 程式碼識別字用英文；註解可用繁體中文，只在「為什麼這樣做」不明顯時才寫

## 業務規則（摘要，完整版見 PLAN.md 第三節）

* 營業時間 10:00～19:00，固定 60 分鐘一格，開始時間必須是整點
* 時間一律以台灣時間（UTC+8）存為 `datetime2`
* 同一位師傅、同一開始時間，只能有一筆 Status = Active 的預約（由資料庫唯一篩選索引保證）
* 取消為軟取消（Status 改為 Cancelled），不刪除資料

## 禁止事項

* 不要把連線字串、SA 密碼寫進 `appsettings.json` 或任何會進版控的檔案；開發用 User Secrets，容器用 `.env`
* 不要修改已經套用過的 Migration，要改 schema 請新增 Migration
* 不要在沒有詢問的情況下新增 NuGet 套件
* 不要為了讓測試通過而修改測試的預期結果，除非先說明理由並得到同意

## 進度與文件更新

* 每完成一項進度，主動更新 `PLAN.md`：勾選完成的任務，部分完成或卡住的項目註明原因
* 技術選型或套件有變更時，同步更新 `TECH_STACK.md`；協作規則有變更時，同步更新本檔
* 文件更新不需要先詢問，但要在回應中說明改了哪些地方

## Git

* GitHub Flow：每個任務開分支（`feat/...`、`fix/...`、`chore/...`），透過 PR 合併到 `main`
* Commit 使用 Conventional Commits：`feat:`、`fix:`、`test:`、`docs:`、`chore:`、`refactor:`
* 不要自行 commit 或 push，除非我要求

## 回應語言

一律使用繁體中文。

