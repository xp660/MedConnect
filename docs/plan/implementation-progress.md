# MedConnect Implementation Progress & Workflow

> **本檔案用途**：追蹤 MVP 五個實作階段（1a-1e）的目前進度，並記錄每個階段實際採用的 Developer / Reviewer / 知識點教學執行流程。
>
> 與 [`architecture-plan.md`](architecture-plan.md) 分工不同：那份文件是「架構決定了什麼」的 SSOT；這份文件是「進度到哪、每次怎麼做」的追蹤表，會隨著實作進度持續更新（不需要像 architecture-plan.md 的 [Decision] 條目一樣走版本保留規則，直接更新勾選狀態與備註即可）。
>
> **分頁分工**：
> - **這個主分頁（Developer/Architect）**：實作、知識點教學、架構討論、進度查詢 —— 都在這裡。
> - **Reviewer 分頁（只在 1c 開啟）**：純粹 blind code review，只餵它 git diff + `CLAUDE.md` + `architecture-plan.md`，不在那裡討論架構或知識點，發現的問題一律帶回主分頁處理。

---

## 目前狀態（最後更新：2026-09-18）

- [x] 0. 前置作業：`git init`（repo 已存在，含 initial commit）
- [x] 1a 骨架（commit `246fed4`：4 專案＋DI 組裝＋docker-compose(MySQL)＋health check，`dotnet run` 可起、`/health` 回 200）
- [x] 1b Domain + DB（commit `e35614e`：ScheduleSlot/Appointment entity＋invariant＋18 個 Domain unit test 全綠＋EF 設定＋version interceptor＋migration＋seed；已用真實 Docker MySQL 8.0.39 驗證 migration 套用、CHECK constraint、generated column 皆如預期運作；獨立 blind review 抓到 6 項發現，5 項已修正，1 項（TimeSlot 用 Complex Type 而非文件寫的 owned type）與使用者確認後以 architecture-plan.md §0 v1.2 版本化更新處理）
- [x] 1c 併發驗證（全案最重要里程碑）—— **Exit criteria 已達成**：Testcontainers 整合測試專案（`MedConnect.IntegrationTests`）＋ §8.5 招牌測試（50 個不同病人搶 Capacity=5）＋ 無 partial commit 專門測試，全部綠燈，5 輪共 250 個請求 0 死結、0 非預期例外、0 超賣、兩表 0 drift。過程中用真實 MySQL 抓到**兩項文件層級的錯誤假設**（InnoDB 死結未被 §5.2 涵蓋；§8.5 的 `min(N, Capacity)` 與「不啟用 retry」互斥），已依 §7 版本保留規則記入 architecture-plan.md §0 v1.4，未覆寫原文。斷言有效性以 mutation testing 反向驗證過，非「怎麼跑都會過」的假測試。
- [ ] 1d 補齊 MVP
- [ ] 1e 加值

1c 已完整完成。下一步進入 1d（Login/JWT＋Schedule Query＋Cancel），但在那之前建議先單獨處理 architecture-plan.md §12 第 7 項（Retry 機制）——1c 實測已把它從「加值項目」升為「§8.5 第 2/4 條斷言能否成立的前提」，使用者已指定此項要單獨用一次 Architect Mode 討論，不得順帶做掉。

---

## 總進度表（Roadmap）

| # | 階段 | 內容 | Reviewer 模式 | Exit Criteria（完成標準） |
|---|---|---|---|---|
| 0 | 前置作業 | `git init` | — | repo 可以 commit |
| 1a | 骨架 | 4 專案＋DI＋docker-compose(MySQL)＋health check | 輕量（同分頁 `/code-review`） | `dotnet run` 能起來 |
| 1b | Domain + DB | ScheduleSlot/Appointment entity＋invariant＋Domain unit test＋EF 設定＋version interceptor＋migration＋seed | 中量（同分頁 `/code-review`，逐條對照 §3.5 invariant） | Domain unit test 全部綠燈 |
| 1c | **併發驗證**（全案最重要） | BookAppointment handler＋POST endpoint（先不做 JWT）＋Testcontainers＋併發測試 | **重量**（開新分頁 blind review） | 併發測試全部斷言綠燈（§8.5 雙向斷言） |
| 1d | 補齊 MVP | Login/JWT＋Schedule Query＋Cancel＋全域例外處理＋其餘 integration test | 中量（同分頁，另核對 §5.2 例外對應表） | 四支 API 全部可用、整合測試綠燈 |
| 1e | 加值 | Redis 快取＋Serilog/OpenTelemetry＋Dockerfile＋CI＋ArchitectureTests | 輕量（同分頁） | CI pipeline 綠燈、README 完成 |

