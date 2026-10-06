# Handoff and status

Each project keeps one status table. Other documents link to its entries instead of maintaining another status table.

Use the project's existing status file. Owner has deferred a common path and format across projects.

## Work records

Use existing files or issue records for these purposes. Each record has one writer.

| Record | Purpose | Writer |
|---|---|---|
| Status table | Holds the authoritative project status and evidence links. | Project session |
| Task brief | Defines Scope, Accept, and worker boundaries. | Project session |
| Work in progress (WIP) note | Preserves context and the next steps for resuming work. | Project session |
| Inbox record | Preserves messages when the approved channel cannot deliver them. | Originating project session |
| Worker output | Preserves results, commands, logs, and failures for a task. | Assigned Codex worker |
| Review and approval record | Binds findings and decisions to a pull request head. | Assigned reviewer or owner |

Keep live records and their deployment locations outside the public repository. Use sanitized evidence when a pull request needs a public reference.

## Save a resumable WIP note

Update the WIP note after each work batch and before a long wait, owner question, handoff, or context compaction.

Mark the current summary clearly. Link to the status table instead of copying its status entries.

Preserve the following:

- Owner decisions, exact wording, decision times, and remaining questions in the private record.
- The active task brief and constraints that affect the next action.
- Background task identifiers, branches, full head commit SHAs, and output references.
- The queue record and evidence needed to inspect running or failed work.
- Full pull request URLs and links to their status and review records.
- Verification commands, results, and checks that remain incomplete.
- The ordered next steps and the role responsible for each step.

## Resume or transfer work

1. Read the current repository rules, task brief, and current WIP summary.
2. Open the status table entries and owner decisions that govern the task.
3. Check the current branch, head, background jobs, and evidence against the saved references.
4. Confirm the current writer before continuing or transferring edits.
5. Continue the first incomplete step and update the WIP note with any changed facts.

The handoff is complete when the receiving role can locate the evidence and identify the next action without reconstructing the conversation.

Preserve failed outputs before retrying. Resume an existing task when possible to avoid duplicate dispatch.
