# Agent instructions

<!-- bmad:context -->
<!-- Verified 2026-09-16 against 0ce083a. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## AI_Project_Console

四套獨立產品（控制台／公司工作區／系統分析／仲介），.NET 10、Blazor。產品規格與執行計畫在 `docs/product/`（權威：`spec.md`、`prd.md`、`architecture.md`、`execution-plan.md`）。Cursor 規則見 `.cursor/rules/`。

## Policy

- 遵守 OOP 與 SOLID：單一職責、依賴抽象、可替換實作；禁止 God class、跨邊界亂引用。
- 禁止以 MVP、骨架、佔位、dry-run、記憶體暫存、規則模板、「之後再補」充當完成；缺口必須在文件標明**未完成**，不得寫成已出貨。
- 禁止用註解承載設計、契約或未完成工作；行為以可執行程式＋測試為準，規格寫進 `docs/product/`。
- 每個宣稱功能必須文件化；有碼無文件、有文件無碼、文件與行為不一致皆為缺陷——改碼時同步改文件。
- Analysis／Marketplace **不得**參考 `Company.Infrastructure`；產品間禁止共用 DbContext／直連對方庫（AD-3／AD-11）。
- G-01～G-04 未關閉前禁止 Marketplace 業務 PR；見證表：`docs/product/analysis-gate-checklist.md`。
- 不提交祕密；不 force-push main；不擅自 commit／push（使用者明確要求除外）。

## Where things are

- 控制台：`src/AiProject.Console.*`；工作區：`src/AiProject.Company.*`；分析站：`src/AiProject.Analysis.*`；共用：`src/AiProject.Shared.*`
- 部署：`deploy/workspace/`、`deploy/analysis/`；對應 `docs/product/deploy-company.md`、`deploy-analysis.md`
- 解決方案：`AiProject.Console.slnx`

## Running and verifying

- 執行時／目標框架以專案檔的 `net10.0` 為準。
- 改哪個產品就測哪個：`dotnet test tests/AiProject.<Area>.*Tests/...`；全套用 `dotnet test AiProject.Console.slnx`（較久）。
- 分析站本機：`dotnet run --project src/AiProject.Analysis.Web`（預設 `:5200`）。

## Conventions that differ from defaults

- 分層：Domain ← Application ← Web／Infrastructure；規則不進 `.razor`；Application 開頭授權（工作區）。
- UI token 吃 `Shared.DesignSystem` semantic 變數；禁止新散落魔法 accent。
- 回報／公開契約以 `docs/product/reporting.md` 為準，勿發明 path／blob 上傳欄位。

## Known pitfalls

- 計劃書裡的「骨架／dry-run／記憶體 store」是**現況限制**，不是品質豁免；審查與實作須當缺口處理。
- 仲介是門檻產品：G 未關＝不做，不是「先做骨架再說」。

<!-- /bmad:context -->
