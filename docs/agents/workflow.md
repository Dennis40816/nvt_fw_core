# Roles and workflow

Owner makes scope and approval decisions. Commander collects questions and relays decisions. Project sessions plan work and integrate Codex worker results.

## Roles

| Role | Responsibility |
|---|---|
| Owner | Decides scope, feature ownership, exceptions, and pull request approval. |
| Commander | Collects questions, coordinates dependencies, and presents approval batches to owner. |
| Project session | Maintains project status, writes task briefs, verifies results, and prepares pull requests. |
| Codex worker | Implements the assigned Scope, checks Accept, and returns evidence to the project session. |

Commander relays authority from owner. A commander message alone does not authorize a decision or merge.

The project session responsible for Core integration controls shared files and merge order.

## Follow the conventions

New C# code and tests follow [conventions.md](../core/conventions.md) and [testing.md](../core/testing.md). Existing code is the baseline. It can only go down.

## Start within approved scope

Before building a new feature, the project session proposes either Core or project-specific ownership with a reason.

Send the proposal to commander as an owner question. Owner decides the classification.

Record clearly firmware-specific product logic as project-specific without another question.

Continue work within scope that owner already approved. Dispatch only the smallest change that satisfies the task.

Use existing mechanisms. Add abstractions, options, or extension points only when the approved requirement needs them.

## Route owner questions

1. The project session records the question and the decision that blocks work.
2. It sends commander an `[owner answer needed]` message with the question or a link to its complete text.
3. Commander presents related questions to owner in a batch.
4. Commander relays the answer verbatim with its decision time to the originating project session.
5. The project session records the decision and sends an `[owner answered]` message.

Include the following in each question:

- The question and the affected scope.
- Options and a recommended answer with its reason.
- The originating role and relevant evidence.
- A direct action link when owner must act on a page.

Continue independent work while an answer is pending. Clarify conditional or ambiguous answers before treating them as approval.

Keep confidential answers in private work records. Publish only their approved, sanitized meaning.

Use the approved message channel. If delivery fails, write the complete message to the existing inbox record and verify delivery later.

## Coordinate changes

Assign one writer to each task and each shared file. Use separate branches and worktrees for independent tasks.

Start from the agreed integration baseline. Keep each worker inside the paths named in its brief.

The integrating project session owns these shared files:

- Package declarations, build settings, and SDK settings.
- Solution files, lock files, and root configuration.
- Continuous integration workflows.

Workers request shared changes through their project session. The integrating project session applies approved changes.

For a module extraction, prepare a Core pull request and a separate adopting-project pull request.

Follow [dispatch](dispatch.md) for worker completion and [review](review.md) for review, approval, and module import acceptance.
