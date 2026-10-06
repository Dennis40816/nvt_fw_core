# Dispatch

Dispatch a Codex worker only when its brief defines a bounded Scope and checkable Accept criteria.

## Prepare the brief

The project session supplies the following:

| Field | Required content |
|---|---|
| Scope | The required behavior and exact files or folders the worker may change. |
| Out of scope | Adjacent work and shared files that the worker must leave unchanged. |
| Baseline | The agreed base branch or commit and any frozen source reference. |
| Accept | Observable results, exact verification commands, and required evidence. |
| Constraints | Repository rules, public hygiene, licensing, and dependency restrictions. |
| Return | Changed files, results, evidence references, remaining questions, and the next responsible role. |

Use [workflow](workflow.md) to settle feature ownership and shared-file changes before dispatch.

For an extraction, record the source ref, full commit SHA, and file paths before implementation starts.

Keep private source details in private records. Publish only approved provenance.

Require the smallest change that meets Scope and Accept. Keep product behavior and product data in the source project.

## Queue states

| State | Meaning |
|---|---|
| `proposed` | The task needs scope, dependency, or acceptance checks. |
| `ready` | The task has a complete brief, required scope decisions, and no writer conflict. |
| `running` | One Codex worker has claimed the task. |
| `done` | The worker finished successfully and preserved its result and evidence. |
| `failed` | The worker could not finish and preserved the failure evidence. |

The normal sequence is `proposed` → `ready` → `running` → `done` or `failed`.

Keep a task in `proposed` while a required owner decision or overlapping change blocks it.

Check the local execution environment before dispatch. Keep execution settings outside the public repository.

Pause new dispatch when the environment cannot run the task or preserve its evidence. Save WIP before waiting.

## Verify the returned result

The project session checks every Scope and Accept item against the changed files and evidence.

Record exact commands and results. Report unrun checks and their reason.

A `done` queue state records worker completion. Review, owner approval, and module import acceptance remain separate steps.

Before retrying a failed task, inspect its evidence and correct the brief or blocking condition. Preserve the previous attempt's output.

Request [review](review.md) only after the project session has verified the result. Link the result from the single project status table.
