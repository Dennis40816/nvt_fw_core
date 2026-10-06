# Open questions

All of these require the owner's decision. The owner said to wait until each project has its own prototype before discussing them together, so only questions and options are listed now, without conclusions.

## 1. Whether to split the shared core into multiple libs

On 2026-10-02, the owner said he had not thought this through yet.

- A single core package: version management is simple, but the three tools must upgrade together.
- Split into several libs by function (one each for launcher, shell, settings, and diagnostics): each tool can adopt only the parts it needs, in line with 「逐元件、誰成熟誰納入」 ("Component by component, bring in whichever is mature"); the cost is managing version dependencies.
- Start with one package and split it later.

## 2. Where to put the shared code

- This repo (`nvt_fw_core`).
- NFC's 2.0 trunk.
- Keep each component in its original repo and share it as a package or submodule.

Creating this repo first to hold the plans already leans toward the first option in practice, but no formal decision has been made yet.

## 3. How tools plug into the shared shell

- In-process plugins: tight integration, but an error in one tool affects the entire shell.
- Separate processes plus contracts (manifest, launch protocol): good isolation, and the code in NFC that determines the output bytes can remain independent; the cost is that the integrated experience needs to be designed separately.

## 4. Scope of the shared core

For the tentative assumption, see [vision.md](vision.md): only the shell and infrastructure are included, not each tool's business logic. The owner has not confirmed this yet.

## 5. Avalonia major version alignment

NFC 12.0.5, NFU 12.1.1, NFH 11.3.12. Core moves to 12.1.1 (owner, 2026-10-06), so NFC moves to 12.1.1 when it adopts Core UI, and NFH targets 12.1.1. There is no confirmed timeline yet for when NFH will upgrade to 12 or whether AvaloniaEdit has a corresponding version.

## 6. Naming

2.0.0 allows breaking restructuring. The current namespaces are `NvtFwCombiner.*`, `FreeformHelper.*`, and `Nvt.Replay.*`; the shared core's namespace and package naming have not been decided yet. The owner has decided that NFU's namespace will be 「這輪不改」 ("Unchanged this round"), which only applies to 1.x.

## 7. Maturity assessment criteria

[components.md](components.md) lists five proposals, with no owner decision yet.

## 8. Governance

The three tools' repos already use the same set of 「公版」 ("shared baseline") rules (ADR, approval check, ruleset). Since 2026-10-03, this repo itself has also adopted them: rulesets for `main` and `v*` tags, CODEOWNERS, and self-test CI.

The 2026-10-03 inventory found that the 「公版」 ("shared baseline") now only shares the same skeleton, and approval checks have split into two implementations, NFC and NFU. For the sharing proposal and the 10 questions requiring the owner's decision, see [shared-ci/proposal.md](shared-ci/proposal.md).

## 9. When to open a dedicated NVT Core session

Resolved: session 「NVT CORE」 opened on 2026-10-03, starting with the shared CI/CD and governance framework. Shared core code still has to wait until each project has a prototype.

## 10. Workstation composition and versions

On 2026-10-03, the owner decided that each tool can always be used and released independently, and Workstation is an additional integrated release (see [decisions.md](decisions.md)). Still undecided, to be asked at the prototype stage:

- Whether Workstation can be assembled with only selected tools.
- How Workstation versions correspond to each tool's versions.

## 11. CI cost rules for private repos

The owner's original words on 2026-10-03: 「另外那個 private 一直用 github action private repo 不應該頻繁觸發」 ("Also, that private repo keeps using github action; private repos should not trigger it frequently"). Background: FreeformHelper (private) was halted that same day by the Actions minutes or spending limit, with each PR running 6 Windows jobs. Actions in public repos (NFC, NFU, nvt_fw_core) do not count against minutes.

The owner added at 14:4x that same day (relayed by commander): 「所有測試都應該住在 public 執行才正確，對於 private 而言不應該執行太多的 PR 與 CI」 ("All tests should live and run in public; private repos should not have too many PRs or CI runs"). Which tests can run in public repos and how private repo code can be tested without making it public have not been decided yet.

Candidate approaches to include in the shared CI proposal, neither decided nor implemented yet:

- Set `concurrency` and `cancel-in-progress` in workflows so that a new push cancels the old run.
- Run only lightweight structure checks for documentation-only PRs.
- Make heavy jobs manually triggered or label-triggered.
- Use local validation outside the sandbox as the main gate, and push only a head that is ready to merge to trigger CI; combine small changes into fewer PRs.

Changing workflows is a `.github/workflows` change, which the App cannot push; the owner must decide and then push it.
