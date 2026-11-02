# 效能調校腳本

量測「某客戶近半年預約，依時間排序」在三種索引狀態下的差異。結果與解讀見根目錄 [README.md](../../README.md) 的「效能調校」一節。

所有步驟都在獨立的 `BookingPerfDb` 進行，不影響開發用的 `BookingDb`。會修改資料或索引的腳本會先檢查資料庫名稱，在其他資料庫執行會直接中止。

| 檔案 | 用途 |
|---|---|
| `01-seed-perf-data.sql` | 以集合式 T-SQL（`GENERATE_SERIES`）產生 200 位師傅、10,000 位客戶、100 萬筆預約 |
| `02-customer-bookings-query.sql` | 參數化的量測查詢，開啟 `STATISTICS IO, TIME` |
| `03-stage1-drop-customer-index.sql` | 階段 1：拿掉 `IX_Bookings_CustomerId` |
| `04-stage2-restore-customer-index.sql` | 階段 2：還原 `IX_Bookings_CustomerId` |

## 重現步驟

`<密碼>` 換成你的 SA 密碼；以下假設 SQL Server 在 `localhost,1433`。

1. 在 SSMS 建立資料庫：

   ```sql
   CREATE DATABASE BookingPerfDb;
   ```

2. 只套用第一個 Migration（還沒有複合索引的狀態）：

   ```powershell
   dotnet ef database update InitialCreate --project src/BookingApi --connection 'Server=localhost,1433;Database=BookingPerfDb;User Id=sa;Password=<密碼>;TrustServerCertificate=True'
   ```

3. 在 SSMS 選擇 `BookingPerfDb`，開啟「包含實際執行計畫」（Ctrl+M），依序執行：
   - `01-seed-perf-data.sql`：產生資料（約 30 秒）
   - **階段 1**：`03-stage1-drop-customer-index.sql` → `02-customer-bookings-query.sql`
   - **階段 2**：`04-stage2-restore-customer-index.sql` → `02-customer-bookings-query.sql`

4. **階段 3**：套用到最新的 Migration（`AddCustomerStartIndex` 會加入複合覆蓋索引），再執行一次量測查詢：

   ```powershell
   dotnet ef database update --project src/BookingApi --connection 'Server=localhost,1433;Database=BookingPerfDb;User Id=sa;Password=<密碼>;TrustServerCertificate=True'
   ```

   接著執行 `02-customer-bookings-query.sql`。

每個階段在「訊息」頁籤看 `logical reads` 與 `elapsed time`，在「執行計畫」頁籤看運算子。三個階段的實際執行計畫也存在 `docs/perf/*.sqlplan`，可直接用 SSMS 開啟。

不再需要時：

```sql
DROP DATABASE BookingPerfDb;
```