---

## 每個階段的實際執行迴圈

這個迴圈在 1a / 1b / 1d / 1e 都一樣，只有 1c 在第 4 步改成開新分頁：

```
1. 你確認：「今天/現在進入 XX 階段」
2. Developer（我）實作這個階段的完整段落
3. 我用白話解釋：做了什麼、為什麼這樣設計、有什麼 trade-off
4. Reviewer 審查
     - 1a/1b/1d/1e → 我在同分頁跑 /code-review
     - 1c          → 你另開分頁，我告訴你要餵給它什麼（diff + 兩份文件），
                      它盲審後把發現帶回這裡
5. 我把 Review 發現翻譯成你聽得懂的版本
6. 我直接修正
7. 我跑測試，把真實的綠燈/紅燈結果貼給你看
8. 知識點教學總結：這階段展示了什麼能力 + 對應 architecture-plan.md §13 面試官可能問的問題
9. git commit（commit message 寫明「驗證了什麼」，例如
   "1b: Domain invariants + unit tests all green"）
10. 回來本檔案，把該階段打勾，準備下一階段
```

---

## 變更紀錄

- 2026-09-05：建立本檔案，記錄五階段 Roadmap 與執行迴圈（尚未開始任何階段）。
- 2026-09-05：確認 repo 已有 initial commit，勾選「0. 前置作業：git init」；準備進入 1a。
- 2026-09-05：完成 1a 骨架（4 專案＋DI 組裝＋docker-compose(MySQL)＋health check），`dotnet build`/`dotnet run`/`GET /health` 皆驗證通過，commit `246fed4`。docker-compose.yml 本機無 Docker CLI，僅人工檢視語法，未實際 `docker compose up` 驗證。
- 2026-09-07：本機已有 Docker CLI，補做 1a 遺留的驗證：`docker compose up` 實際跑起 MySQL，healthcheck 如預期在數秒內轉為 healthy。
- 2026-09-07：完成 1b（ScheduleSlot/Appointment entity＋invariant＋18 個 Domain unit test＋EF 設定＋version interceptor＋migration＋seed），commit `e35614e`。全程用真實 Docker MySQL 8.0.39 驗證（非僅 unit test 綠燈）：migration 套用成功、CHECK constraint 生效、generated column + partial unique index 正確擋下/放行重複預約、`dotnet run` 會自動 migrate+seed 且 `/health` 回 200。獨立 blind reviewer（只給 diff＋CLAUDE.md＋architecture-plan.md）抓到 6 項發現，5 項已修正（tinyint 符號性、DB 欄位 DEFAULT、FK ON DELETE 行為、文件註解），1 項（TimeSlot 用 EF Core Complex Type 而非文件寫的 owned type）與使用者確認後改為版本化更新 architecture-plan.md（§0 v1.2），沒有默默覆寫原文。實作過程中也順帶驗證了 §12 第 2、3 項 Deferred Decision，並發現一個新的 MySQL 限制（generated column 依賴的欄位其 FK 不可用 ON DELETE CASCADE），已記入 §0 v1.2。
- 2026-09-12：Phase 1c 開始前，先解決 §12 第 5 項 Deferred Decision（MediatR 授權）：查證 MediatR v13.0.0 起改為商業授權，決定專案釘住 v12.5.0（Apache 2.0 最後一版免費授權），不採用 v13+ 商業版、不改用替代方案，已記入 architecture-plan.md §0 v1.3。
- 2026-09-15：完成 1c 的「單一請求完整流程」（先不含 50 vs 5 併發測試），commit `0f043d8`（原始 commit `0ecd430` 之後被 amend 補上 bug fix 說明，hash 已變動，此處更正引用）。新增 `BookAppointmentCommand`/`BookAppointmentHandler`、`IScheduleSlotRepository`/`IAppointmentRepository`/`IUnitOfWork`（Infrastructure 實作）、`AppointmentsController`（`POST /api/v1/appointments`，PatientId 暫時寫死，1d 才接 JWT）、`GlobalExceptionHandler`（對照 §5.2/§7.3 把例外轉成 ProblemDetails + errorCode）；`DatabaseSeeder` 補一筆固定測試用 Patient/User 以滿足 FK。用真實 Docker MySQL（docker-compose，非 EF InMemory）手動跑過成功(201)／不存在(404 SLOT_NOT_FOUND)／訂滿(409 SLOT_FULL)／重複預約(409 DUPLICATE_BOOKING)四種情境，並交叉查過 DB 最終狀態（`appointments` 筆數、`schedule_slots.booked_count`/`version`）確認無 partial write／無 drift。過程中在真實 MySQL 上跑出一個先前 unit test 沒抓到的真實 bug：`UnitOfWork.IsDuplicateActivePatientBooking()` 誤假設「觸發唯一索引衝突的 SaveChanges 只會有一個 tracked entry」，但 Booking 一定會同時讓 `ScheduleSlot`（Modified）跟新增的 `Appointment`（Added）一起出現，導致 `DuplicateBookingException` 帶的 `slotId`/`patientId` 一直被誤判成 0。改用 `EntityState.Added` + 型別過濾修正（不解析 exception message 字串），並用「先讓新測試在修正前的程式碼上跑到 FAIL，再套用修正跑到 PASS」的方式驗證這個回歸測試真的抓得住這個 bug。新增測試專案 `MedConnect.Application.UnitTests`（Handler 編排測試）。**尚未開始、也是 1c 真正的 exit criteria**：Testcontainers 整合測試專案、50 個病人搶 5 個名額的併發測試（§8.5 招牌測試）。
- 2026-09-18：完成 1c 的 Testcontainers + 50 vs 5 併發測試，1c 正式結案。新增 `MedConnect.IntegrationTests` 獨立測試專案（Testcontainers.MySql 4.15.0，image 釘 `mysql:8.0.39` 與 docker-compose 一致，容器與手動測試用的 docker-compose MySQL 完全獨立），以 `IAsyncLifetime` collection fixture 管理容器生命週期，並用與 `Program.cs` 完全相同的 `AddApplication()` + `AddInfrastructure()` 組出指向容器的 `IServiceProvider`，每個模擬請求各自 `CreateAsyncScope()` 取得獨立 DbContext、經 `IMediator.Send()` 送出。

  **測試第一次跑就抓到兩個真問題，都不是斷言失敗：**

  1. **InnoDB 死結（MySQL 1213）**：50 個請求中 **47 個**死結，且因 1213 不在 §5.2 例外對映表內，會以未對映的 `InvalidOperationException` 外洩成 HTTP 500。用 `SHOW ENGINE INNODB STATUS` 的 LATEST DETECTED DEADLOCK 報告確認根因（非推測）：兩個交易同時 `HOLDS lock mode S` 又 `WAITING FOR lock_mode X`，鎖在 `schedule_slots` 同一列；S 鎖來自 `appointments` INSERT 的 FK 檢查，X 鎖來自 `schedule_slots` 的 UPDATE，而 EF Core 因 principal 是 Modified（非 Added）不建立相依邊、退回**資料表名稱字典序**，把 INSERT 排在 UPDATE 前面。同時證實「調整 change tracker 加入順序」無效（原本就已是 Update 先於 Add）。修法：`IUnitOfWork` 新增 `ExecuteInTransactionAsync<T>()`，同一筆顯式交易內拆成兩次 `SaveChangesAsync`（先 UPDATE 拿 X 鎖、再 INSERT）。修後 5 輪 250 個請求死結 **0 次**。未加任何 retry（依使用者指示，retry 為獨立決策）。
  2. **§8.5 兩條 [Decision] 互斥**：`BookedCount == min(N, Capacity)` 與「第一版不啟用 retry」在物理上不可能同時成立——50 個請求同時讀到同一個 `version`，CAS 每輪至多一個贏家。對照實驗（排除死結變因）連續 3 次皆為「1 成功 / 49 衝突」；修好死結後的正式測試落在 2～3 成功（5 輪：3/3/3/2/3），本質非確定性。已與使用者確認採「v1 斷言改為至少 1 成功 / 絕不超賣 / 兩表一致，min(N, Capacity) 留到 v2」，並依 §7 規則在 §8.5 保留原文、加註「v1 適用範圍」。

  **斷言有效性驗證（mutation testing，三個變異）**：(a) 使用者原本指定的「Capacity 改 50」——實測**測試照樣通過**，誠實回報：該變異是為舊的 `==5/==45` 斷言設計的，對 v1 斷言已無偵測力；(b) 移除 `ScheduleSlotConfiguration` 的 `.IsConcurrencyToken()` → 50/50 全數成功造成超賣 → 測試如預期紅燈；(c) 移除 `ExecuteInTransactionAsync` 的顯式交易 → 出現 `booked_count=2` 但 `appointments=1` 的 drift → rollback 測試如預期紅燈。(b)(c) 證明測試對「樂觀鎖被破壞」與「交易未回滾」這兩件真正該抓的事有實際偵測力。

  順帶驗證通過 §12 第 1、6（部分）、13 項，並把第 7 項（Retry）升為最高優先。最終全測試：Domain 23 + Application 4 + Infrastructure 5 + Integration 2 = **34 個全綠**。
