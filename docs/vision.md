# Vision and version cadence

## Goals

The owner's provisional goal, in the original words (2026-10-02):

> 三個版本的 2.0.0 時都開始共用核心架構並在下一個重大版本更新推出整合型的 work station ("All three versions start using the shared core architecture at 2.0.0, and an integrated work station is introduced in the next major version update.")

Interpretation:

- Starting with each tool's **2.0.0**, the three tools begin using the same shared core.
- The **next major version** (interpreted as 3.0.0) introduces the integrated **NVT FW Workstation**.
- **Each tool can always be used and released independently.** The owner's original words (2026-10-03 08:4x): 「我講清楚一點，就算有 workstation 每個功能還是可以獨立使用發布 其他部門可能只需要其中幾個 例如 NFC」 ("Let me make this clearer: even with a workstation, each function can still be used and released independently; other departments may only need a few of them, such as NFC."). Workstation is an additional integrated release and does not replace each tool's standalone versions; the shared core can only be a dependency bundled with the tool, cannot become a platform that requires Workstation to be installed first, and cannot depend on any of the tools.

## Each tool's path to 2.0.0

| Tool | Current version | Path | Notes |
|---|---|---|---|
| NFC | 1.2.x | 1.x → 2.0.0 | Develops the launcher |
| NFH | 1.3.x | 1.x → 2.0.0 | Avalonia needs to be upgraded from 11 to 12 |
| NFU | 0.x | 0.x → 1.0 → 2.0.0 | The owner's original words: 「照常 0.x → 1.0 → 2.0」 ("As usual, 0.x → 1.0 → 2.0"), without skipping version numbers |

Release cadence: the owner's original words, 「各自就緒就發，共用核心先到先採用」 ("Release each when ready; whichever reaches the shared core first adopts it first."). The three 2.0.0 releases do not have to happen at the same time.

## Approach

The owner's original words (2026-10-03):

> 應該說 launcher 這塊會由 NFC 開發後就套用 => 其他套件也可以探討是否已成熟，誰最好最納入共用的概念 ("More precisely, the launcher will be applied once NFC develops it => for other packages, we can also discuss whether they are mature, with the idea of bringing whichever is best into shared use.")

- **launcher**: Once NFC completes development, apply it directly to NFH and NFU.
- **Other components**: Do not predetermine who develops them. Later, evaluate the implementations in the three projects one by one and bring the most mature, best implementation into shared use. See [components.md](components.md).

The part of the owner's earlier statement (2026-10-02) that remains valid:

> 基本上我覺得邏輯上是由 NFC 訂好開發 launcher 等共用核心骨架…也不能說 1.x.x 不能投入，我比較 prefer 平行開發之後直接取代 ("Basically, I think the logical approach is for NFC to define and develop the shared core skeleton, such as the launcher... Nor can we say that we cannot invest in 1.x.x; I prefer parallel development followed by direct replacement.")

- Work on each tool can continue as usual during 1.x.
- The shared parts develop in parallel with 1.x and are swapped in directly at 2.0.0, rather than being extracted gradually during 1.x. 2.0.0 is a major version and allows breaking restructuring (namespaces, package boundaries).

## Provisional scope of the shared core

These are planning assumptions; the owner has not yet made a decision:

- Includes: launcher and version management/updates, the main UI framework (shell, navigation, tool registration), themes and fonts, settings storage, diagnostics and logging, and About and license pages.
- Excludes: firmware merging, Event Buffer replay and analysis, Freeform business logic; the code in NFC that determines output bytes.

## Stages

| Stage | Content | When |
|---|---|---|
| 0 | Planning only; each project continues development as usual and keeps a note of the general-purpose things it builds along the way | Now |
| 0.5 | **First step: shared CI/CD and governance framework** (share the ruleset and some CI/CD rules so that one change takes effect in all three). Start with an inventory and a proposal; see [shared-ci](shared-ci/) | Starts now (owner 2026-10-03) |
| 1 | Once each project has a prototype, jointly inventory the components, evaluate maturity, and decide which implementation to bring into shared use | When the owner says the prototypes are ready |
| 2 | Apply the launcher; bring the selected components into shared use; each tool swaps them in at 2.0.0 | Release each when ready |
| 3 | Integrated NVT FW Workstation | The next major version |

The owner's original words (2026-10-02): 「基本上是要先有專案各自雛型後再一起探討的」 ("Basically, each project needs to have its own prototype first, and then we discuss them together.").

## Development priorities

The owner's original words (2026-10-02):

> 我覺得這都很重要但目前開發優先級別是 NFC >= NFH > NVT Core (是否有分。lib ... 我還沒想清楚＞NFU ("I think all of this is important, but the current development priority is NFC >= NFH > NVT Core (whether to split into libs ... I have not thought it through yet) > NFU.")

NFC ≥ NFH > NVT Core > NFU. The shared core comes after NFC and NFH.
