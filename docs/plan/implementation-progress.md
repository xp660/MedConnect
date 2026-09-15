# MedConnect Implementation Progress & Workflow

> **本檔案用途**：追蹤 MVP 五個實作階段（1a-1e）的目前進度，並記錄每個階段實際採用的 Developer / Reviewer / 知識點教學執行流程。
>
> 與 [`architecture-plan.md`](architecture-plan.md) 分工不同：那份文件是「架構決定了什麼」的 SSOT；這份文件是「進度到哪、每次怎麼做」的追蹤表，會隨著實作進度持續更新（不需要像 architecture-plan.md 的 [Decision] 條目一樣走版本保留規則，直接更新勾選狀態與備註即可）。
>
> **分頁分工**：
> - **這個主分頁（Developer/Architect）**：實作、知識點教學、架構討論、進度查詢 —— 都在這裡。
> - **Reviewer 分頁（只在 1c 開啟）**：純粹 blind code review，只餵它 git diff + `CLAUDE.md` + `architecture-plan.md`，不在那裡討論架構或知識點，發現的問題一律帶回主分頁處理。

---

## 目前狀態（最後更新：2026-09-15）

- [x] 0. 前置作業：`git init`（repo 已存在，含 initial commit）
- [x] 1a 骨架（commit `246fed4`：4 專案＋DI 組裝＋docker-compose(MySQL)＋health check，`dotnet run` 可起、`/health` 回 200）
- [x] 1b Domain + DB（commit `e35614e`：ScheduleSlot/Appointment entity＋invariant＋18 個 Domain unit test 全綠＋EF 設定＋version interceptor＋migration＋seed；已用真實 Docker MySQL 8.0.39 驗證 migration 套用、CHECK constraint、generated column 皆如預期運作；獨立 blind review 抓到 6 項發現，5 項已修正，1 項（TimeSlot 用 Complex Type 而非文件寫的 owned type）與使用者確認後以 architecture-plan.md §0 v1.2 版本化更新處理）
- [ ] 1c 併發驗證（全案最重要里程碑）—— **單一請求完整流程已完成並驗證**（commit `0f043d8`）：BookAppointmentCommand/Handler、IScheduleSlotRepository/IAppointmentRepository/IUnitOfWork（含 Infrastructure 實作）、AppointmentsController、GlobalExceptionHandler 全部串接完成；用真實 Docker MySQL 手動驗證成功／404 SLOT_NOT_FOUND／409 SLOT_FULL／409 DUPLICATE_BOOKING 四種情境；過程中發現並修正一個真實 bug（`UnitOfWork.IsDuplicateActivePatientBooking()` 誤判 SaveChanges 的 tracked entry 數量，見下方變更紀錄）。**尚未開始**：Testcontainers 整合測試專案、50 vs 5 併發測試（§8.5 招牌測試）——這才是本階段真正的 exit criteria，還沒達成。
- [ ] 1d 補齊 MVP
- [ ] 1e 加值

1c 的「單一請求路徑」（非併發）已完成並驗證，下一步是建立 Testcontainers + 50 vs 5 併發測試（§8.5 招牌測試，全案最重要里程碑），尚未開始。

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