- 2026-09-19：修正 §8.5 招牌測試的斷言，使其精確反映 v1（無 retry）的真實保證，而非沿用舊的「恰好 5」假設。改動：成功數斷言為 `>= 1 且 <= 5`（不再要求 `== 5`）；新增「成功數 + 失敗數 == 50」的顯式斷言；資料庫最終狀態一致性改為直接比對 `schedule_slots.booked_count` 與獨立查出的 `COUNT(appointments WHERE Status == Booked)`，不透過回應數字繞一圈；失敗 errorCode 僅允許 `SLOT_FULL`/`CONCURRENCY_CONFLICT` 的斷言不變，並在程式碼註解中明確記錄「死結目前完全不在 UnitOfWork 的轉譯範圍內，若曾經漏出來會是全新型別、連測試的 catch 都接不住，會讓 Task.WhenAll 直接崩潰、不會被誤算進這兩種 errorCode」。architecture-plan.md §8.5 依 §7 版本保留規則補上「v1 實際保證」一句話總結與「v2 待辦」提示，原文與既有 v1.4 修正說明皆未覆寫。

  跑了 4 輪（本輪）＋先前 7 輪，共 11 輪、550 個請求，**死結 0 次**，全部通過新斷言：
  | 輪次 | 成功 | 失敗 |
  |---|---|---|
  | 1 | 2 | 48 |
  | 2 | 2 | 48 |
  | 3 | 2 | 48 |
  | 4 | 3 | 47 |

  資料庫最終狀態每輪皆自洽（`booked_count == active_appointments == 成功數`）。

  **死結 vs 樂觀鎖衝突能否分開統計**：可以，且不需要額外程式碼——兩者在目前的例外轉譯邏輯下是**結構性不同**的例外類型，不是同一型別的不同訊息。`ConcurrencyConflictException` 只可能來自 `DbUpdateConcurrencyException`（真正的 `WHERE version = ?` 影響列數為 0），死結（MySqlError 1213）完全不在 `UnitOfWork.SaveChangesAsync` 的兩個 catch 範圍內（見程式碼註解），所以絕不會被誤算進 `CONCURRENCY_CONFLICT` 的統計裡——如果死結漏出來，會是一個測試 `catch` 不住的全新例外型別，讓整個測試直接崩潰紅燈，而不是安靜地被計成第三種失敗。這也代表本輪測出的「47/48 CONCURRENCY_CONFLICT」是純粹的樂觀鎖搶輸數字，不含任何死結。

  **已知缺口，本輪未修，待使用者決定是否處理**：`UnitOfWork.SaveChangesAsync` 目前沒有任何一行程式碼認得 MySqlError 1213。死結修正（v1.4）消除的是「INSERT 排在 UPDATE 前面」這個死結成因，但沒有替 1213 補上例外轉譯／errorCode 對應。也就是說，如果未來（例如換一種查詢模式、加了別的併發寫入路徑）又出現死結，它依然會以未對映例外外洩成 HTTP 500——這與死結成因是否已排除是兩件事。目前 11 輪 550 個請求死結率為 0，這件事還不急，但要不要現在補上 1213 → 可重試例外的對映，是一個獨立的小改動，需要使用者決定要不要做。
