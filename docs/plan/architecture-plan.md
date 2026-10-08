# MedConnect Architecture Plan v1

> **文件狀態**：本文件是 Claude Code 與使用者於架構規劃階段討論後的整理結果，作為 repository 的 Single Source of Truth，供後續不同分頁 / session 的 Claude Code 讀取與延續。
>
> **標記說明**：
> - **[Decision]** — 已在規劃討論中確定，作為目前實作的基準
> - **[Alternative]** — 討論中提出並比較過，但未被選為主要方案；保留供未來重新評估
> - **[Deferred Decision]** — 尚未決定，需要之後補做決策或實測驗證後才能定案
>
> **重要澄清（關於「已確認」的範圍）**：在對話中，使用者明確、逐字確認的架構決策只有一項——**「使用 Booking 作為第一個 Vertical Slice」**。本文件其餘內容（Domain 設計、Clean Architecture 分層、Booking Flow、Database Schema、API 設計、Testing Strategy、各項 Trade-off 結論）是 Claude Code 以 Senior .NET Architect 角色提出的**架構建議**，使用者尚未逐項回覆同意或反對，只表示「等你 Review」後即要求整理成文件。本文件將這些建議一併標記為 **[Decision]**，是因為它們在規劃討論中已被提出為「本專案採用的方案」（而非留白的候選項），且使用者要求將「目前已經確定的 Plan」整理成文件；但這與使用者逐條口頭確認不同。若這不符合你的預期，請明確指出要調整或降級為 [Deferred Decision] 的項目。
>
> **目前狀態**：截至本文件建立時，`D:\MedConnect` 尚未有任何程式碼、專案檔或套件安裝。本文件僅為規劃文件。

---

## 目錄

