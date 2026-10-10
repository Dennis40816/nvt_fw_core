# Where project records live

Applies to all NVT repositories. New content follows this page from the first PR. Existing content is cleaned up in separate PRs.

## Principle

A product repository holds only what a future developer needs:

- code and tests
- user and developer documentation
- ADRs
- the CHANGELOG

Process records stay in GitHub or in the working folder of the person or agent who made them.

## One home for each kind of record

| Content | Home | Do not |
|---|---|---|
| Bugs, to-dos, tracked items | A GitHub Issue with a label. Close the issue when it is fixed | Keep one Markdown file per bug in the repository |
| Review and approval records | The PR itself: reviews and the review record comment | Write a separate JSON record or attestation file |
| Screenshots, mockups, proposal images | The PR conversation | Commit proposal images v4, v5, v6 into the repository |
| Design decisions | Write an ADR for a major decision. Write a minor decision in the one decisions table of the repository. Mark it in place when a later decision replaces it | Spread one decision over several documents |
| Handoff notes, work-in-progress notes, progress snapshots | The working folder, outside the repository | Stack handoff documents by version under `docs/` |
| Version changes | The CHANGELOG and the GitHub Release | Write a status document for each version |
| Questions that wait for a maintainer's answer | One open-questions file per repository. Move the entry to the decisions table after the answer | Write the same question in several places |

## Lifecycle

- Every status file names a responsible person and an end condition. When the end condition is met, delete the file. Git history keeps it. Do not create an archive folder.
- A validation script must not pin a history record. A pinned record cannot be deleted, so records only pile up.
- Before you add a new kind of record or a new folder for records, check the table above. If no home fits, add the question to the open-questions file. Do not invent a new home.
- Before you commit a binary file (an image or a spreadsheet), explain why the PR conversation is not enough. Keep only the final version of each image.
- Automation under `tools/` may stay in the repository. It must have a README.

## Health indicators

Check these in a periodic repository review:

- In the last 14 days, did the lines added to documentation exceed the lines added to `src`? If yes, explain why.
- The total size of binary files under `docs/`.
- The number of open bug files or open bug issues.