- 2026-09-21：補齊死結（MySqlError 1213）的例外轉譯，屬純錯誤處理完整性修正，**未實作任何 retry 邏輯**。新增 `TransientConflictException`（Application 層，與 `ConcurrencyConflictException` 刻意分開，理由見類別 XML 註解：兩者成因結構不同，未來若要分別調整重試策略不必重新拆開）；`GlobalExceptionHandler` 補上 `TRANSIENT_CONFLICT`（409，可重試）。

  **動手前先用真實 MySQL 驗證了例外形狀，結果與原本假設的不同**：原本預期死結會跟 `DUPLICATE_BOOKING` 一樣是 `DbUpdateException`，但用一組與 booking 無關、兩個 slot 刻意反向鎖定順序更新的對照實驗重現死結（5 輪、5 次命中），實際形狀是三層：`InvalidOperationException -> DbUpdateException -> MySqlException{ErrorCode=LockDeadlock}`——因為 Booking 現在是在顯式交易（`ExecuteInTransactionAsync`，v1.4 導入）裡呼叫 `SaveChangesAsync`，EF Core 的 `ExecutionStrategy` 判定例外看似 transient、但處於使用者自管交易中且未開 `EnableRetryOnFailure()`，無法安全自動重試，因此包了一層 `InvalidOperationException`。若照最初的直覺寫 `catch (DbUpdateException ex) when (...)`，這個 catch 永遠不會命中，死結會繼續外洩成 500——這是「先實測、再寫轉譯邏輯」第二次在這個功能上抓到跟預期不符的地方（第一次是 v1.4 死結成因本身）。已依 §7 版本保留規則記入 architecture-plan.md §0 v1.5，並在 §5.2 例外對映表新增第三列、§7.3 補上 `409 TRANSIENT_CONFLICT`，原文均未覆寫。

  `UnitOfWorkTests` 新增兩個測試：(1) 完整重現三層例外形狀，驗證正確轉譯成 `TransientConflictException`；(2) 反向驗證一個「無關的 `InvalidOperationException`」不會被誤判成死結而悄悄吞掉，必須原樣往外拋——確保新 catch 的 `when` 過濾條件夠精準。死結 catch（攔 `InvalidOperationException`）與既有的 duplicate-key catch（攔 `DbUpdateException`）鎖定完全不同的例外型別，天生互斥，不依賴攔截順序維持互斥。

  全測試回歸：Domain 23（不變）／Application 4（不變）／**Infrastructure 5→7**（新增兩個死結轉譯測試）／Integration 2（不變）。50 vs 5 併發測試重跑 3 輪，行為與修正前完全一致（成功 2、失敗 48、DB 自洽、死結率仍是 0）——符合預期：這輪修正的是「萬一死結發生時的錯誤處理」，不改變死結發生的機率。
