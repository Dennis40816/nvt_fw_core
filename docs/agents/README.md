# Agent workflow

Owner approves decisions and pull requests. Commander coordinates project sessions. Project sessions use Codex workers to deliver bounded tasks.

Each pull request receives an independent Codex first review and a Claude second review before owner approval.

These documents describe the generic workflow for Core and adopting projects. Keep local setup and live work records outside this public repository.

[Traditional Chinese](README.zh-TW.md)

## Read the document for the current step

| When | Document |
|---|---|
| Assign roles, classify a feature, or request an owner decision | [Workflow](workflow.md) |
| Save progress, transfer work, or resume after context compaction | [Handoff and status](handoff.md) |
| Prepare a task brief, dispatch a worker, or inspect its result | [Dispatch](dispatch.md) |
| Review a pull request, request approval, or prepare a merge | [Review and approval](review.md) |
| Assess a proposed skill or plugin before installation | [Skill assessment](skills.md) |

## Apply the repository rules

Read [CONTRIBUTING.md](../../CONTRIBUTING.md) with these documents. Adopting projects copy or link the rules in their adoption pull request.

Use English for repository text. Update English and Traditional Chinese README files together. Add other translations only when owner requests them.

Publish generic roles and sanitized evidence. Exclude the following from public files, history, and artifacts:

- Local absolute paths, user names, machine names, and account names.
- Application identifiers, secrets, and credentials.
- Private repository names, private data, and private project details.
- Personal usage limits, live queue state, and local execution settings.
- Session names and identifiers that disclose a deployment.

Use role names when a public record must identify a participant.
