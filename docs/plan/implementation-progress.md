# MedConnect Implementation Progress & Workflow

> **本檔案用途**：追蹤 MVP 五個實作階段（1a-1e）的目前進度，並記錄每個階段實際採用的 Developer / Reviewer / 知識點教學執行流程。
>
> 與 [`architecture-plan.md`](architecture-plan.md) 分工不同：那份文件是「架構決定了什麼」的 SSOT；這份文件是「進度到哪、每次怎麼做」的追蹤表，會隨著實作進度持續更新（不需要像 architecture-plan.md 的 [Decision] 條目一樣走版本保留規則，直接更新勾選狀態與備註即可）。
>
> **分頁分工**：
> - **這個主分頁（Developer/Architect）**：實作、知識點教學、架構討論、進度查詢 —— 都在這裡。
> - **Reviewer 分頁（只在 1c 開啟）**：純粹 blind code review，只餵它 git diff + `CLAUDE.md` + `architecture-plan.md`，不在那裡討論架構或知識點，發現的問題一律帶回主分頁處理。

---

## 目前狀態（最後更新：2026-09-05）

- [x] 0. 前置作業：`git init`（repo 已存在，含 initial commit）
- [x] 1a 骨架（commit `246fed4`：4 專案＋DI 組裝＋docker-compose(MySQL)＋health check，`dotnet run` 可起、`/health` 回 200）
- [ ] 1b Domain + DB
- [ ] 1c 併發驗證（全案最重要里程碑）
- [ ] 1d 補齊 MVP
- [ ] 1e 加值

1a 已完成並驗證，準備進入 1b（Domain + DB）。

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
