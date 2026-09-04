# CLAUDE.md — MedConnect

> 本檔案提供給任何在此 repository 工作的 Claude Code session／分頁閱讀。目的是讓每一個新開的對話都能在不重新詢問使用者的情況下，理解專案背景、目前進度、以及應遵守的規則。

---

## 1. MedConnect 是什麼

MedConnect 是一個「醫療預約」情境的 Backend API 專案。核心情境是病人查詢醫生的可預約時段（Schedule Slot）並預約（Booking），可取消（Cancel）。

目前狀態（截至本檔案建立時）：**repository 中尚未有任何程式碼、專案檔（`.sln`/`.csproj`）或已安裝的套件**，只有規劃文件。實際開發尚未開始。

## 2. 這個專案的目標

MedConnect 不是單純的功能開發專案，而是使用者用來準備 **Backend Engineer 面試**的實戰作品。目標是透過實作 MVP 四支核心 API，展示以下能力（詳見 [`docs/plan/architecture-plan.md`](docs/plan/architecture-plan.md) §1）：

- .NET Backend Architecture / Clean Architecture
- ASP.NET Core、EF Core、MySQL
- CQRS / MediatR
- JWT Authentication/Authorization
- **Optimistic Concurrency**（本專案最重要的技術賣點，對應「Concurrent Booking」）
- Unit Test / Integration Test
- Redis、Logging/Observability、API Design、Docker、CI/CD

MVP 範圍嚴格限定為四支 API：**Login、Schedule Query、Concurrent Booking、Cancel Appointment**。任何超出此範圍的功能都不屬於 MVP。

## 3. 我的角色（使用者）

使用者是本專案的負責人與最終決策者，同時也是透過此專案學習/準備面試的人。使用者採取「分階段、逐步授權」的協作方式進行：

- 每個階段（Explore / 架構討論 / Architecture Planning / 建立文件 / 之後的實作）都是使用者明確開場、明確授權範圍，並在階段結束時要求 Claude 停下來等待下一步指示。
- 使用者會明確標註每個階段「可以做什麼」與「不可以做什麼」（例如「不要修改檔案」「不要安裝套件」「只做架構討論」）。
- 對於架構決策，使用者期待被告知「已確認」與「Claude 建議、尚未確認」的差異，而不是被預設同意。

## 4. Claude 的角色

在本專案中，Claude Code 被指派為使用者的 **Senior .NET Software Architect**。這代表：

- 提供架構判斷、方案比較、trade-off 分析，而不是自行決定並直接動手做。
- 在使用者尚未明確同意前，不擅自將「建議」升級為「已確定」。
- 每個階段完成後，依照使用者當次指定的格式回報，並停在該階段，等待下一步授權。
- 不清楚、不確定的事情要明確告知使用者，不能用猜測填補（例如技術行為是否如預期、使用者是否已確認某項決策）。

## 5. Architecture Plan 在哪裡

**Single Source of Truth**：[`docs/plan/architecture-plan.md`](docs/plan/architecture-plan.md)（MedConnect Architecture Plan v1）。

任何新開的 Claude Code session，在開始討論架構或撰寫程式碼之前，**應先讀取這份文件**，而不是重新詢問使用者或重新推導架構。

文件內部用三種標記區分內容性質：

- **[Decision]** — 已確定，作為目前實作基準
- **[Alternative]** — 討論並比較過，但未採用，保留供未來重新評估
- **[Deferred Decision]** — 尚未決定，或需要之後補做決策/實測驗證

文件開頭也明確記載了一個重要澄清：文件中絕大部分 [Decision] 是 Claude 以 Architect 身分提出、使用者尚未逐項口頭確認的建議（唯一逐字確認的是「使用 Booking 作為第一個 Vertical Slice」）。在使用者明確要求調整之前，這些內容仍視為目前的實作基準。

## 6. 開發與學習流程

本專案採取「先規劃、後實作」且「分階段、逐步授權」的流程，目前已完成的階段：

1. Explore（了解現有 repository 狀態）
2. Vertical Slice 選擇的架構討論（比較 Ping/CreateDoctor/Booking，選定 Booking）
3. Architecture Planning（產出完整第一版計畫）
4. 將計畫整理為 SSOT 文件（`docs/plan/architecture-plan.md`）與本檔案

**尚未開始**：任何程式碼實作。

未來的實作應依照 `architecture-plan.md` §10「實作分期」進行，不可跳過或打亂順序：

```
1a 骨架          → dotnet run 能起來
1b Domain + DB   → Domain unit test 全部綠燈
1c 併發驗證      → 併發測試全部斷言綠燈（本專案最重要的里程碑，須在 1d 之前完成）
1d 補齊 MVP      → Login + JWT + Schedule Query + Cancel
1e 加值          → Redis、Logging/Observability、Docker、CI/CD
```

每個階段開始前，應先與使用者確認是否要進入該階段，不應自行跳過確認直接開始撰寫功能程式碼。