- 2026-09-22：使用者補充 v1.5 的一項設計限制並已記入 architecture-plan.md（§0 v1.5 第 5 項、§12 第 7 項 (d)）：`EnableRetryOnFailure()` 刻意維持關閉，且此限制延伸到 v2——v2 的 retry 必須寫在 Application 層、每次重試前重新讀取資料，不能用 EF Core 這個機械式重跑整個 transaction、不重新讀資料的內建開關（否則會與手寫 retry 疊加、且可能用過期快照重試）。純文件補充，未動程式碼，未跑測試。
- 2026-09-25：進行 1d 的 Login/JWT 手動驗證時，發現本機環境已知缺口——尚未修正，記錄現象供之後處理：

  **現象**：`docker ps` 顯示本機執行中的 `medconnect-mysql-1` 容器對外連接埠是 `0.0.0.0:13306->3306`，但 `src/MedConnect.Api/appsettings.Development.json` 的 `ConnectionStrings:MedConnect` 寫的是 `Port=3306`。若直接 `dotnet run` 不覆寫連線字串，會連不上這個容器（3306 無人監聽）。手動驗證 Login 時是用環境變數 `ConnectionStrings__MedConnect`（指向 13306）暫時覆寫過去，未修改任何已提交的檔案。

  **可能成因**（尚未實際查證，僅為推測）：`docker-compose.yml` 的埠對應寫的是 `"${MYSQL_PORT:-3306}:3306"`，代表這個容器極可能是在某次帶有 `MYSQL_PORT=13306` 環境變數的 shell（例如本機另一個常駐 3306 的服務、或先前手動除錯時暫時改過）下啟動的；容器本身持續執行超過一週（`CREATED 9 days ago`），時間上早於這次 1d 工作。目前的 shell session 並未設定 `MYSQL_PORT`，代表這個對應是啟動當下決定的，事後改 `.env`/環境變數不會回溯影響已存在的容器，需要 `docker compose down` 再 `up` 才會套用新值。

  **這次刻意不處理的原因**：不確定使用者是否依賴 13306（例如另一個服務占用了本機 3306），貿然 `docker compose down`/改連線字串屬於會影響本機環境設定的動作，超出這次 Login/JWT 任務範圍，留給使用者決定要固定用 13306（改 `appsettings.Development.json`）還是換回 3306（重建容器）。