0. [變更紀錄（Change Log）](#0-變更紀錄change-log)
1. [專案目標與 MVP 範圍](#1-專案目標與-mvp-範圍)
2. [Vertical Slice 順序](#2-vertical-slice-順序)
3. [Domain Model](#3-domain-model)
4. [Clean Architecture](#4-clean-architecture)
5. [Booking Flow](#5-booking-flow)
6. [Database Design](#6-database-design)
7. [API Design](#7-api-design)
8. [Testing Strategy](#8-testing-strategy)
9. [Trade-off 決策紀錄](#9-trade-off-決策紀錄)
10. [實作分期](#10-實作分期)
11. [Risks](#11-risks)
12. [Deferred Decisions / 必須實測驗證清單](#12-deferred-decisions--必須實測驗證清單)
13. [面試官可能追問的問題（附錄）](#13-面試官可能追問的問題附錄)

---

## 0. 變更紀錄（Change Log）

### v1.1 — 2026-09-05：回應一次 Senior Architect Review 後的修正

本輪修改處理一次針對本文件的嚴格架構審查（Review），共 6 項發現，皆已與使用者確認方向後修正。依規則（見文件結尾）保留版本歷史，被取代/刪除的原文摘要如下：

1. **移除**（§7.4）：原本 Cancel Appointment 的 Status 列表包含 `409 CANCELLATION_WINDOW_CLOSED`，原文為：
   `Status: 200／400／401／404 APPOINTMENT_NOT_FOUND（...）／409 ALREADY_CANCELLED／409 CANCELLATION_WINDOW_CLOSED／409 CONCURRENCY_CONFLICT。`
   原因：§3.5 Domain Invariant 從未定義「取消時間窗」規則，屬文件內部不一致。MVP 決定不做此功能，已從 §7.4 移除，並在 §3.5、§7.4 加註說明，規則本身移入 §12 Deferred Decision。
2. **新增**（§12、§8.5）：加入「`SaveChangesAsync` 在部分 UPDATE 因 optimistic concurrency 失敗時是否完整 rollback（無 partial commit）」為必須實測驗證項目，並要求 §8.5 招牌測試新增一條直接查 DB 驗證的專門情境。
3. **移除**（§6.1 `users` 表）：原本包含 `version INT NOT NULL DEFAULT 0,` 欄位。原因：MVP 沒有任何併發修改 `User` 的場景（Login 唯讀、無 Update User API），保留即違反 §9.1「說不出解決什麼問題就不加」的原則。
4. **限縮說明**（§6.1 `users.role`、§12）：`role` 欄位本身保留（§7.3 booking 需要 `role: Patient` 做 authorization，有實際消費者），但 `Admin` 列舉值目前 MVP 四支 API 中零消費者，明確標示為 §12 Deferred Decision，避免被當作已設計的功能。
5. **補充說明（無結構變更）**（§6.1 `schedule_slots`）：`ux_slot_doctor_start` 與 `ix_slot_doctor_time` 兩個索引前綴重疊並非疏漏——前者是業務唯一性約束，後者是 Schedule Query 的 covering index，兩者目的不同，予以保留並寫明原因。
6. **新增**（§5.2）：明確的 Exception 攔截對應表，區分 `DbUpdateConcurrencyException`（→ `CONCURRENCY_CONFLICT`）與 `DbUpdateException`/MySQL duplicate entry on `ux_appt_slot_active_patient`（→ `DUPLICATE_BOOKING`），避免實作時混在同一個 catch 區塊處理。

### v1.2 — 2026-09-07：Phase 1b 實作後的驗證與修正

Phase 1b（ScheduleSlot/Appointment entity + Domain unit test + EF 設定 + migration + seed）實作完成、經過一次獨立 blind code review 後，有 1 項與使用者確認過方向的 [Decision] 文字修正，另有數項 §12 待驗證清單項目已用真實 MySQL 8.0.39（Docker）驗證，記錄如下：

1. **修正**（§3.5）：原文「`TimeSlot` 為 Value Object（EF Core owned type）」改為「EF Core Complex Type」。實作時改用 EF Core 8+ 引入的 Complex Type（`ComplexProperty`）而非 `OwnsOne`，因為 Complex Type 是專門為「無獨立身分的純值物件」設計的較新機制，語意更貼近 `TimeSlot` 的本質。已與使用者確認採用此技術選擇並更新本文件用詞。
   **副作用（新發現，非文件原有假設）**：EF Core 9 目前不支援對 Complex Type 的屬性建立 composite index（`HasIndex()` 的 lambda 與字串路徑 overload 皆在 design-time 丟例外；底層 `IMutableEntityType.AddIndex()` 也拒絕「index 屬性須全部屬於同一個 entity type」）。`ux_slot_doctor_start`／`ix_slot_doctor_time`（跨 `doctor_id` 與 `TimeSlot.StartUtc`）因此改在 migration 用原始 SQL（`migrationBuilder.Sql(...)`）直接建立，不透過 EF 模型宣告；代價是這兩個 index 對 EF 的 model diff 不可見，未來若要調整需手動維護 migration。
2. **驗證通過**（§12 第 2 項）：實測 MySQL 版本為 `8.0.39`（≥ 8.0.16），`CHECK` constraint（`ck_slot_capacity`/`ck_slot_booked`/`ck_slot_time_order`）在此版本下確認真的會擋下違規寫入（非文件退化為 no-op）。
3. **驗證通過，並發現新限制**（§12 第 3 項）：Pomelo 的 `HasComputedColumnSql(..., stored: true)` 確認能正確映射並產出可用的 generated column（`active_patient_id`），partial-unique-index 模擬（同 slot 同病人僅一筆有效預約）也用真實資料驗證行為正確（取消後可再次預約、重複預約會被擋下）。**新發現**：MySQL 不允許「被 STORED generated column 依賴的欄位」所在的 FK 使用 `ON DELETE CASCADE`/`SET NULL`（會丟 Error 1215），因此 `fk_appt_patient`（`active_patient_id` 依賴 `patient_id`）改為 `ON DELETE RESTRICT`；順帶把其餘三個 FK（`fk_slot_doctor`/`fk_patients_user`/`fk_appt_slot`）也統一改為 `RESTRICT`，因為 §6.1 原始 SQL 本來就未寫 `ON DELETE`（MySQL 預設即 RESTRICT），EF Core 的慣例預設值 `CASCADE`其實是與文件本身不一致，一併修正對齊。

### v1.3 — 2026-09-12：Phase 1c 開始前，解決 MediatR 授權這項 Deferred Decision

1. **驗證通過並決策確定**（§12 第 5 項）：查證 MediatR 現況——v13.0.0 起改為商業授權（依公司規模分級收費，另有免費 Community 版但需註冊 mediatr.io 帳號取得 license key，僅適用年營收 <$5M / 非營利 / 教育 / 非正式生產環境）；v12.x（最後一版 v12.5.0）維持原本 Apache 2.0 授權，可永久免費使用，不需帳號或 license key，但不再有新版本。與使用者確認：**專案釘住 `MediatR` v12.5.0（Apache 2.0）**，不採用 v13+ 商業版（避免引入帳號/license key 這類與 4 支 API 規模不相稱的操作負擔，見 §9.1），也不改用其他免費替代品（`Mediator`/`FreeMediator` 等）——保留使用「正牌 MediatR」在面試情境下的可辨識度與可討論性。§9.2「採用 MediatR」的決策本身不變，本項只解決「用哪個版本」。

### v1.4 — 2026-09-18：Phase 1c 併發測試實作後，兩項被實測推翻的假設

Phase 1c 的 §8.5 招牌測試（Testcontainers + 真實 MySQL 8.0.39 + 50 個病人搶 Capacity=5）第一次跑起來就推翻了本文件兩項 [Decision] 的隱含假設。依 §7 版本保留規則，原文一律保留，於下方加註適用範圍，不直接覆寫。

1. **新增（§5.2 例外對映表的缺口）**：實測發現 Booking 在高併發下會大量觸發 **InnoDB 死結（MySQL error 1213）**，而 1213 不在 §5.2 的對映表內，會以未對映的 `InvalidOperationException` 外洩成 HTTP 500。首次實測 50 個請求中有 **47 個**是死結。

   **根因（已用 `SHOW ENGINE INNODB STATUS` 的 LATEST DETECTED DEADLOCK 報告確認，非推測）**：兩個交易同時 `HOLDS lock mode S` 又 `WAITING FOR lock_mode X`，鎖在 `schedule_slots` 同一列。S 鎖來自 `appointments` INSERT 的 `fk_appt_slot` 外鍵檢查（需確認父列存在），X 鎖來自 `schedule_slots` 的 UPDATE。關鍵在於 **EF Core 把 INSERT 排在 UPDATE 之前**：當 principal 是 *Modified*（而非 Added）時，EF 的 `CommandBatchPreparer` 不會建立相依邊，語句順序退回 `ModificationCommandComparer` 的**資料表名稱字典序**，`appointments` < `schedule_slots`。因此每個交易都是「先拿 S、再想升級成 X」，互相等待成死結。

   **同時證實無效的修法**：調整 entity 加入 change tracker 的順序**不能**影響語句順序——原本的 `BookAppointmentHandler` 就已經是先 `Update(slot)` 才 `Add(appointment)`，實際 SQL 仍是 INSERT 在前。

   **採用的修法（已與使用者確認）**：`IUnitOfWork` 新增 `ExecuteInTransactionAsync<T>()`，Booking 在同一筆顯式交易內拆成兩次 `SaveChangesAsync`——先 UPDATE `schedule_slots`（取得 X 鎖）、再 INSERT `appointments`（做 FK 檢查）。實測 5 輪共 250 個請求，死結 **0 次**。代價是單筆預約由「可能批次成一次來回」變成保證兩次來回。未採用 `SELECT ... FOR UPDATE`（放棄樂觀鎖即放棄 §1 的核心賣點），亦**未**加入任何 retry（是否加 retry 為獨立決策，見 §12）。

   **衍生的新風險與其驗證**：拆成兩次 flush 後出現「第一次 UPDATE 已成功、第二次 INSERT 才失敗」的視窗。已新增專門的整合測試（`BookingTransactionRollbackTests`）以重複預約重現此情境，實測確認顯式交易完整回滾，`booked_count` 與 `version` 皆未被推進、兩表無 drift。

2. **限縮適用範圍（§8.5 斷言第 2、4 條）**：§8.5 同時要求「`BookedCount == min(N, Capacity)`（50 人搶 5 → 恰為 5）」與「第一版**不啟用 retry**」。實測證明這兩條**在物理上互斥**，且與實作正確性無關：50 個請求在同一瞬間都讀到 `version = 0`，樂觀鎖的 CAS 每一輪至多一個贏家，要填滿 5 個名額需要 5 個回合，而「5 個回合」的定義就是 retry。

   **證據**：在完全排除死結變因的對照實驗中（以原生 SQL 重現 handler 語意、但順序改為 UPDATE-先於-INSERT），連續 3 次皆為「1 成功 / 49 CONCURRENCY_CONFLICT，0 死結」，`booked_count == active_appointments == 1`。修好死結後的正式測試則落在 **2～3 成功**之間（5 輪實測：3/3/3/2/3），因真實請求間的延遲抖動讓部分請求得以讀到較新的 version——即成功數本質上是**非確定性**的，`== 5` 是一條會 flaky 的斷言。

   **v1 適用範圍（已與使用者確認）**：§8.5 下方斷言清單第 2 條與第 4 條**僅在啟用 retry 的版本（v2）成立**，v1 不適用。v1 的正確斷言改為「至少賣出 1 個、成功數 ≤ Capacity、`BookedCount == 實際成功數`、`COUNT(active appointments) == BookedCount`」。第 1、3、5、6、7 條不受影響，完全保留。原文一併保留於 §8.5，未刪除。

   **刻意不在本輪一併決定的事**：是否加入 retry、重試次數、只對死結重試還是也對 concurrency conflict 重試、以及 retry 是否會讓某些請求因排隊順序而系統性地更容易搶到（公平性），皆列為獨立決策，見 §12。

### v1.5 — 2026-09-21：補齊死結（1213）的例外轉譯，與 retry 無關的獨立修正

v1.4 修掉了死結的**成因**（S→X 鎖升級），但沒有替死結補上例外轉譯——`UnitOfWork.SaveChangesAsync()` 當時完全不認得 MySqlError 1213，若死結真的發生仍會以未對映例外外洩成 HTTP 500。本輪補齊這個錯誤處理完整性缺口，**不涉及、也未實作任何 retry 邏輯**（retry 本身仍是 §12 第 7 項的獨立待決事項）。

1. **新增**：`TransientConflictException`（Application 層），與 `ConcurrencyConflictException` 刻意分開，理由見該類別的 XML 文件註解——兩者雖然對客戶端都是「可重試」，但成因結構不同（version CAS 失敗 vs InnoDB 主動犧牲交易），未來若要針對其中一種調整重試策略，分開的型別能避免屆時要重新拆開。
2. **修正（§5.2 例外對映表）**：新增第三列 `InvalidOperationException` → `TRANSIENT_CONFLICT`。**這裡有一個值得記錄的意外**：原本預期死結會跟 `DUPLICATE_BOOKING` 那列一樣是 `DbUpdateException`，但實測發現完全不是——因為 Booking 現在是在顯式交易（`ExecuteInTransactionAsync`）裡呼叫 `SaveChangesAsync`，EF Core 的 `ExecutionStrategy` 偵測到例外看似 transient、又處於使用者自管交易中且未設定 `EnableRetryOnFailure()`，判定無法安全自動重試，於是把原始例外包成 `InvalidOperationException` 往外丟，訊息還會建議去開 `EnableRetryOnFailure()`。用一組與 booking 無關、兩個 slot 故意用相反鎖定順序更新的對照實驗重現死結，5 次重現、5 次都是同一個形狀：`InvalidOperationException -> DbUpdateException -> MySqlException{ErrorCode=LockDeadlock}`。若照最初直覺寫 `catch (DbUpdateException ex) when (...1213...)`，這個 catch 永遠不會命中——這正是「先實測再寫程式碼」在本專案第二次抓到與預期不符的例外形狀（第一次是 v1.4 的死結成因本身）。
3. **新增**（§7.3）：Status 列表補上 `409 TRANSIENT_CONFLICT`（可重試）。
4. **新增測試**：`UnitOfWorkTests` 新增一個案例，比照既有 `DuplicateBookingException` 測試的手法（反射建構真實 `MySqlException`，因為它的建構子全是 non-public），驗證 `MySqlException{ErrorCode=LockDeadlock}` 包在 `DbUpdateException` 又包在 `InvalidOperationException` 的三層結構下，`UnitOfWork.SaveChangesAsync()` 能正確轉譯成 `TransientConflictException`；同時驗證這個新 catch 不會誤吃既有的 `DuplicateBookingException`/`ConcurrencyConflictException` 情境（兩兩互斥的例外型別，天生不會搶到彼此，不靠攔截順序維持互斥）。

#### 附註（v1.5）：`EnableRetryOnFailure()` 刻意保持關閉

**[Decision — 使用者補充]**

原因：本專案的併發衝突處理（樂觀鎖 retry、未來 v2 的 bounded retry）都需要業務邏輯層級介入（重新讀取最新資料再判斷），EF Core 的 `EnableRetryOnFailure()` 是機械式重跑整個 transaction，不會重新讀取資料，兩者若同時啟用會導致 retry 次數不可控、且可能用過期資料重試。v2 設計 retry 時維持只在 Application 層手寫，不開啟此開關。

此限制同時併入 §12 第 7 項 (d) 的待決範圍，作為 v2 設計 retry 時的既定限制條件。

### v1.6 — 2026-10-01：Schedule Query（第一個 CQRS Query-side 切片）實作前，與 §7.2 原文的落差

Schedule Query 開始實作前，使用者直接給出與 §7.2 原文不同的設計，當場確認定案。依 §7 版本保留規則，§7.2 原文不覆寫，差異記錄如下：

1. **Route / 回應形狀改變**：§7.2 原文是 `GET /api/v1/doctors/{doctorId}/slots?from=...&to=...`，回傳 `PagedResult<SlotDto>`（含分頁）。實際採用 `GET /api/v1/schedule-slots?doctorId={id}&date={date}`——**單一日期查詢**，不支援日期範圍，也**不分頁**，直接回傳 `List<ScheduleSlotDto>`。原因：MVP 範圍，日期範圍查詢與分頁目前沒有實際消費者。
2. **DTO 欄位改變**：`ScheduleSlotDto(Id, DoctorId, StartUtc, Capacity, AvailableCount, Status)`——拿掉原文的 `doctorName`、`bookedCount`。不含 `doctorName` 的前提假設：前端查詢流程會先自己取得醫生清單，這個端點只需要回傳時段資訊；`bookedCount` 改用 `AvailableCount`（`Capacity - BookedCount`），避免對外暴露內部計算用的原始欄位。
3. **保留明確的 `404 DOCTOR_NOT_FOUND`**：與原文一致（原文本來就有這條），但特別強調不可退化成「查無資料一律回空陣列」——`doctorId` 不存在（404）與「該 doctorId 當天沒有時段」（200 + 空陣列）必須是呼叫端可分辨的兩種不同結果。實作：新增 `IDoctorRepository.ExistsAsync()`（最小化介面，目前只有這一個方法）+ `DoctorNotFoundException`，對映模式比照既有的 `ScheduleSlotNotFoundException` → `404 SLOT_NOT_FOUND`。
4. **§4.2 Deferred Decision 的落地**：§4.2 原本把「Application 是否依賴 `Microsoft.EntityFrameworkCore.Abstractions`」列為待決，預設傾向「`IReadDbContext` 介面隔離的嚴格版本」。本次查詢切片具體採用的做法是：新增 `IScheduleSlotQueryRepository`（`Application/Abstractions`），方法簽章直接回傳 `Task<List<ScheduleSlotDto>>`（DTO，不是 `IQueryable`、也不是 Domain Entity），Infrastructure 內部用 EF Core LINQ 投影（`Select` + `AsNoTracking()`）實作。即：**不**讓 Application 依賴 EF Core Abstractions，走比「`IReadDbContext`」更嚴格的「repository 直接回傳 DTO」版本。與既有的 `IScheduleSlotRepository`（寫入用，回傳/操作 Domain Entity）刻意分成兩個介面，放在同一個 `Abstractions/` 資料夾，不額外分子資料夾。

### v1.7 — 2026-10-03：Cancel Appointment 實作後的驗證與修正（§5.3、§7.4、§8.5）

Cancel Appointment 實作並用真實 MySQL 8.0.39 驗證後，有數項與 §5.3 原文不同或原文未涵蓋的結論。依 §7 版本保留規則，§5.3、§7.4、§8.5 原文一律保留，差異記錄如下：

1. **§5.3 的「SaveChanges()：兩列（appointment、slot）各自做 version check」描述的是單一次 flush；實際改為同一筆顯式交易內的兩次 flush，先 `schedule_slots` 後 `appointments`**（`ExecuteInTransactionAsync`，與 Booking 同一機制，見 v1.4）。原因是鎖定順序必須與 Booking 一致（先拿 slot 的 X 鎖），否則 Book/Cancel 混跑會重新引入 v1.4 修掉的死結風險。
   **實測證據（非推論）**：
   - 把兩次 flush 合併成單次 `SaveChangesAsync` 後，MySQL server 端 general log 顯示收到的順序是 `UPDATE appointments` 先於 `UPDATE schedule_slots`——EF 在 Modified／Modified 且無相依邊時，確實退回資料表名稱字典序（`appointments` < `schedule_slots`），與 Booking 的鎖定順序相反。這把 v1.4 針對「Added dependent／Modified principal」的分析，在「兩個 Modified」的情境下也實測確認了一次。
   - 正確寫法下，同一份 general log 與應用程式自己的 EF SQL log 都顯示：`start transaction` → `UPDATE schedule_slots` → `UPDATE appointments` → `commit`。
   - **衍生的寫法限制**：`appointment.Cancel()` 必須在第一次 flush **之後**才呼叫。change tracker 會把所有已修改的實體一起 flush，提早呼叫會讓 appointment 的 UPDATE 被併進第一次 flush，順序又反了（有單元測試以「兩次 flush 當下的實體狀態」鎖住這件事，並經 mutation 驗證會紅燈）。
2. **新增 Domain 守衛 `Appointment.EnsureCanBeCancelled()`（設計決策，已與使用者確認）**：因為寫入順序要求 `slot.Release()` 先於 `appointment.Cancel()`，而「已取消」這條規則原本只存在於 `Cancel()` 內，順序一旦固定，重複取消 `BookedCount` 已為 0 的時段就會先撞上 `SlotReleaseUnderflowException`（未對映，HTTP 500），而不是 `409 ALREADY_CANCELLED`。解法是把規則抽成可單獨呼叫的守衛，`Cancel()` 也呼叫它，規則只活在 Domain 一處。**未採用**的替代方案：在 Handler 內自己檢查 `Status == Cancelled` 再丟例外（把 Domain 規則在 Application 層重複一份）。
3. **所有權檢查（`appointment.PatientId != 呼叫者`）排在「已取消」檢查之前**，且「預約不存在」與「不是你的」丟同一個 `AppointmentNotFoundException`、同一個訊息。這與 §7.4 既有 [Decision]（兩者合併回應，避免洩漏他人資料存在性）一致，非偏離；順序本身是實作層的關鍵細節——反過來的話，陌生人可從 409 vs 404 推測他人預約是否已取消。
4. **§7.4 的選填 request body `{ "reason": "..." }` 目前未實作**：沒有任何欄位可以儲存取消理由，`CancelAppointmentCommand` 只有 `(AppointmentId, PatientId)`。屬於 MVP 範圍取捨，若未來要做，需先決定儲存位置（`appointments` 新欄位或獨立稽核表）。§7.4 的 Route（`POST .../cancel`）、`200 { appointmentId, status, cancelledAtUtc }`、`404 APPOINTMENT_NOT_FOUND`、`409 ALREADY_CANCELLED` 均已照原文實作並加入 `GlobalExceptionHandler` 對映。
5. **§8.5 [Decision] 變體測試「併發 Cancel（同一 appointment）恰好一個成功」已完成**：5 輪 × 10 個併發請求取消同一筆預約（slot 上另有 2 筆其他預約，使 double-release 可被偵測），每輪皆為「1 成功 + 9 `CONCURRENCY_CONFLICT`」，`booked_count` 恰好只釋放一次（3 → 2），兩表無 drift，無死結（`TransientConflictException` 一旦出現會讓測試直接崩潰）。輸家得到的是 `CONCURRENCY_CONFLICT`（可重試，重試後才會看到 `ALREADY_CANCELLED`），而非直接 `ALREADY_CANCELLED`——因為併發下兩者都通過了記憶體內的 pre-check，真正的裁決者是 slot／appointment 各自的 version 檢查。**§8.5 的另一個變體「Book/Cancel 混合負載」尚未做**，留待獨立處理。
6. **Cancel 的 atomicity 驗證（比照 §12 第 13 項對 Booking 的驗證方式，等級只高不低）**：用臨時 MySQL trigger 強制 Cancel 的第二次 UPDATE（`appointments`）必然失敗，第一次 UPDATE（`schedule_slots`）則先成功，重現「第一次 flush 已成功、第二次才失敗」的危險視窗，實測整筆交易完整回滾（`booked_count`、兩列的 `version`、`status` 皆不變）。反向驗證（mutation）：移除顯式交易後，測試如預期紅燈，實測出現 `booked_count` 歸零但 `Appointment.Status` 仍為 `Booked` 的 drift。

### v1.8 — 2026-10-03：Validation Behavior 與 `400 VALIDATION_FAILED` 落地（§5.1、§7、§9.2、§9.8）

§7 的狀態碼表從 Login 到 Cancel 都列了 `400 VALIDATION_FAILED`，§5.1 也把 Validation 列為 MediatR pipeline 的一環，但 1d 實作完四支 API 後，程式碼裡兩者都不存在（以 grep 確認，不是印象）。本輪補上。依 §7 版本保留規則，§5.1、§7 原文不覆寫，記錄如下：

1. **[Decision] 手寫驗證，不引入 FluentValidation。** 目前的規則（必填、長度上限、id > 0）簡單到不需要新套件；每個新套件都必須能用一句話說明解決了什麼問題（§9.1）。介面是 `IRequestValidator<TRequest>`，validator 與其 request 放在同一個資料夾，由 `AddApplication()` 以組件掃描自動註冊（約十行 reflection，不為此引入 Scrutor）。**[Alternative]（已比較但不採用）**：FluentValidation——規則變複雜（跨欄位條件、非同步檢查、大量規則）時再評估升級，屆時 `IRequestValidator` 可以被取代而不影響 Handler。
2. **[Decision] Validator 只檢查結構，絕不查資料庫。** 必填、長度、數值範圍可以；「這筆資料存不存在、是不是你的」不行，那屬於 Handler。理由：防 Enumeration 的邏輯（Login 帳號不存在與密碼錯誤收斂成同一例外、Cancel 的「不存在」與「不是你的」收斂成同一例外）必須只活在 Handler 一處，驗證與存在性判斷若拆在兩處，規則就會散落、容易被其中一處破壞。Login 因此刻意不檢查 email 格式。
3. **[Decision] Pipeline Behavior 的範圍只有 `ValidationBehavior`。** §5.1 規劃的順序是 Logging → Validation → Performance，這裡只實作 Validation，並以 `AddOpenBehavior` 註冊，慣例是「註冊順序 = 由外而內的執行順序」，Logging / Performance 留到 1e（Serilog / OpenTelemetry），屆時加在它的前後即可。驗證失敗以 `RequestValidationException` 表達，由 `GlobalExceptionHandler` 對映，與 §9.8 的 Exception 模式一致（Result pattern 仍是 [Deferred Decision]）；Handler 在驗證失敗時完全不會被呼叫，不會開交易、不會拿鎖。
4. **`400 VALIDATION_FAILED` 的回應合約**：`application/problem+json`，含 `errorCode`、`traceId`，以及一個 §7 原文沒有的新欄位 `errors`（欄位 → 訊息陣列，與 ASP.NET 內建 `ValidationProblemDetails` 同形狀；欄位名用對外名稱，例如 Cancel 回 `id`、Book 回 `slotId`）。400 有**兩個來源**：`ValidationBehavior`（業務層驗證）與 `[ApiController]` 的 model binding 失敗（例如 `doctorId=abc`、JSON 格式錯誤，原本回一個沒有 `errorCode`、key 為 PascalCase 的預設 400，且發生在 MediatR 之前）。兩者都走同一個 `ApiProblemDetails`（後者透過 `InvalidModelStateResponseFactory`，key 轉 camelCase），確保只有一種 400 形狀。
5. **規則與行為變更**：Login——`email` 必填且 ≤ 256（對應 `users.email VARCHAR(256)`）、`password` 必填；Book——`slotId > 0`；Cancel——`id > 0`；GetAvailableSlots——`doctorId > 0`、`date` 不可為 `default`（query string 少了 `date` 時 model binding 會悄悄給 `0001-01-01`）。`PatientId` 來自已驗證的 JWT、不是使用者輸入，不驗證。以下行為變更皆為預期：非正數 id 由 404 變 400；缺 `date` 的 slots 查詢由**靜默的 `200 []`** 變 400；Login 空 email、空 password、email 超過 256 由 401 變 400。以 characterization 測試在**未修改的程式碼**上先記錄實際回應、改動後以同一組輸入再跑一次：19 個 case 中 16 個依預期改變，3 個不變（2 個 control 與不匹配任何路由的 `id=abc`）。
6. **順帶發現並修正的既有偏離（非原定範圍）**：被 `GlobalExceptionHandler` 對映的錯誤（404 / 409 / 401）實際一直回 `application/json`，與 §7 規定的 `application/problem+json` 不符。原因是 `WriteAsJsonAsync` 會無條件覆寫先前設好的 `Response.ContentType`，需要把 `contentType` 直接傳給它。這個 bug 之所以沒被發現，是因為沒有任何測試斷言過 Content-Type；現已新增嚴格的斷言 helper（不用 `ContentType?.MediaType.Should()`，那種寫法在 header 缺席時會悄悄跳過斷言）並套用到既有的 404 / 409 測試。反向驗證（mutation）：還原舊寫法後整個整合測試套件有 16 個測試變紅，含既有的 Schedule Query 404、Cancel 404 / 409。
7. **JWT claims 組裝共用（實作細節，無架構變更）**：`JwtClaimsBuilder.BuildClaims` 放在 Infrastructure（`JwtRegisteredClaimNames` 來自只有 Infrastructure 引用的 JWT 套件，§4.2），簽發端與測試的 token 偽造 helper 共用同一份 claims 結構。
8. **已知、刻意延後（1e）**：`ExceptionHandlerMiddleware` 目前會把已對映的業務例外（404 / 409 / 現在還有 400）以 error level 記 log；驗證失敗是使用者輸入問題，不該是 error，留到 1e 的 Logging 一併處理。

### v1.9 — 2026-10-03：v2 bounded retry（MediatR `RetryBehavior`）落地（§9.5、§12 第 7 項、§8.5）

§12 第 7 項（Retry 機制）經一次獨立的 Architect Mode 討論後定案並實作。依 §7 版本保留規則，§9.5、§8.5、§7.3 原文不覆寫，差異與實測記錄如下：

1. **[Decision] Retry 放在 MediatR Pipeline Behavior，而非 Handler 內迴圈。** 與 §9.5 [Deferred Decision] 原文的差異：§9.5 寫「放在最外層（非 MediatR behavior 內以避免同一 scoped DbContext 重放）」，但 §12 第 7 項 (d)（v1.5 補充）已把 pipeline behavior 列為可選方案，本輪採用之，並以 `IUnitOfWork.ResetTracking()` 直接解決 §9.5 擔心的「同一 scoped DbContext 重放」問題。**[Alternative]（已比較但不採用）**：每次嘗試開新 DI scope 重新解析 Handler——Behavior 內無法乾淨地做到（behavior 與 handler 已在同一個 scope 解析完成）。註冊順序：`ValidationBehavior`（外）→ `RetryBehavior`（內），不合法的輸入不進入重試迴圈。
2. **[Decision] `IUnitOfWork.ResetTracking()` = `DbContext.ChangeTracker.Clear()`，RetryBehavior 在每次重試前（延遲之後）呼叫。** Application 層不依賴 `DbContext`。**Change Tracker 風險已用真實 MySQL 實測證明，非僅憑分析**：先加空實作，測試（`RetryTrackingTests`）紅燈——同一 scope 內重讀拿到的是同一個過期物件（`BookedCount=0`、`Version=0`，而資料庫已是 1、1）；改成 `ChangeTracker.Clear()` 後綠燈。端到端 mutation（`RetryPipelineTests`，拿掉 RetryBehavior 內的 `ResetTracking()` 呼叫）：4 個測試中 3 個紅燈——Booking 重試以 `RetryExhaustedException` 失敗（陳舊 version 每次都衝突）；Booking 的 SLOT_FULL 測試因沒有走到重試後的重讀而紅；**Cancel 紅燈的方式最值得記錄**：第一次 flush 成功、第二次衝突後，記憶體中的 appointment 已被 `Cancel()` 標為已取消，沒有 reset 的重試會讀到這個髒實體，丟出**假的** `AppointmentAlreadyCancelledException`（資料庫其實仍是 Booked）。
3. **[Decision] 只重試 `ConcurrencyConflictException` 與 `TransientConflictException`**；`SlotFullException`、`DuplicateBookingException`、`AppointmentAlreadyCancelledException` 等業務結果、以及任何未預期例外一律不重試、原樣往外丟。重試後重新讀到「已滿」會自然以 `SLOT_FULL` 結束（有測試）。
4. **[Decision] 參數**：最多重試 3 次（總嘗試 4 次）；延遲 `baseDelay × (attempt + 1) + Random(0, jitterRange)`（`attempt` 從 0 起算，線性、非指數退避），`baseDelay = 25ms`、`jitterRange = 50ms`（第 1/2/3 次重試分別為 25–75 / 50–100 / 75–125ms）。參數放在 `RetryOptions`（單一用途：讓測試把延遲設成 0）。
5. **[Decision] 耗盡後丟 `RetryExhaustedException`（Application 層，InnerException 保留最後一次的原始例外），對映 `409 RETRY_EXHAUSTED`**——獨立的 errorCode，不沿用 `CONCURRENCY_CONFLICT`。理由：v1.5 刻意把死結與樂觀鎖衝突分成兩種 errorCode，耗盡時若統一成 `CONCURRENCY_CONFLICT` 會悄悄抹掉這個區分，且客戶端無從得知「系統已經重試過仍失敗」。（§7.3 的狀態碼列表原文保留，`RETRY_EXHAUSTED` 以本條為準。）
6. **實測：50 人搶 Capacity=5（同一個測試 `ConcurrentBookingDistributionTests`，20 輪，前後同條件對比）**：
   - **無 retry**：1 成功 × 10 輪、2 成功 × 10 輪，平均 **1.50**，0 輪填滿；失敗全為 `CONCURRENCY_CONFLICT`（970 個）。
   - **有 retry（預設參數）**：連跑 3 次共 60 輪，**60 輪全部恰為 5 成功**（平均 5.00，每輪 `booked_count == active_appointments == 5`，無超賣、無 drift）。失敗原因：`SLOT_FULL` 為主，另有少量 `RETRY_EXHAUSTED`（每 20 輪／1000 個請求中 3、24、10 個）。**值得注意**：這些 `RETRY_EXHAUSTED` 的請求在重試用盡時，名額其實常已被搶完——它們若再多讀一次本應得到 `SLOT_FULL`；客戶端拿到的是「已重試仍衝突」而非「已滿」，兩者都是 409，但語意不同。
7. **Cancel 與 retry 的交互作用（已驗證）**：Cancel 的雙階段 flush 在 `RetryBehavior` 內行為正確——`RetryPipelineTests` 刻意製造「第一次 flush（`schedule_slots`）成功、第二次 flush（`appointments`）才衝突」，實測交易整筆回滾，重試後 `booked_count` 只釋放一次（3→2）、slot `version` 3→4（失敗那次沒有留下痕跡）、兩表無 drift。**併發 Cancel 的輸家結果改變**：原本（v1.7 第 5 項）輸家得到 `CONCURRENCY_CONFLICT`，現在重試時重新讀到最新資料，輸家得到 `ALREADY_CANCELLED`（15 輪 × 10 個請求實測：每輪皆為 1 SUCCESS + 9 ALREADY_CANCELLED，0 RETRY_EXHAUSTED）——對客戶端是更準確的結果。
8. **ValidationBehavior 不被 retry 包住**：以真實 pipeline 驗證，不合法請求 validator 只執行一次、Handler 與交易完全沒被呼叫、`ResetTracking` 0 次；DI 註冊順序另有單元測試鎖住。
9. **[Decision] 既有測試的處理（已與使用者確認）**：
   - **`RETRY_EXHAUSTED` 是新的「正常失敗型態」**：`ConcurrentBookingTests`（§8.5 招牌測試）與併發 Cancel 測試原本只 catch `SlotFullException` / `ConcurrencyConflictException`，`RetryExhaustedException` 逃出去會讓整個測試以未處理例外崩潰——實測招牌測試連跑 6 次有 1 次因此變紅。兩個測試都已把 `RETRY_EXHAUSTED` 加進預期內的失敗清單（其餘例外仍故意不接，避免偽裝成普通併發失敗）。
   - **健康指標（只印出、不當失敗條件）**：兩個測試與 `ConcurrentBookingDistributionTests` 都會印出 `[health] RETRY_EXHAUSTED=n/總請求數 (比例)`，供人工檢視趨勢。實測波動很大：招牌測試連跑 30 次，多數為 0/50，偶發 2～5/50，兩次離群值達 17/50（34%）與 36/50（72%）——**即使如此那幾次的成功數仍恰為 5**（耗盡的是名額已被搶完後的輸家）。此數字長期上升代表重試參數（次數／延遲／jitter）或鎖競爭需要檢視。
   - **§8.5 第 2、4 條不收緊成 `== min(N, Capacity)`**：改為**下限斷言 `>= Capacity - 1`**（`ConcurrentBookingTests`）。理由：有界重試下精確值斷言會是 flaky test；60 輪全為 5 是機率證據、不是保證，`RETRY_EXHAUSTED` 的存在本身就說明了這一點。`ConcurrentBookingDistributionTests` 只印出每輪分佈與耗盡次數，不斷言成功數。§8.5 原文不覆寫。
   - **實測（調整後）**：`ConcurrentBookingTests` 連跑 15 次 15 次通過；併發 Cancel 測試連跑 8 次 8 次通過、`RETRY_EXHAUSTED` 0 次。
10. **未解決**：§12 第 7 項 (c)（公平性：重試是否讓某些請求系統性地更容易搶到）本輪**未量測**。

### v1.10 — 2026-10-08：Architecture Tests（NetArchTest）落地（§4.2、§10 1e）

§4.2 的 [Decision]「用 NetArchTest 把依賴規則寫成自動化 Architecture Test」於 1e 第一塊實作。依 §7 版本保留規則，§4.2 原文不覆寫，記錄如下：

1. **[Decision] 獨立專案 `MedConnect.ArchitectureTests`**（`NetArchTest.Rules` 1.3.2），不放進既有的 `*.UnitTests`：這類測試掃描的是編譯後組件，不是執行行為。四條規則（皆為嚴格版本、無例外子句）：Domain 不依賴其他層與 EF Core／MySqlConnector／MediatR／ASP.NET Core；Application 不依賴 Infrastructure／Api；Application 完全不依賴 `Microsoft.EntityFrameworkCore`；Infrastructure 不依賴 Api。
2. **實作前的事實確認（非推測）**：第 3 條規則動手前先查 Application 是否有任何 EF Core 的直接／間接引用——原始碼 0 個 `using Microsoft.EntityFrameworkCore`、0 個 EF Attribute（僅 4 處 XML 註解文字提到名稱）；編譯後組件引用只有 MediatR、MediatR.Contracts、DI.Abstractions、Domain 與 System（Domain 只有 System）。故不需要任何例外子句。
3. **[Decision] 另有 `ScannerSanityTests`（解決的唯一問題：NetArchTest 對空集合或打錯的命名空間字串一律回報成功，規則可能永遠綠燈卻什麼都沒擋）**：每個被掃描的組件確實有型別；規則用到的命名空間字串對「確實存在的依賴」必須找得到（正向對照）。
4. **反向（mutation）驗證**：暫時植入違規再還原，7 種情境皆使對應規則紅燈且失敗訊息列出違規型別全名——Domain→MediatR（base type）、Domain→EF Core（僅出現在方法內部）、Application→EF Core（欄位／屬性上的 Attribute／僅方法內部，共三種寫法）、Application→`MedConnect.Infrastructure.*`、Application→`MedConnect.Api.*`、Infrastructure→`MedConnect.Api.*`、Domain→`MedConnect.Application.*`。規則 2、4 無法用「真的加專案參考」植入違規（反向參考會是循環參考、編譯不過），所以這兩條的 mutation 是以違規命名空間的型別＋依賴它的型別植入。
5. **這組測試補上了編譯器的哪些缺口、哪些只是防護網（誠實的定位）**：
   - **防護網（編譯器已經會擋）**：規則 2（Application→Infrastructure／Api）、規則 4（Infrastructure→Api），以及規則 1 的「分層」部分（Domain→Application／Infrastructure／Api）。專案之間的 ProjectReference 是單向的，反向引用是循環參考、根本編譯不過。這幾條測試的價值是：萬一有人調整專案結構（例如合併專案、移除或改向某個 ProjectReference），規則仍會把違規抓出來；它們不是在補編譯器的洞。
   - **真正補上編譯器缺口的**：規則 1 的「外部技術」部分（Domain 不依賴 EF Core／MySqlConnector／MediatR／ASP.NET Core——任何人在 Domain 的 csproj 加一個 NuGet 套件，編譯器完全不會有意見）與規則 3（Application 不依賴 EF Core——同理，加一個套件、或經由傳遞依賴用到 EF 型別／Attribute，編譯器不擋）。
6. **[已知缺口，之後再補]（§4.2 表格列出但本次範圍外，不在本輪順帶擴張）**，依優先順序：
   1. **Api 不直接使用 `DbContext`／EF 型別（優先）**——§4.2 表格明列的規則，且 Api 是唯一同時引用 Infrastructure 的上層，編譯器擋不住它直接用 EF 型別，是缺口中風險最高的一個。
   2. Application 不依賴 ASP.NET Core 與 MySQL driver（MySqlConnector／Pomelo）。

---

---

## 1. 專案目標與 MVP 範圍

**[Decision]**

MedConnect 是一個醫療預約情境的 Backend API，用來在面試中展示：
ASP.NET Core、Clean Architecture、EF Core、MySQL、CQRS/MediatR、JWT Authentication/Authorization、Optimistic Concurrency、Unit/Integration Test、Redis、Logging/Observability、API Design、Docker、CI/CD。

MVP 核心 API 嚴格限定為四個：

1. Login
2. Schedule Query
3. Concurrent Booking
4. Cancel Appointment

---

## 2. Vertical Slice 順序

**[Decision]** — 這是對話中使用者唯一逐字確認的項目。

第一個 Walking Skeleton / Vertical Slice 選擇 **Booking（Appointment / Concurrent Booking）**，而非 Ping/Health Check 或 CreateDoctor。

理由摘要：
- MVP 明確列出的「Concurrent Booking」與學習目標中的「Optimistic Concurrency」是同一件事，是本專案最具面試展示價值、也是風險最高的部分，應優先驗證。
- Ping/Health Check 面試展示價值極低，CreateDoctor 是通用 CRUD，無法展示並發控制或 aggregate 設計判斷。
- 先攻克 Booking 可以及早把 Schedule/Slot/Appointment 的 aggregate 邊界定案，避免後續回頭重構。

**[Alternative]（已比較但不採用）**
- Ping / Health Check：僅驗證服務可啟動，不涉及 domain。
- CreateDoctor：驗證 CRUD + EF Core + Migration 管線，但不觸及並發或核心業務規則。

---

## 3. Domain Model

### 3.1 分類

**[Decision]**

| 概念 | 分類 |
|---|---|
| Doctor | Entity + Aggregate Root |
| Patient | Entity + Aggregate Root |
| ScheduleSlot | Entity + **Aggregate Root（核心）** |
| Appointment | Entity + Aggregate Root |
| Schedule（排班樣板） | MVP **不建模**，改用 seed 直接產生扁平 Slot |
| Booking | **不是 Entity**，是 Application 層的 Command（`BookAppointmentCommand`） |

### 3.2 Aggregate Boundary

**[Decision]**

- Aggregate 之間**只用 Id 互相參照**，不放 navigation property，避免 Domain 層被迫載入整個物件圖。
- 邊界：
  - `Doctor`（獨立 AR）
  - `Patient`（獨立 AR）
  - `ScheduleSlot`（AR）：`Id, DoctorId(Id ref), TimeSlot(VO: Start/End), Capacity, BookedCount, Status, Version`
  - `Appointment`（AR）：`Id, SlotId(Id ref), PatientId(Id ref), Status, BookedAtUtc, CancelledAtUtc`

### 3.3 Capacity / BookedCount 歸屬

**[Decision]**

`Capacity` 與 `BookedCount` 都放在 **`ScheduleSlot`** aggregate。

理由：
1. 要保護的 invariant `0 <= BookedCount <= Capacity` 是跨多筆 Appointment 的聚合條件，單一 Appointment 無法自行保證；DDD 原則是 aggregate 即 transactional consistency boundary，因此持有此 invariant 的物件必須是 aggregate root。
2. 若改用 `COUNT(appointments)` 即時計算，會產生 read-then-write 的多列競態（幻讀），需要 `SERIALIZABLE`/gap lock 等昂貴手段；把 invariant 收斂到單一列，可用便宜的單列樂觀鎖解決。
3. 代價：`BookedCount` 與實際 Appointment 數量存在 drift 風險，需靠「同一 transaction 內一起變動」+ DB `CHECK` constraint + 併發測試明確斷言兩者相等來緩解。

`AvailableCount = Capacity - BookedCount` 為 derived property，**不落地儲存**。

**[Alternative]（已比較但不採用，保留供未來重新評估）**

| 方案 | 內容 | 不採用原因 |
|---|---|---|
| A. Appointment 為唯一 AR，用 COUNT 即時計算 | 不 denormalize | 多列競態問題，是常見的天真做法 |
| C. Appointment 作為 ScheduleSlot 的子 Entity（同一 aggregate） | 一次 transaction 只碰一個 aggregate，DDD 上最「純」 | 每次 booking 需載入該 slot 全部 appointment；「查我的所有預約」需繞過 aggregate root |
| D. 引入跨 aggregate 的 `BookingService` domain service | | 沒必要，邏輯完全放得進 `ScheduleSlot.Book()` |

### 3.4 一次 Transaction 修改兩個 Aggregate

**[Decision]（刻意偏離嚴格 DDD 規則）**

一次 Booking 會在同一個 transaction 內同時修改 `ScheduleSlot`（+1）與新增 `Appointment`。嚴格 DDD 建議「一次 transaction 只改一個 aggregate」，但本專案刻意違反，理由：
- 兩者在同一個關聯式 DB，一次 `SaveChanges` 即 ACID transaction，成本為零。
- 用 domain event + outbox 做 eventual consistency 會留下「slot 已加 1 但 appointment 未建立」的可觀察錯誤狀態，在醫療情境不可接受。

### 3.5 Domain Invariants

**[Decision]**（需轉為 Unit Test，見 §8.2）

`ScheduleSlot`：
- `Capacity >= 1`；`TimeSlot.Start < TimeSlot.End`
- `0 <= BookedCount <= Capacity`（恆成立）
- `Book()`：要求 `Status == Open && Start > now && BookedCount < Capacity`，成功後 `BookedCount += 1`
- `Release()`：要求 `BookedCount > 0`，成功後 `BookedCount -= 1`
- `AvailableCount => Capacity - BookedCount`

`Appointment`：
- `Cancel()`：要求 `Status == Booked`；重複取消需拋例外，不可靜默成功

**[Decision]（2026-09-05 修訂）**：MVP 的 `Cancel()` **刻意不檢查取消時間窗**（例如「看診前 N 小時內不可取消」）。此前 §7.4 曾列出 `409 CANCELLATION_WINDOW_CLOSED` 但本節從未定義對應規則，屬文件不一致，已於本輪修正中從 §7.4 移除該狀態碼。若未來要做，見 §12 Deferred Decision（需注意：判斷時間窗需要 `Appointment` 取得對應 `ScheduleSlot` 的時間，屬跨 aggregate 讀取，非單純加一行判斷）。

`TimeSlot` 為 Value Object（**[Decision]（2026-09-07 修訂，見 §0 v1.2）**：EF Core Complex Type，非 owned type——見 §0 v1.2 說明）。

**[Decision]**：時間來源注入 **`TimeProvider`**（.NET 8+ BCL 型別），不直接呼叫 `DateTime.UtcNow`，測試用 `FakeTimeProvider`，避免測試隨真實時間漂移。

---

## 4. Clean Architecture

### 4.1 四層責任

**[Decision]**

```
MedConnect.Domain            ← 零外部依賴（僅 BCL）
MedConnect.Application       → 依賴 Domain
MedConnect.Infrastructure    → 依賴 Application, Domain
MedConnect.Api               → 依賴 Application（Infrastructure 僅限 Program.cs）
```

- **Domain**：Entities、Value Objects、Enums、Domain Exceptions。不知道 EF、HTTP、MediatR、JSON 的存在。
- **Application**：Repository/UnitOfWork/ICacheService 等介面定義、MediatR Command/Query/Handler、Validation、Pipeline Behaviors、DTO。定義「需要什麼」，不實作「怎麼做」。
- **Infrastructure**：`DbContext`、EF Configurations、Repository 實作、UnitOfWork、Migrations、JWT/Password Hasher 實作、Redis 實作、DI 註冊。
- **Api**：Controllers、Global Exception Handler、JWT Bearer 設定、`Program.cs`（composition root）。

### 4.2 Dependency Direction

**[Decision]**

```
              ┌─────────────────────┐
              │        Api          │
              └──────┬───────┬──────┘
                     │       │ (僅 Program.cs / DI 註冊)
                     │       ▼
                     │  ┌─────────────────┐
                     │  │ Infrastructure  │
                     │  └────────┬────────┘
                     ▼           ▼
              ┌─────────────────────┐
              │     Application     │
              └──────────┬──────────┘
                         ▼
              ┌─────────────────────┐
              │       Domain        │
              └─────────────────────┘
```

| | 可以依賴 | 絕對不可以依賴 |
|---|---|---|
| Domain | 無（僅 BCL） | Application / Infrastructure / Api / EF Core / MediatR / 任何 NuGet |
| Application | Domain | Infrastructure / Api / EF Core / MySQL driver / Redis client / ASP.NET Core |
| Infrastructure | Application, Domain | Api |
| Api | Application（+Infrastructure 僅 DI） | 直接使用 `DbContext` 或 EF 型別 |

**[Decision]**：用 **NetArchTest** 把上述依賴規則寫成自動化 Architecture Test。

**[Deferred Decision]**：Application 是否允許依賴 `Microsoft.EntityFrameworkCore.Abstractions`（讓 query handler 直接用 `IQueryable`）——討論中提出這是「務實但需主動聲明的選擇」，尚未定案，目前預設走 `IReadDbContext` 介面隔離的嚴格版本。

### 4.3 元件歸屬

**[Decision]**

| 元件 | 歸屬 |
|---|---|
| EF Core 套件、`DbContext`、`IEntityTypeConfiguration<>`、Migrations | Infrastructure |
| MediatR Handler | Application（Handler 即 use case） |
| `IPipelineBehavior`（Logging/Validation/Performance） | Application |
| Repository 介面 | Application `/Abstractions` |
| Repository 實作 | Infrastructure |
| JWT 產生（`ITokenService` 實作） | Infrastructure（介面在 Application） |
| JWT 驗證 middleware | Api |
| Password Hasher 實作 | Infrastructure（介面在 Application） |
| Redis 實作（`ICacheService` 實作） | Infrastructure（介面在 Application） |
| DTO / ViewModel | Application，與對應 Command/Query 同資料夾（vertical slice 風格） |
| Domain Exception | Domain |
| HTTP status 對映 | Api（Global Exception Handler） |

**[Decision]**：Domain entity **不使用 EF Attribute**，一律用 Fluent API（`IEntityTypeConfiguration<>`）避免 Domain 依賴 EF。

### 4.4 Controller 責任

**[Decision]**

Controller 只做四件事：
1. Model binding
2. 組出 Command/Query（**`PatientId` 一律從 `ClaimsPrincipal` 取，不從 request body 取**）
3. `await _sender.Send(command)`
4. 轉譯為 HTTP response（status code、`Location` header）

不做：驗證邏輯、business rule、直接操作 `DbContext`、try/catch 業務例外。

---

## 5. Booking Flow

### 5.1 完整流程

**[Decision]**

```
① HTTP POST /api/v1/appointments  (Authorization: Bearer <jwt>, { "slotId": 42 })
② [Api] JWT Bearer middleware 驗證 → 失敗回 401
③ [Api] Controller：patientId 取自 token → 組 BookAppointmentCommand → Send
④ [Application] MediatR pipeline：LoggingBehavior → ValidationBehavior → PerformanceBehavior
⑤ [Application] Handler：載入 slot（tracked）；不存在 → 404
⑥ [Domain] slot.Book(now)：檢查 Status/Start/BookedCount，通過則 BookedCount++（僅記憶體內）
⑦ [Application] 建立 Appointment，加入 repository
⑧ [Application] await _unitOfWork.SaveChangesAsync()  ← commit point
⑨ [Infrastructure] Interceptor 將 slot.Version 的 CurrentValue = OriginalValue + 1
⑩ [MySQL] UPDATE schedule_slots SET booked_count=?, version=? WHERE id=? AND version=?；
           INSERT appointments (...)；於同一隱式 transaction 內 COMMIT
⑪ 若 UPDATE 影響列數為 0 → DbUpdateConcurrencyException → 轉譯為 ConcurrencyConflictException
⑫ [Api] GlobalExceptionHandler → ProblemDetails；成功回 201 + Location
```

### 5.2 四個關鍵問題

**[Decision]**

- **Transaction 位置**：`SaveChangesAsync()` 的隱式 transaction（EF Core 在單次 SaveChanges 發出多條寫入語句時自動包 transaction）。MVP **不**顯式呼叫 `BeginTransaction()`，也**不**做成 MediatR pipeline 尾端自動 commit（避免持鎖時間拉長、避免「看不見的 commit」造成除錯困難）。commit 由 Handler 顯式呼叫。
- **Concurrency Token 位置**：`schedule_slots.version`。只有 `ScheduleSlot` 需要 token（Appointment 的 INSERT 靠唯一索引擋重複，不靠 version；Cancel 流程另涉及 `appointments.version`）。
- **可能發生 conflict 的步驟**：步驟 ⑩ 的 `UPDATE ... WHERE version = ?`。步驟 ⑥ 的 domain 檢查基於過期快照，**不是**真正的併發守衛；真正守衛在 DB 層的 `WHERE` 條件。第二個潛在衝突點：⑩ 的 INSERT 撞到唯一索引（同病人重複預約）。
- **Conflict 回應**：一律 **`409 Conflict`**（`application/problem+json`），用 `errorCode` 區分：
  - `CONCURRENCY_CONFLICT`（version 不符，可重試）
  - `SLOT_FULL`（真的滿了，不可重試）
  - `DUPLICATE_BOOKING`（同病人重複預約同一 slot）
  - 理由：409 語意精準對應「request 合法但與目標資源當前狀態衝突」；非 400（request 本身無語法/語意錯誤）、非 500（這是預期內的正常事件，非 bug）、非 503/429（語意不符且會誘導不當重試）。**422 被視為「可辯護的替代」但未採用**。

**[Alternative]（已比較但不採用）**
- 用 `422 Unprocessable Content` 取代部分 409 情境——語意上可辯護，但為求客戶端處理一致性，選擇統一用 409 + errorCode。

**[Decision]（2026-09-05 新增）**：Booking 流程有兩種完全不同來源的衝突，**不可用同一個 catch 區塊處理**，Infrastructure 層需明確分流：

| .NET Exception | 觸發來源 | 對應 errorCode |
|---|---|---|
| `DbUpdateConcurrencyException` | `schedule_slots` 的 `UPDATE ... WHERE version = ?` 影響列數為 0 | `CONCURRENCY_CONFLICT`（409，可重試） |
| `DbUpdateException`（inner exception 為 MySQL duplicate entry，違反的 key 是 `ux_appt_slot_active_patient`） | `appointments` INSERT 撞到 partial-unique index（同病人重複預約同一 slot） | `DUPLICATE_BOOKING`（409，不可重試） |
| `InvalidOperationException`（**注意型別，非 `DbUpdateException`**；inner exception 鏈為 `DbUpdateException` → `MySqlException{ErrorCode=LockDeadlock}`，見 §0 v1.5） | InnoDB 偵測到鎖的循環等待，主動犧牲其中一個交易（MySQL error 1213） | `TRANSIENT_CONFLICT`（409，可重試） |

判斷 `DbUpdateException` 具體違反哪一個 unique key，需要解析 inner `MySqlException` 的錯誤訊息或 constraint 名稱；這段解析邏輯應封裝在 Infrastructure 層（例如一個 `MySqlExceptionTranslator`），Application/Api 層不可直接對例外訊息做字串比對。

> **[Decision — 新增，2026-09-21，見 §0 v1.5]** 第三列的 `InvalidOperationException` 型別是實測結果，不是預期或文件推導——死結原本預期會跟第二列一樣是 `DbUpdateException`，但因為 Booking 是在顯式交易（`ExecuteInTransactionAsync`，見 §0 v1.4）裡呼叫 `SaveChangesAsync`，EF Core 的 `ExecutionStrategy` 偵測到例外看似 transient（死結在 `ShouldRetryOn` 清單內）、又處於使用者自管的交易中且未設定 `EnableRetryOnFailure()`，判定無法安全地自動重試，於是把原始例外包成 `InvalidOperationException` 往外丟。用一組與 booking 無關、刻意設計成兩個 slot 相反鎖定順序的對照實驗重現 5 次真實死結，5 次的例外形狀完全一致。這代表：若沿用「只攔 `DbUpdateException`」的直覺寫法，這個 catch 永遠不會命中，死結會繼續以未對映例外外洩成 500——即使成因（S→X 鎖升級）已在 v1.4 修掉，往後任何新的死結情境都不會被這裡接住。

### 5.3 Cancel Flow

**[Decision]**

```
載入 appointment（含 version）
若不存在或不屬於當前使用者 → 404
appt.Cancel(now)：已取消則拋例外 → 409 (ALREADY_CANCELLED)
載入 slot(appt.SlotId)
slot.Release()：BookedCount--，不可低於 0
SaveChanges()：兩列（appointment、slot）各自做 version check
```

需驗證：取消後名額可能立刻被別人搶走（正確行為）；`BookedCount` 不可變負、不可 double-release。

---

## 6. Database Design

### 6.1 Schema

**[Decision]**（MySQL 8.0+, InnoDB, utf8mb4）

五張表：`users`、`doctors`、`patients`、`schedule_slots`、`appointments`。

```sql
CREATE TABLE users (
  id            BIGINT       NOT NULL AUTO_INCREMENT,
  email         VARCHAR(256) NOT NULL,
  password_hash VARCHAR(256) NOT NULL,
  role          TINYINT      NOT NULL,        -- 0=Patient, 1=Admin
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME(6)  NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
```

**[Decision]（2026-09-05 修訂）**：原本 `users` 表有一個 `version INT NOT NULL DEFAULT 0` 欄位，已移除。原因：MVP 沒有任何併發修改 `User` 的場景（Login 是唯讀查詢，沒有 Update User API），保留它說不出解決什麼問題，違反 §9.1/§11 的過度設計防線。若未來新增帳號停用、角色變更等會寫入 `users` 的功能且需要併發保護，屆時再加回。

**[Decision]（2026-09-05 修訂）**：`role` 欄位本身保留，因為它有實際消費者——§7.3 Concurrent Booking 明確要求 `role: Patient` 做 authorization 檢查，JWT claim 也含 `role`（§7.1）。但 `Admin`（值 1）目前在 MVP 四支 API 中**沒有任何 endpoint 會檢查它**，純屬保留欄位，已列入 §12 Deferred Decision——實作時不可假設 Admin 已有任何實際行為。

```sql
CREATE TABLE doctors (
  id         BIGINT       NOT NULL AUTO_INCREMENT,
  full_name  VARCHAR(128) NOT NULL,
  specialty  VARCHAR(64)  NOT NULL,
  created_at DATETIME(6)  NOT NULL,
  PRIMARY KEY (id),
  KEY ix_doctors_specialty (specialty)
) ENGINE=InnoDB;

CREATE TABLE patients (
  id         BIGINT       NOT NULL AUTO_INCREMENT,
  user_id    BIGINT       NOT NULL,
  full_name  VARCHAR(128) NOT NULL,
  created_at DATETIME(6)  NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_patients_user (user_id),
  CONSTRAINT fk_patients_user FOREIGN KEY (user_id) REFERENCES users(id)
) ENGINE=InnoDB;

CREATE TABLE schedule_slots (
  id             BIGINT      NOT NULL AUTO_INCREMENT,
  doctor_id      BIGINT      NOT NULL,
  start_time_utc DATETIME(6) NOT NULL,
  end_time_utc   DATETIME(6) NOT NULL,
  capacity       INT         NOT NULL,
  booked_count   INT         NOT NULL DEFAULT 0,
  status         TINYINT     NOT NULL DEFAULT 0,   -- 0=Open, 1=Closed
  version        INT         NOT NULL DEFAULT 0,   -- concurrency token
  created_at     DATETIME(6) NOT NULL,
  updated_at     DATETIME(6) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_slot_doctor_start (doctor_id, start_time_utc),
  KEY ix_slot_doctor_time (doctor_id, start_time_utc, status),
  CONSTRAINT fk_slot_doctor FOREIGN KEY (doctor_id) REFERENCES doctors(id),
  CONSTRAINT ck_slot_capacity   CHECK (capacity >= 1),
  CONSTRAINT ck_slot_booked     CHECK (booked_count >= 0 AND booked_count <= capacity),
  CONSTRAINT ck_slot_time_order CHECK (start_time_utc < end_time_utc)
) ENGINE=InnoDB;
```

**[Decision]（2026-09-05 修訂 — 僅補充說明，無結構變更）**：`ux_slot_doctor_start (doctor_id, start_time_utc)` 與 `ix_slot_doctor_time (doctor_id, start_time_utc, status)` 前綴重疊並非疏漏，兩者刻意都保留：前者是**業務唯一性約束**（同一醫生同一時段不可有兩個 slot，屬資料完整性），後者是為 Schedule Query（§7.2：依 `doctor_id` + 時間範圍 + `status=Open` 過濾）服務的 **covering index**，兩者目的不同、不能互相取代。多維護一個索引對寫入的額外成本可忽略（`status` 極少變動，`booked_count` 更新也不涉及這兩個索引鍵值）。

```sql
CREATE TABLE appointments (
  id               BIGINT      NOT NULL AUTO_INCREMENT,
  slot_id          BIGINT      NOT NULL,
  patient_id       BIGINT      NOT NULL,
  status           TINYINT     NOT NULL,           -- 0=Booked, 1=Cancelled
  booked_at_utc    DATETIME(6) NOT NULL,
  cancelled_at_utc DATETIME(6) NULL,
  version          INT         NOT NULL DEFAULT 0, -- 防雙重取消
  PRIMARY KEY (id),
  KEY ix_appt_slot (slot_id),
  KEY ix_appt_patient_status (patient_id, status, booked_at_utc),
  CONSTRAINT fk_appt_slot    FOREIGN KEY (slot_id)    REFERENCES schedule_slots(id),
  CONSTRAINT fk_appt_patient FOREIGN KEY (patient_id) REFERENCES patients(id)
) ENGINE=InnoDB;

-- MySQL 無 partial/filtered index，用 generated column 模擬「同一 slot 同一病人僅一筆有效預約」
ALTER TABLE appointments
  ADD COLUMN active_patient_id BIGINT
      GENERATED ALWAYS AS (IF(status = 0, patient_id, NULL)) STORED,
  ADD UNIQUE KEY ux_appt_slot_active_patient (slot_id, active_patient_id);
```

### 6.2 MySQL 下的 Optimistic Concurrency

**[Decision]**：**不使用 SQL Server 的 `rowversion`/`[Timestamp]` 概念**（MySQL 無此型別）。改用**應用程式維護的整數 `version` 欄位**，搭配 EF Core `IsConcurrencyToken()` + 自訂 Interceptor，在 `SaveChanges` 前把 `CurrentValue` 設為 `OriginalValue + 1`，使 EF 產生的 `UPDATE ... SET version=? WHERE id=? AND version=?` 能正確運作。

**[Alternative]（已比較但不採用）**
- `TIMESTAMP ON UPDATE CURRENT_TIMESTAMP`：精度問題（需 `TIMESTAMP(6)`）、同微秒內更新仍可能撞、2038 年上限、隱式時區轉換。
- Pomelo 的 `.IsRowVersion()`：行為隨版本不明確，被列為需要實測而非直接信任的項目（見 §12）。
- 條件式原子 UPDATE（`UPDATE ... SET booked_count = booked_count + 1 WHERE booked_count < capacity`）：效能最佳、無衝突、無需 retry，但繞過 domain model 與樂觀鎖機制，不符合本專案展示 Optimistic Concurrency 的學習目標，**MVP 不採用為主要路徑，但作為「高競爭情境替代方案」的知識點保留**。

**[Decision]**：DB 層 `CHECK` constraint 作為 defence in depth，非主要機制。

### 6.3 其他 DB 決策

**[Decision]**
- 時間欄位一律 `DATETIME(6)`、存 UTC，不用 `TIMESTAMP`（隱式時區轉換、2038 上限問題）。
- PK 用 `BIGINT AUTO_INCREMENT`（InnoDB clustered index 插入局部性與索引體積考量）。
- 不做 soft delete；Cancel 是狀態轉換，非刪除（保留稽核軌跡）。
- Enum 存 `TINYINT`。

**[Deferred Decision]**：id 可枚舉的資訊洩漏疑慮——若未來需要，可加對外 `public_id`（ULID/GUIDv7），MVP 階段先不做，僅記錄為已知取捨。

---

## 7. API Design

**[Decision]**：Base path `/api/v1`；錯誤一律回 `application/problem+json`（含 `errorCode`、`traceId`）；時間欄位一律 UTC ISO-8601；401 = 無/無效 token，403 = 有 token 但角色不足。

### 7.1 Login

**[Decision]**

| | |
|---|---|
| Method / Route | `POST /api/v1/auth/login`（Anonymous） |

Request: `{ "email": "...", "password": "..." }`
Response 200: `{ "accessToken": "...", "tokenType": "Bearer", "expiresIn": 3600 }`

Status: `200` 成功／`400 VALIDATION_FAILED`／`401 INVALID_CREDENTIALS`（帳號不存在與密碼錯誤回相同訊息，防 user enumeration）／`403 ACCOUNT_DISABLED`（可選）。

**[Decision]**：MVP **不做 refresh token**，JWT claims 至少含 `sub`、`patient_id`、`role`、`jti`、`exp`。此為已知限制，非疏漏。

### 7.2 Schedule Query

**[Decision]**

| | |
|---|---|
| Method / Route | `GET /api/v1/doctors/{doctorId}/slots?from=...&to=...`（Anonymous） |

Response 200: `PagedResult<SlotDto>`，`SlotDto` 含 `slotId, doctorId, doctorName, startTimeUtc, endTimeUtc, capacity, bookedCount, availableCount`（`availableCount` 為 derived）。

Status: `200`（無資料回空陣列，非 404）／`400 VALIDATION_FAILED`（日期範圍錯誤或超過上限）／`404 DOCTOR_NOT_FOUND`。

**[Decision]**：API 合約需明確標示 `availableCount` 為「時間點提示，非保證」，客戶端必須能處理後續 booking 回 409（尤其在引入 Redis 快取後更明顯）。

**[Decision]**：此查詢用 projection 直接投影 DTO + `.AsNoTracking()`，不經過 domain entity 載入。

### 7.3 Concurrent Booking

**[Decision]**

| | |
|---|---|
| Method / Route | `POST /api/v1/appointments`（Required Auth, role: Patient） |

Request: `{ "slotId": 42 }`（**無 `patientId` 欄位，一律取自 JWT**）
Response 201: `{ appointmentId, slotId, patientId, status, bookedAtUtc }` + `Location` header

Status: `201`／`400 VALIDATION_FAILED`／`401`／`404 SLOT_NOT_FOUND`／`409 SLOT_FULL`（不可重試）／`409 CONCURRENCY_CONFLICT`（可重試）／`409 TRANSIENT_CONFLICT`（可重試，見 §0 v1.5）／`409 DUPLICATE_BOOKING`／`409 SLOT_NOT_BOOKABLE`。

**[Decision]**：MVP 不做完整 `Idempotency-Key` 機制，靠 `(slot_id, active_patient_id)` 唯一索引提供天然的部分冪等性（同病人重送只會拿到 409 DUPLICATE_BOOKING，不會產生兩筆）。完整 Idempotency-Key + Redis 記錄回應列為後續擴充（見 §12）。

### 7.4 Cancel Appointment

**[Decision]**

| | |
|---|---|
| Method / Route | `POST /api/v1/appointments/{id}/cancel`（Required Auth，僅本人） |

Request（可選）: `{ "reason": "..." }`
Response 200: `{ appointmentId, status, cancelledAtUtc }`

Status: `200`／`400`／`401`／`404 APPOINTMENT_NOT_FOUND`（不存在**或不屬於當前使用者，兩者合併回應**，避免洩漏他人資料存在性）／`409 ALREADY_CANCELLED`／`409 CONCURRENCY_CONFLICT`。

**[Decision]（2026-09-05 修訂）**：原本此處列出的 `409 CANCELLATION_WINDOW_CLOSED` 已移除。原因：§3.5 從未定義取消時間窗這條 Domain Invariant，此狀態碼等於 API 合約承諾了一個 Domain 層不存在的行為——這是一次 Review 發現的文件內部不一致。MVP 決定不做此功能，改列入 §12 Deferred Decision；若未來要做，需先在 §3.5 補上規則並解決跨 aggregate 讀取 slot 時間的設計問題，不可只在 Api 層加狀態碼。

**[Decision]**：用 `POST .../cancel` 而非 `DELETE`，因 Cancel 是狀態轉換而非資源刪除（醫療情境需保留稽核軌跡）。這是刻意的 RPC-over-REST 選擇，非純 REST。

**[Alternative]（已比較但不採用）**：`PATCH /appointments/{id}` + `{ "status": "Cancelled" }`——更符合純 REST，但把合法狀態轉換規則的判斷推給客戶端，且不利於未來擴充取消理由/通知等副作用。

---

## 8. Testing Strategy

### 8.1 測試金字塔

**[Decision]**

```
Concurrent Booking Test（1–3個，真 MySQL + 真 HTTP，招牌）
Integration Test（~12–15個，Testcontainers MySQL + WebApplicationFactory）
Domain Unit Test（~25個，無 I/O）
```

工具：xUnit + FluentAssertions + NSubstitute（少量 mock）+ Testcontainers.MySql + Respawn + `Microsoft.Extensions.TimeProvider.Testing`。

### 8.2 Domain Unit Test

**[Decision]**：涵蓋 `ScheduleSlot.Book/Release`、邊界值（滿載、關閉、過去時間）、`Appointment.Cancel`（含重複取消）。斷言需精確（例如「恰好 +1」而非「有變大」）。

### 8.3 Application Handler Unit Test

**[Decision]**：僅測編排（載入失敗處理、成功時是否呼叫一次 SaveChanges、domain 拋例外時不應呼叫 SaveChanges），**不重複測試 domain 規則**。

### 8.4 Integration Test

**[Decision]**：**必須使用 Testcontainers 起真 MySQL，絕對不使用 EF Core InMemory Provider**，因為 InMemory 不強制 concurrency token、unique index、FK、CHECK constraint，也非真正關聯式，樂觀鎖測試會「全部通過但毫無意義」。

涵蓋 Auth、Schedule Query、Booking、Cancel、Migration 可清潔套用、Architecture Test（NetArchTest）等場景（詳見先前討論中列出的測試清單）。

### 8.5 Concurrent Booking Test（招牌測試）

**[Decision]**

必須雙向斷言，不能只驗證 `BookedCount <= Capacity`（單向斷言會被「拒絕所有人」的壞實作通過）：

1. `BookedCount <= Capacity`
2. **`BookedCount == min(N, Capacity)`**（例如 capacity=5、50 個不同病人併發 → 必須恰為 5）
3. `COUNT(active appointments) == BookedCount`（兩表一致，無 drift）
4. `201` 數量 == 成功預期數；`409` 數量 == 失敗預期數
5. **回應中不可出現任何 5xx**
6. 成功者的 `patientId` 互不重複
7. 每個 409 回應帶正確的 `errorCode`

> **[Decision — 適用範圍修正，2026-09-18，見 §0 v1.4]**
>
> 上方清單**原文保留，未刪除**。經 Phase 1c 實測，其中 **第 2 條與第 4 條僅適用於「已啟用 retry」的版本（v2）**，在本文件同時要求的「第一版不啟用 retry」前提下**不成立**，且與實作是否正確無關：50 個請求同時讀到同一個 `version`，CAS 每輪至多一個贏家，填滿 5 個名額必須跑 5 輪 = retry。實測（5 輪）成功數落在 2～3 之間且本質非確定性。
>
> **v1（無 retry）採用的斷言**：
> 1. `BookedCount <= Capacity`（不超賣）
> 2. 成功數 `>= 1`（不因過度保守而全數拒絕）且 `<= Capacity`
> 3. `BookedCount == 實際回報的成功數`（回應與 DB 一致）
> 4. `COUNT(active appointments) == BookedCount`（兩表無 drift）
> 5. 無任何非預期例外外洩（1c 直接呼叫 Handler，不經 HTTP，故第 5 條的「不可出現 5xx」在此層轉化為此條）
> 6. 成功者的 `patientId` 互不重複
> 7. 每個失敗都帶 `SLOT_FULL` 或 `CONCURRENCY_CONFLICT`，不得有第三種
>
> 第 1、3、5、6、7 條（原編號）不受此修正影響。v2 加入 retry 後，第 2、4 條原文即恢復適用，屆時應直接斷言 `== min(N, Capacity)`。
>
> **v1 實際保證是什麼（一句話版本）**：不超賣、資料庫狀態內部自洽（`schedule_slots.booked_count` 與 `appointments` 表中 `Status == Booked` 的筆數永遠一致，無論最終賣出幾個）、無任何非預期例外外洩；**但不保證吞吐量精確等於 Capacity**——同一瞬間爆發的 N 個請求，v1 只保證其中有人贏、贏家互不衝突、輸家全部乾淨失敗，不保證贏家數量湊滿 Capacity。
>
> **v2 待辦**：若之後加入 bounded retry（見 §12 第 7 項，已升為最高優先），屆時應回來更新本節，把斷言重新收緊為精確 `== min(N, Capacity)`。

實作要點：
- 用 50 個**不同病人**（同一人會先被唯一索引擋下，測不到樂觀鎖）
- 用 `TaskCompletionSource`/Barrier 讓所有請求同一瞬間釋放，避免被排程成近似循序
- 驗證讀取須用**全新的 DI scope / connection**（MySQL 預設 `REPEATABLE READ`，用原本連線重讀可能拿到快照）
- `DbContext` 必須是 Scoped（ASP.NET Core 預設），不可誤設為 Singleton
- 第一版**不啟用 retry**，需看到乾淨的「N 成功 / 其餘 409」

**[Decision]**：需一併撰寫的變體測試：
- 低競爭情境（capacity 遠大於併發數）全部成功，防過度保守實作
- 跨兩個獨立 slot 的併發互不影響，驗證鎖粒度為 per-slot
- 併發 Cancel（同一 appointment）恰好一個成功
- Book/Cancel 混合負載，跑多輪增加抓到競態的機率

**[Decision]（2026-09-05 新增，回應 §12 第 13 項）**：專門驗證「無 partial commit」的情境——刻意製造 `schedule_slots` 的 UPDATE 因版本衝突失敗（例如把 capacity 設為剛好被搶完），直接以獨立連線查 DB 確認對應的 `appointments` INSERT **完全沒有落地**，而不是只看 HTTP 回應為 409。這正是上面第 3 條 `COUNT(active appointments) == BookedCount` 斷言背後真正要驗證的因果關係（兩表無 drift 的前提就是「衝突時不會有一半寫入」）；本條把這個因果關係明確寫出，避免被誤解為只是防呆用的巧合斷言。

### 8.6 CI 考量

**[Decision]**：Testcontainers 需要 Docker daemon；GitHub Actions `ubuntu-latest` runner 內建 Docker 可直接用；建議固定 MySQL image tag 版本以避免 flaky。

---

## 9. Trade-off 決策紀錄

以下每項先前以「比較 + 建議」形式討論，本節統一標記最終立場。

### 9.1 Clean Architecture vs 簡單分層

**[Decision]**：採用 4 層 Clean Architecture，但保持薄；不加無法辯護的抽象。

**[Decision]**：承認對 4-endpoint 規模而言這客觀上是過度設計，選擇它是因為（a）專案目的即展示對它的理解，（b）此 domain 有真實 invariant（capacity），Domain 層不是空殼。

### 9.2 MediatR / CQRS 是否必要

**[Decision]**：採用 MediatR。價值在於 `IPipelineBehavior` 讓 cross-cutting concern 統一套用到所有 use case。

**[Decision]**：CQRS 的定義範圍明確限定為「Command 與 Query 用不同模型與路徑」，**不是** event sourcing、**不是**讀寫分離資料庫、**不是** eventual consistency。

**[Deferred Decision]**：MediatR 授權模式（近年轉為商業授權，個人/OSS 免費、商業使用有營收門檻）需要在實作前確認目前條款；或評估改用 source-generator 版替代品（如 `Mediator`）、Wolverine、或 DI + Scrutor decorator。列入 §12。

### 9.3 Repository Pattern 是否需要

**[Decision]**：拒絕 generic `IRepository<T>`。採用不對稱設計：
- 寫入（Command）：少量、意圖明確的 aggregate repository（例如 `ISlotRepository.GetForBookingAsync`）
- 讀取（Query）：Query handler 直接透過 `IReadDbContext` 做 projection，不經 repository

`IUnitOfWork` 保留一個薄介面（`SaveChangesAsync`），目的是提供明確 commit point，而非抽象化資料庫。

### 9.4 Optimistic vs Pessimistic Concurrency

**[Decision]**：採用 Optimistic Concurrency（見 §6.2）。理由：門診預約情境競爭度中低、不持鎖、可觀測性好（衝突率可做 metric）、是本專案明確學習目標、DB `CHECK` 提供 defence in depth。

**[Alternative]（已比較但不採用，需知道失效邊界）**
- Pessimistic（`SELECT ... FOR UPDATE`）：適合高競爭，但持鎖時間長、有死鎖/lock wait 風險。
- 條件式原子 UPDATE：極高競爭（如演唱會搶票）下的更優方案，但繞過 domain model，不符合本專案學習目標，MVP 不採用。

### 9.5 Retry 是否應該使用

**[Decision]**：MVP 第一版**不加 retry**，理由是 retry 會掩蓋樂觀鎖實作的潛在 bug，需先看到乾淨的「成功/衝突」比例。

**[Deferred Decision]**：Retry 機制列為後續獨立階段加入，需遵守：只重試 `ConcurrencyConflictException`（絕不重試 `SlotFullException`）、必須開新 DI scope/DbContext 重新讀取狀態、放在最外層（非 MediatR behavior 內以避免同一 scoped DbContext 重放）、上限 2–3 次 + jitter。列入 §12/§10（第二階段）。

### 9.6 Redis 是否應加入 Booking Transaction

**[Decision]**：**明確不加入**。Redis 不在 DB transaction 內，dual write 會造成不一致；不應把醫療 invariant 的正確性交給快取；分散式鎖（Redlock）是效能最佳化手段，不是正確性原語。

**[Decision]**：Redis 在 MVP 階段僅用於 **Schedule Query 的讀取快取**（短 TTL 30–60 秒，Booking/Cancel 成功後主動失效相關 key）。

**[Deferred Decision]**：Redis 用於 login rate limiting、JWT denylist、Idempotency-Key 記錄等用途，列為後續擴充，不在 MVP 範圍。

### 9.7 JWT 無狀態 vs 可撤銷

**[Decision]**：MVP 採用短 TTL 的純 JWT，**不做撤銷機制**，此為已知限制並需明確記錄（而非視為疏漏）。

**[Deferred Decision]**：撤銷機制（refresh token / Redis denylist / token_version claim）留待後續評估，尚未選定方案。

### 9.8 Exception vs Result Pattern

**[Decision]**：MVP 使用 domain exception（如 `SlotFullException`）+ 全域例外處理器對映 HTTP status，理由是程式碼較乾淨、不易忘記處理。

**[Deferred Decision]**：若未來有極高吞吐路徑，`Result<T>`/OneOf 模式可能更合適，本階段不採用，僅記錄為已知的替代方案。

---

## 10. 實作分期

**[Decision]**

```
1a  骨架          4 專案 + DI + Program.cs + docker-compose(MySQL) + health check
                  → 目標：dotnet run 能起來
1b  Domain + DB   ScheduleSlot/Appointment entity + invariant + Domain unit test
                  + EF 設定 + version interceptor + migration + seed
                  → 目標：Domain unit test 全部綠燈
1c  併發驗證      BookAppointment command/handler + POST endpoint（先不做 JWT，
                  用固定 patientId）+ Testcontainers + 併發測試
                  → 目標：併發測試全部斷言綠燈（本專案最重要的里程碑）
1d  補齊 MVP      Login + JWT + Schedule Query + Cancel + 全域例外處理
                  + 其餘 integration test
1e  加值          Redis 讀取快取 + Serilog/OpenTelemetry + Dockerfile
                  + GitHub Actions CI + ArchitectureTests + README
```

**[Decision]**：1c（併發測試綠燈）在 1d 之前完成；JWT 刻意延後到 1d，因為認證是橫切關注點，不會影響 domain model 設計。

---

## 11. Risks

**[Decision]**（風險清單本身是已達成共識的紀錄，緩解手段部分涉及 §12 的待驗證項目）

| 風險 | 影響 | 緩解 |
|---|---|---|
| Pomelo/MySQL 的 `UseAffectedRows` 與 `IsConcurrencyToken` 實際行為未經驗證 | 樂觀鎖可能靜默失效或誤判，核心賣點失守 | 併發測試作為唯一驗證閘門，在其綠燈前不進行後續功能 |
| EF InMemory 的誘惑 | 併發測試假通過 | 從一開始就用 Testcontainers |
| 併發測試不夠「真併發」 | 測試綠燈但未真正競爭過 | 用 barrier 同時放閘；驗證讀取用新 scope；跑多輪 |
| MySQL `REPEATABLE READ` 的斷言陷阱 | 測試中重讀拿到快照，斷言結果失真 | 最終狀態驗證一律用獨立 connection |
| `BookedCount` 與 `COUNT(appointments)` drift | 資料不一致且不易察覺 | 同 transaction 變動 + CHECK constraint + 測試明確斷言兩者相等 |
| Cancel 路徑被忽略 | 名額洩漏或 BookedCount 變負 | 從第一天納入 cancel 併發測試 |
| 範圍膨脹（一次上齊 MediatR+Redis+JWT+OTel） | 卡在基礎設施，核心價值做不完 | 依 §10 分期，Redis 與 observability 排在併發測試綠燈之後 |
| MediatR 商業授權疑慮 | 若日後商業使用有法務問題 | 實作前確認版本/條款，見 §12 |
| Testcontainers 需要 Docker | CI 或本機環境跑不起來 | GitHub Actions ubuntu-latest 內建 Docker；固定 image tag |
| 時區/DateTime 處理 | 高頻隱性 bug 來源 | 全程 UTC + DATETIME(6) + 注入 TimeProvider |
| JWT 不可撤銷 | 停權使用者仍可操作 | 短 TTL；README 明確記錄為已知取捨 |
| 過度抽象 | 專案變成「展示 pattern」而非「展示判斷力」 | 每個抽象需能一句話說明解決什麼問題，說不出來就刪 |

---

## 12. Deferred Decisions / 必須實測驗證清單

以下項目**尚未決定**，或雖有預設方向但**必須先實測驗證**才能視為定案，實作時不應假設它們已成立：

1. ~~**[Deferred Decision — 最高優先]** Pomelo 的 `IsConcurrencyToken()` + MySQL 受影響列數行為，特別是連線字串 `UseAffectedRows` 設定對 EF 併發偵測的實際影響。~~ **[驗證通過，見 §0 v1.4]** 1c 用 Testcontainers 真實 MySQL 8.0.39 實測：`IsConcurrencyToken()` 確實產生 `UPDATE ... WHERE id = ? AND version = ?`，衝突時正確拋出 `DbUpdateConcurrencyException` 並被 `UnitOfWork` 轉譯為 `ConcurrencyConflictException`（預設連線字串下無須額外設定 `UseAffectedRows`）。反向驗證亦完成：以 mutation testing 拿掉 `.IsConcurrencyToken()` 後，50 個併發請求**全數成功**造成超賣，招牌測試如預期紅燈——證明這道防線確實是唯一擋住超賣的機制，而非巧合。
2. ~~**[Deferred Decision]** 實際使用的 MySQL 版本是否 ≥ 8.0.16（影響 `CHECK` constraint 是否真的被強制執行）。~~ **[驗證通過，見 §0 v1.2]** 1b 用 Docker MySQL 8.0.39 實測，`CHECK` constraint 確認生效（違規 INSERT 被擋下並回傳 Error 3819）。
3. ~~**[Deferred Decision]** Pomelo 是否支援 `HasComputedColumnSql(..., stored: true)` 映射 generated column，以及 migration 產出是否正確。~~ **[驗證通過，並發現新限制，見 §0 v1.2]** 1b 已用真實 MySQL 驗證 generated column 行為正確；另發現「FK 若被 STORED generated column 依賴的欄位參照，不可用 `ON DELETE CASCADE`」的 MySQL 限制，非本項原始問題範圍但一併記錄。
4. **[Deferred Decision]** `DateTime`/`DateTimeOffset` 在 Pomelo 上對 `DATETIME(6)` 的實際映射與往返精度。
5. ~~**[Deferred Decision]** MediatR 目前版本的授權條款細節，以及是否改用替代方案（`Mediator`、Wolverine、DI+Scrutor）。~~ **[已決策，見 §0 v1.3]** 釘住 `MediatR` v12.5.0（Apache 2.0，最後一版免費授權），不採用 v13+ 商業版，不改用替代方案。
6. ~~**[Deferred Decision]** `WebApplicationFactory` + `Task.WhenAll` 是否能產生真正的並行請求（而非被排程成近似循序）——需在撰寫併發測試時驗證。~~ **[部分驗證，見 §0 v1.4]** 1c 尚未經過 HTTP／`WebApplicationFactory`（直接以 `IServiceScopeFactory` + `IMediator` 呼叫 Handler），但 `Task.Run` + `TaskCompletionSource` 閘門確認能產生**真正的並行**：未修正死結前 50 個請求中有 47 個互相死結，這種程度的鎖競爭不可能由近似循序的排程產生。`WebApplicationFactory` 那一層仍待 1d 驗證。
7. **[Deferred Decision — 2026-09-18 升為最高優先，見 §0 v1.4]** Retry 機制的具體實作方式與導入時機（§9.5）。1c 實測確立了這不再只是「加值項目」——**§8.5 的 `BookedCount == min(N, Capacity)` 必須有 retry 才可能成立**，無 retry 時一次爆發只賣得掉 2～3 個名額（capacity=5、50 人併發）。使用者已明確要求此項單獨決策、不得順著死結修正一併倉促做掉。待決的子問題至少包含：(a) 重試幾次、退避策略為何；(b) 只對 InnoDB 死結重試，還是也對 `ConcurrencyConflictException` 重試（兩者語意不同：死結是 DB 主動犧牲、衝突是業務層面的搶輸）；(c) retry 是否會讓某些請求因排隊順序而系統性地更容易搶到，造成**公平性**問題；(d) retry 應放在哪一層——**已有既定限制，見 §0 v1.5**：`EnableRetryOnFailure()` 排除在選項外，不只是因為與現行顯式交易衝突（§0 v1.4），更根本的原因是它機械式重跑整個 transaction、不會重新讀取資料，會讓 retry 用過期快照重送；v2 的 retry 邏輯必須寫在 Application 層（例如 MediatR pipeline behavior 或 Handler 內迴圈），每次重試前重新 `GetByIdAsync()`。
8. **[Deferred Decision]** JWT 撤銷機制的最終方案（refresh token / denylist / token_version）（§9.7）。
9. **[Deferred Decision]** 完整 Idempotency-Key 機制的導入時機與實作方式（§7.3）。
10. **[Deferred Decision]** Application 層是否允許依賴 `EFCore.Abstractions`（§4.2）。
11. **[Deferred Decision]** 對外 `public_id`（防 id 枚舉）是否導入，及導入時機（§6.3）。
12. **[Deferred Decision]** `TIMESTAMP`/`Pomelo .IsRowVersion()` 是否在未來版本變得可靠，值得重新評估（目前决定不採用，見 §6.2 Alternative）。
13. ~~**[Deferred Decision — 最高優先，2026-09-05 新增]** `SaveChangesAsync` 部分寫入 / rollback 驗證：同一次 `SaveChangesAsync` 中若某筆 UPDATE（如 `schedule_slots`）因 optimistic concurrency 檢查失敗（影響列數為 0），是否保證整個 implicit transaction 完全 rollback，不會有其他語句（如 `appointments` 的 INSERT）被誤留下來部分提交。這與第 1 項不同：第 1 項驗證「EF 有沒有正確偵測到衝突」，本項驗證「偵測到之後，其他已送出但邏輯上應一併作廢的語句會不會被錯誤保留」。必須用 Testcontainers 真實 MySQL 驗證，不可假設；對應測試見 §8.5。~~ **[驗證通過，見 §0 v1.4]** 1c 新增 `BookingTransactionRollbackTests` 專門驗證此項。因死結修正已把 Booking 改為「同一筆顯式交易內兩次 `SaveChangesAsync`」，本項的風險面比原始描述更大（現在是「UPDATE 已成功提交送出、INSERT 才失敗」），測試以重複預約重現該視窗，實測確認整筆交易完整回滾：`booked_count` 與 `version` 皆停在前一次的值，`appointments` 筆數不變，兩表無 drift。反向驗證亦完成：以 mutation testing 移除顯式交易後，實測出現 `booked_count=2` 但 `appointments=1` 的 drift，測試如預期紅燈。
14. **[Deferred Decision，2026-09-05 新增]** 取消時間窗（cancellation window，例如「看診前 N 小時內不可取消」）機制是否要做、規則為何。此前曾短暫出現於 §7.4 的 `CANCELLATION_WINDOW_CLOSED` 狀態碼已移除（因 §3.5 從未定義對應規則），MVP 不實作；若未來要做，需先在 §3.5 補上 Domain Invariant，並解決 `Appointment.Cancel()` 需要跨 aggregate 讀取 `ScheduleSlot` 時間的設計問題。
15. **[Deferred Decision，2026-09-05 新增]** `users.role = Admin` 的實際用途與對應 endpoint 尚未設計。MVP 僅保留欄位與 enum 值（因 `role` 本身被 §7.3 的 Patient-only authorization 使用），但沒有任何 Admin-only 功能；不得在程式碼中提前假設 Admin 已有意義。

---

## 13. 面試官可能追問的問題（附錄）

**[Decision — 保留原文作為面試準備素材，非架構決策本身]**

此清單為規劃討論中一併產出的面試準備素材，與架構決策本身分開列出，避免與 §9 的決策紀錄混淆。僅列出問題與測試意圖，答案由使用者自行準備：

1. 為什麼 `BookedCount` 存在 `ScheduleSlot` 上，而非每次 `COUNT(appointments)` 算出來？
2. Concurrency token 是什麼型別？為什麼不能直接用 SQL Server 的 `rowversion`？
3. 併發衝突為什麼回 409，而不是 400/500/503/422？
4. 若 1000 人同時搶同一個名額，設計會發生什麼？如何改善？
5. Transaction 邊界在哪裡？誰負責 commit？若需在同一操作中寫兩個 aggregate 呢？
6. Cancel 之後 `BookedCount` 怎麼回復？兩人同時 cancel 同一筆會怎樣？
7. Integration test 用什麼資料庫？為什麼不用 EF Core InMemory provider？
8. 併發測試如何證明沒有超賣？只斷言 `BookedCount <= Capacity` 夠嗎？
9. 在 EF Core 上再包 Repository Pattern 是否多餘？
10. MediatR 帶來了什麼是一般 DI service 做不到的？成本是什麼？
11. Booking 的 `patientId` 從哪裡來？若客戶端在 body 傳 `patientId` 會怎樣？
12. 查詢/取消別人的 appointment，回 403 還是 404？為什麼？
13. Redis 快取的 `availableCount` 過期但名額已被訂走，可以接受嗎？
14. JWT 要怎麼撤銷？使用者被停權後，手上的 token 還能用嗎？
15. 時間怎麼存？跨時區/日光節約時間怎麼處理？
16. Production 上如何得知 booking 衝突率？衝突率飆高怎麼查？
17. 客戶端 timeout 後重送請求，會不會產生兩筆預約？

---

*文件結束。若後續討論產生新的決策或推翻本文件內容，請更新本文件並保留版本歷史（例如另建 v2 章節或於檔案頂部加註變更紀錄），不要直接覆寫掉先前已定案的內容而不留痕跡。*
