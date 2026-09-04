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

`TimeSlot` 為 Value Object（EF Core owned type）。

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

判斷 `DbUpdateException` 具體違反哪一個 unique key，需要解析 inner `MySqlException` 的錯誤訊息或 constraint 名稱；這段解析邏輯應封裝在 Infrastructure 層（例如一個 `MySqlExceptionTranslator`），Application/Api 層不可直接對例外訊息做字串比對。

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

Status: `201`／`400 VALIDATION_FAILED`／`401`／`404 SLOT_NOT_FOUND`／`409 SLOT_FULL`（不可重試）／`409 CONCURRENCY_CONFLICT`（可重試）／`409 DUPLICATE_BOOKING`／`409 SLOT_NOT_BOOKABLE`。

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

1. **[Deferred Decision — 最高優先]** Pomelo 的 `IsConcurrencyToken()` + MySQL 受影響列數行為，特別是連線字串 `UseAffectedRows` 設定對 EF 併發偵測的實際影響。
2. **[Deferred Decision]** 實際使用的 MySQL 版本是否 ≥ 8.0.16（影響 `CHECK` constraint 是否真的被強制執行）。
3. **[Deferred Decision]** Pomelo 是否支援 `HasComputedColumnSql(..., stored: true)` 映射 generated column，以及 migration 產出是否正確。
4. **[Deferred Decision]** `DateTime`/`DateTimeOffset` 在 Pomelo 上對 `DATETIME(6)` 的實際映射與往返精度。
5. **[Deferred Decision]** MediatR 目前版本的授權條款細節，以及是否改用替代方案（`Mediator`、Wolverine、DI+Scrutor）。
6. **[Deferred Decision]** `WebApplicationFactory` + `Task.WhenAll` 是否能產生真正的並行請求（而非被排程成近似循序）——需在撰寫併發測試時驗證。
7. **[Deferred Decision]** Retry 機制的具體實作方式與導入時機（§9.5）。
8. **[Deferred Decision]** JWT 撤銷機制的最終方案（refresh token / denylist / token_version）（§9.7）。
9. **[Deferred Decision]** 完整 Idempotency-Key 機制的導入時機與實作方式（§7.3）。
10. **[Deferred Decision]** Application 層是否允許依賴 `EFCore.Abstractions`（§4.2）。
11. **[Deferred Decision]** 對外 `public_id`（防 id 枚舉）是否導入，及導入時機（§6.3）。
12. **[Deferred Decision]** `TIMESTAMP`/`Pomelo .IsRowVersion()` 是否在未來版本變得可靠，值得重新評估（目前决定不採用，見 §6.2 Alternative）。
13. **[Deferred Decision — 最高優先，2026-09-05 新增]** `SaveChangesAsync` 部分寫入 / rollback 驗證：同一次 `SaveChangesAsync` 中若某筆 UPDATE（如 `schedule_slots`）因 optimistic concurrency 檢查失敗（影響列數為 0），是否保證整個 implicit transaction 完全 rollback，不會有其他語句（如 `appointments` 的 INSERT）被誤留下來部分提交。這與第 1 項不同：第 1 項驗證「EF 有沒有正確偵測到衝突」，本項驗證「偵測到之後，其他已送出但邏輯上應一併作廢的語句會不會被錯誤保留」。必須用 Testcontainers 真實 MySQL 驗證，不可假設；對應測試見 §8.5。
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