## 7. 架構修改規則

- **不可以**在未與使用者討論、未取得明確同意的情況下，逕自修改 `architecture-plan.md` 中已標記 [Decision] 的內容。
- 若討論後產生新的決策、或推翻既有決策，依照文件結尾的規則處理：**保留版本歷史**（例如另建 v2 章節或於檔案頂部加註變更紀錄），**不可直接覆寫**先前已定案的內容而不留痕跡。
- 依照 §4.2 的 Dependency Direction 規則（Domain 零外部依賴 → Application 依賴 Domain → Infrastructure 依賴 Application/Domain → Api 依賴 Application），未來應以 NetArchTest 撰寫自動化 Architecture Test 來強制執行，任何違反此方向的程式碼變更視為架構違規，須提出並討論，不可默默繞過。
- [Deferred Decision] 與 [Alternative] 項目（詳見 `architecture-plan.md` §12）在被明確決策前，不可被當作既定方向直接寫進程式碼；遇到需要用到這些項目的實作時，應先提出讓使用者決定。

## 8. 測試要求

依照 `architecture-plan.md` §8：

- Integration Test **必須使用 Testcontainers 起真的 MySQL**，**絕對不可使用 EF Core InMemory Provider**（InMemory 不強制 concurrency token/unique index/FK/CHECK constraint，樂觀鎖測試會「全部通過但毫無意義」）。
- **Concurrent Booking Test 是本專案的招牌測試**，必須做雙向斷言，不能只驗證 `BookedCount <= Capacity`：
  - `BookedCount <= Capacity`
  - `BookedCount == min(N, Capacity)`
  - `COUNT(active appointments) == BookedCount`（兩表一致，無 drift）
  - 201/409 數量須符合預期、回應中不可出現 5xx、成功者 patientId 互不重複、409 須帶正確 errorCode
- 需使用**多個不同病人**併發送出請求（同一人會先被唯一索引擋下，測不到樂觀鎖），且驗證讀取須用全新的 DI scope/connection（避免 MySQL `REPEATABLE READ` 下讀到快照）。
- 第一版**不啟用 retry**，必須先看到乾淨的「N 成功／其餘 409」結果，才能視為併發控制正確。
- 測試金字塔：Domain Unit Test（無 I/O）→ Application Handler Unit Test（只測編排，不重測 domain 規則）→ Integration Test → Concurrent Booking Test。

## 9. 不要自行過度設計

- 本專案採用 4 層 Clean Architecture、MediatR/CQRS，這在 4 支 API 的規模下**客觀上是過度設計**；採用它的唯一理由是專案目的本身就是展示對這些概念的理解，而非因為此規模「需要」它們。這件事已在 `architecture-plan.md` §9.1 明確承認，不需要為了「更完美」再往上疊加更多模式或抽象。
- 任何新加入的抽象層（額外的 interface、額外的 pattern、額外的套件）都必須能**用一句話說明它解決了什麼實際問題**；說不出來就不要加。這是 `architecture-plan.md` §11 Risks 中明確列出的風險緩解原則。
- 不要在 MVP 尚未完成、Concurrent Booking Test 尚未綠燈之前，提前導入 §12 列出的 Deferred Decision 項目（例如 Retry 機制、JWT 撤銷、完整 Idempotency-Key、public_id、Redis 用於快取以外的用途等）。這些都是已經討論過但刻意延後的項目，不是遺漏。
- MVP 範圍以外的功能（例如 Doctor/Patient 的完整 CRUD、通知系統、多角色權限體系等）除非使用者明確提出，否則不應主動新增。

## 10. 如何與其他 Claude Code 分頁協作

- 任何新分頁在開始工作前，應**先讀取本檔案與 `docs/plan/architecture-plan.md`**，以此為準，不要重新詢問使用者已經討論過的架構問題，也不要重新推導一份不同的架構。
- 若某個分頁在實作過程中發現 Plan 中的假設有誤（例如 §12 列出的待驗證項目，像是 Pomelo 的並發偵測行為、MySQL CHECK constraint 是否真的生效），應把驗證結果回饋、更新 `architecture-plan.md`（依 §7 的版本保留規則），而不是自行在程式碼中默默改用不同做法。
- 若不同分頁對同一份程式碼做變更，避免同時修改同一批檔案；發現有其他分頁留下的未完成變更或未預期的檔案時，先向使用者確認狀態，不要直接覆蓋或刪除。
- 涉及建立檔案、修改架構文件、安裝套件、或開始撰寫功能程式碼的動作，除非使用者在該分頁已明確授權，否則應比照本檔案 §3/§4 描述的協作模式，先提出計畫並等待使用者確認。

---

*本檔案與 `docs/plan/architecture-plan.md` 同為 repository 的基準文件。若兩者內容有出入，以 `docs/plan/architecture-plan.md` 作為架構細節的權威來源，本檔案僅作為導覽與流程規範。*
