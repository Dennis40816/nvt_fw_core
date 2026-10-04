# nvt_fw_core
English | [中文](README.zh-TW.md)

The shared core for the NVT firmware tool family. **The shared core itself is still in the planning stage and has no code yet.** This repository currently contains planning documents and code for the first step, 「共用的 CI/CD 與治理框架」 ("shared CI/CD and governance framework"): the shared approval check [`actions/approval-check`](actions/approval-check/).

## What this repository is

NVT has three independent firmware tools:

| Abbreviation | Tool | Repo | Current version line |
|---|---|---|---|
| NFC | NVT FW Combiner | `Dennis40816/nvt_fw_combiner` | 1.2.x |
| NFH | Freeform Helper | `Dennis40816/nvt-freeform-helper` (the main development repository since 2026-10-04) | 1.3.x |
| NFU | NVT FW UTIL (formerly Event Buffer Replay) | `Dennis40816/nvt-event-buffer-replay` | 0.x |

The three tools have similar parts, such as startup, shell, settings, and diagnostics. This repository is used to plan their shared core, with the ultimate goal of integrating them into a single NVT FW Workstation.

The owner (Dennis)'s tentative goal, in his own words:

> 三個版本的 2.0.0 時都開始共用核心架構並在下一個重大版本更新推出整合型的 work station ("All three versions start using the shared core architecture at 2.0.0, and an integrated work station is introduced in the next major version update.")

## Current status (2026-10-05)

- Shared core: planning only; development has not started. Where the code will ultimately live (this repository, NFC's 2.0 mainline, or a shared package) has not been decided; see [docs/open-questions.md](docs/open-questions.md). Details will be discussed together after each project has its own prototype.
- Shared CI/CD and governance: the shared approval check `v0.1.0` has been released, and none of the three tool repositories has adopted it yet. NFH is the pilot, which will start after NFH's CI fixes and 1.3.2 are complete. See [docs/shared-ci/README.md](docs/shared-ci/README.md) for progress.

## Principles

1. **Adopt components into the shared core one by one as they mature.** After NFC completes development of the launcher, it will be applied directly to the other tools; other components will then be assessed for maturity one by one, and whichever project does each one best will contribute it for shared use.
2. **Continue investing in 1.x as usual.** The tools do not need to pause work during 1.x (0.x and 1.0 for NFU) for the sake of sharing.
3. **Have prototypes before discussing.** Before each project has its own prototype, do not write shared code or design ahead for other projects.
4. **Development priority:** NFC ≥ NFH > NVT Core > NFU.
5. **Domain-neutral.** The shared core does not contain firmware, Event Buffer, or Freeform business logic; code in NFC that determines output bytes (R3) does not enter the shared core. This is a planning assumption that has not yet been confirmed by the owner.

## Documents

- [docs/vision.md](docs/vision.md): goals, version cadence, stages
- [docs/components.md](docs/components.md): component comparison table (each of the three projects' implementations and maturity)
- [docs/decisions.md](docs/decisions.md): record of the owner's decisions
- [docs/open-questions.md](docs/open-questions.md): matters not yet decided
- [docs/shared-ci/README.md](docs/shared-ci/README.md): shared CI/CD and governance framework (inventory, proposal, progress)

## Who maintains this

Since 2026-10-03, the dedicated session 「NVT CORE」 has maintained this repository according to the owner's instructions. Changes to the planning content must be based on the owner's decisions and recorded in [docs/decisions.md](docs/decisions.md). Every PR requires the owner's approval before it can be merged.
