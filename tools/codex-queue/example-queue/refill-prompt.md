Read the reviewed instructions in <REPO> and inspect <TRUNK_WT> without changes or network access.
Use only the approved work list at <WORK_LIST>. Check <QUEUE> to avoid duplicate briefs.
Write at most <MAX_BRIEFS> independent briefs. Use the approved acceptance commands at <ACCEPT_COMMANDS>.
Replace every placeholder before use. Do not invent work or permissions.

Use this format for each brief:

```text
=====BRIEF <SLUG>=====
Title: <TITLE>
Risk: <RISK_LEVEL>
Goal: <GOAL>
Scope:
<REPOSITORY_RELATIVE_PATH>
Accept:
$ <ACCEPT_COMMAND>
Not in scope: <EXCLUSIONS>
```
