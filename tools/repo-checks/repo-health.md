# C# health enforcement (H02/H03)

PowerShell 7, Git and the installed SDK selected by `global.json` are required.
The syntax parser needs no modules, packages, restore or network access. Complete
enforcement uses the existing locked restore and host NuGet cache; it never restores. The embedded host is
compiled by that SDK and loads its `Roslyn/bincore` assemblies in an isolated
context. Measurement version: `roslyn-physical-v1`.

The checker sets `DOTNET_CLI_UI_LANGUAGE=en`, `VSLANG=1033` and `PreferredUILang=en-US` for every tool it starts, so
compiler and format messages do not change with the host language. It runs `dotnet build` and `dotnet msbuild` with
`-m:4 -nodeReuse:false`, and sets `MSBUILDDISABLENODEREUSE=1` so that `dotnet format` leaves no MSBuild node behind.

```powershell
./tools/repo-checks/repo-health.ps1 -Mode Measure -Repo core -Root . -OutputPath measurement.json
./tools/repo-checks/repo-health.ps1 -Mode Verify -Repo core -Root . -Solution Nvt.Core.sln -BaseRef origin/main
./tools/repo-checks/repo-health.ps1 -Mode LowerBaseline -Repo core -Root . -Solution Nvt.Core.sln -BaseRef origin/main
./tools/repo-checks/repo-health.ps1 -Mode Adopt -Repo core -Root . -Solution Nvt.Core.sln -BaseRef origin/main
```

`Repo` accepts `core`, `nfc`, `nfh`, `nfu`. Measure defaults to JSON on stdout;
`OutFile` aliases `OutputPath`. The default baseline is
`eng/code-health/baseline.json`; override it with `BaselinePath`.
Verify, LowerBaseline and Adopt require a committed baseline and Solution. Enroll is the
only production enrollment mode: it requires all providers to succeed, refuses any
existing baseline, and writes debt owned by NVT CORE with no due date. It also
generates the marked HealthBaselineWarningIds block in projects.props. Only Adopt adds
to an existing ledger, and only the debt that the Adopt section below allows. An absent baseline at the merge base fails
closed: first enrollment must be integrated before checking subsequent PRs against
that base. Measure remains syntax-only; it cannot satisfy the CI gate. Exit codes: **0** pass, **1** debt findings, **2**
tool/input error (including parse errors and unavailable stages).

Baseline fields are exactly `schemaVersion`, `measurementVersion`,
`snapshotCommit`, `limits`, `entities`, `findings`. Entity `ceilings` store measured
values; enforcement compares excess above each limit. Measurement entities expose
`values`, locations, line and `contentHash`; size summaries contain total, maximum,
oversized count and excess. Findings use rule/project/member/symbol/`syntaxHash`
and count, preserving identity across path renames. Symbol+location matches and
unique content hashes are reserved before remaining unique symbols; locations
disambiguate partial files with the same symbols. Ambiguous identities fail closed.
The example baseline may omit `viewTypeLines`; its effective limit is 800 without
adding a field during LowerBaseline.
Baseline entity ownership requires `owner`/`issue`; findings require `owner`.
`removeBy` is optional on a finding (owner decision of 2026-10-11: debt has no deadline). Fixes must
lower/delete debt in the same change. Debt cannot be added and its count cannot grow, with or without a date. The one exception is
Adopt in a synchronization-only change (see "Adopt" below).
When a finding has a `removeBy`, Verify compares it with today's date (UTC) in both ledgers. A finding is due
on its `removeBy` day and fails from the next day with `HC_STATE`, naming the finding. It lists the first 20
expired findings of a ledger and then one summary line with the count. A `removeBy` that is present and is
not a real calendar date is a bad ledger (exit 2). A finding without `removeBy` never expires. Structural
entities have no date, so they never expire. Core's two ledgers carry no `removeBy`.
LowerBaseline never moves, adds or removes a date. Adding, moving or removing a date is one JSON edit in a dedicated pull
request with an owner decision. The checker does not compare a date with the base ref, so the owner's review
of that pull request is the control.
The bundle file `schema.json` does not list `removeBy` as required, so a finding without it is valid in
every repository that has the new bundle. A repository with an older pin keeps the old schema, where
`removeBy` is required, until its pin synchronization. The checker of this change needs the schema of this
change: both move in the same synchronization. An application copies `repo-health.ps1` and the six bundle
files in the same pull request. A new checker with an old schema rejects a finding that has no `removeBy`
with `Invalid baseline document (schema)`.

The allowed baseline comes from `git show <merge-base>:<path>`, where the checker
computes the merge base of HEAD and BaseRef. Without BaseRef it reads HEAD's
committed baseline. It checks edited allowances against that baseline before
checking source. LowerBaseline refuses new debt before writing, then only removes
entries or lowers existing ceilings/counts; it preserves snapshotCommit.
Failures print `HC_STATE` or `HC_DIAGNOSTIC`, `file:line`, old/new values, entity
and contributing path. They never offset each other through repository totals.

| Metric | Recognizer |
| --- | --- |
| `asyncVoid` | Async void methods/local functions; semantic void-delegate conversions of async lambdas/anonymous methods, using SDK framework references, the SDK implicit usings and same-project source. When a scanned file names `Microsoft.VisualStudio.Threading.Analyzers`, a lambda that is a direct argument of an unresolved call is not a finding (the build reports it as VSTHRD101). Every other unresolved lambda remains a candidate. Without that name every unresolved conversion remains a candidate. |
| `blockingWait` | Syntax candidates: `.Result`/`?.Result`, `Wait`, `WaitAll`, `WaitAny`, and `GetAwaiter().GetResult()`. Domain results, signal waits and bridges need classification. |
| `stateMembers` | Mutable non-const/non-readonly field variables and auto/partial properties with set/init, per qualified type across partials. Positional record properties count; readonly record structs do not. Observable backing is counted once; partial properties deduplicate by name. Manual properties and readonly collections remain review items. |
| `fileLines` | Physical lines, including blank/comment/literal lines; limit 800. Final newline does not create an extra line. |
| `methodLines` | Full declaration span, including attributes, of methods, constructors, destructors, operators, conversions, accessors and local functions; limit 80, with the >150 split-plan review retained in the size summary. |
| `partialFiles` | Distinct files declaring each project-qualified type, including nested/generic types; limit 8. |
| `axamlCodeBehindLines` | Physical `.axaml.cs` lines; limit 150. |
| `viewTypeLines` | Aggregate declaration spans of AXAML `x:Class` types across partials; adjacent matching-name `.axaml.cs` is the fallback. The plan omits a threshold: use the file budget of 800. A code-only view (no `.axaml`) is a type that derives, directly or through a base type of the same project, from an Avalonia view base type: `Control`, `UserControl`, `TemplatedControl`, `ContentControl`, `ItemsControl`, `Window`, `Panel`, `Decorator`, `Border`, `Grid`, `StackPanel`, `DockPanel`, `WrapPanel`, `Canvas`, `Button`, `ToggleButton`, `ListBox`, `TextBox`, `ScrollViewer`, `Viewbox`, `ComboBox`, `TabControl`, `Slider`, `ProgressBar`, `CheckBox` or `RadioButton`. The checker has no Avalonia assemblies, so an unresolved base type counts by its simple name, and only in a project whose source mentions Avalonia (a `using` or a qualified name, also with `global::`). A resolved base type must be in the `Avalonia` namespace. The view is measured only when its span is strictly above 800 lines, so a small code-only view adds no entity. The file name decides nothing. Known limits: the name list is closed, and a view that derives from a base type of another project (an unresolved type with a name outside the list) is not found. Add the name to the list when a tool needs it. The 800-line budget of a code-only view is fixed, so a tightened `limits.viewTypeLines` applies only to AXAML views. A project-defined namespace that starts with `Avalonia.` counts as a mention. The branch for a resolved Avalonia base type has no fixture, because the fixtures have no Avalonia assemblies. |
| `suppressions` | SuppressMessage/UnconditionalSuppressMessage attributes (with check ID), each disable pragma plus every disabled ID (`ALL` for no IDs), evaluated NoWarn/WarningsNotAsErrors IDs when props contain those properties. MSBuild evaluates imports/conditions without building; default configuration only in H02. |
| `nativeImportDuplicates` | DllImport/LibraryImport (including resolved attribute aliases): exact DLL string + effective EntryPoint constant, falling back to method name. Report duplicate groups/excess and fingerprints of all participants. Unresolved constants fail. |
| `generationFields` | Mutable int/long/uint/ulong/Int32/Int64 fields whose names contain Generation/Revision/RequestId, ignoring case; increment/adoption semantics need review. |
| `fakeClockDuplicates` | Candidate TimeProvider descendants, ClockState, and Fake/Manual/Test-prefixed Clock/TimeProvider names. The `*.TestSupport` project is the reviewed owner (conventions T2) and is exempt. `*.TestSupport.Tests` and every other project are not. |
| `workspaceDuplicates` | Exact TestWorkspace types, or types directly containing Path.GetTempPath, Directory.CreateDirectory and Directory.Delete calls. Candidates require clone/ownership review. |
| `timeWaitsInTests` | In a test project, a call to `Task.Delay`, `Thread.Sleep` or `SpinWait.SpinUntil`. Use `SignalWait` or `ManualTimeProvider`. Own ledger: `test-debt.json`. |
| `elapsedAssertionsInTests` | In a test project, an `Assert.*` call whose arguments use an identifier containing `Elapsed` or `Stopwatch`. Own ledger. |
| `tempPathInTests` | In a test project, `Path.GetTempPath`, `Path.GetTempFileName` or `Directory.CreateTempSubdirectory`. Use `TestWorkspace`. Own ledger. |
| `sourceTextReadsInTests` | In a test project, a call to `ReadText`, `ReadAllText[Async]`, `ReadAllLines[Async]` or `OpenText`. Files under Architecture, Boundary, Layout or Snapshot names, and `Architecture.Tests` projects, are the sanctioned readers and are skipped. Own ledger. |
| `headlessSessionSetups` | In a test project, `UseHeadless`, `HeadlessUnitTestSession.StartNew` or `GetOrStartForAssembly`, and the `AvaloniaTestApplication` assembly attribute. Each test assembly needs one setup. Own ledger. |
| `childProcessInTests` | In a test project, `new Process`, `new ProcessStartInfo` and `Process.Start`. Use `ChildProcessFixture`. Own ledger. |
| `sameNameFakes` | In a test project, a non-partial type named `Fake`, `Stub`, `Spy`, `Mock`, `Recording` or `Dummy` plus an upper-case letter, declared in two or more files. Every participant is a finding. Own ledger. |
| `sourceTextAssertions` | Discovery: ReadText, ReadAllText/Lines/Bytes (including Async), OpenText; Contains/DoesNotContain/Matches/DoesNotMatch calls in the same file. All locations retained, with a separate Architecture.Tests or Tests+Architecture/Boundary/Layout/Snapshot filter flag. These counts do not prove source dependence. |

Tracked C# source is measured in Git trees; standalone fixture trees use all files.
Only bin/obj/artifacts/.git directories are excluded. Enforcement measures each
tracked source in every evaluated Compile owner, including linked files. It rejects
unowned tracked C# and discovers untracked projects too. Syntax candidate metrics
retain H02's SDK Latest/default preprocessor convention; diagnostic fingerprints
use evaluated Release DefineConstants and ResolveReferences. H02's semantic
candidate metrics still use SDK framework references and same-project source;
they do not prove complete semantic R1–R12 compliance.

Verify/LowerBaseline/Adopt/Enroll validate the local schema, bundle provenance, managed
EditorConfig and evaluated project settings, create artifacts/code-health, remove
stale named outputs, then run:

```text
dotnet build <solution> -c Release --no-restore --no-incremental -p:HealthCollectDiagnostics=true
dotnet format <solution> --verify-no-changes --no-restore --severity warn --report artifacts/code-health/format
dotnet format style <test.csproj> --verify-no-changes --no-restore --severity warn --diagnostics IDE0005 --report artifacts/code-health/format-style-<project>
```

Every project must produce fresh, readable SARIF 2.1.0. A failed build, analyzer
crash, invalid location, failed project load or missing report fails. SARIF warning
results enter the ledger; notes do not. RS0030's quoted symbol is parsed regardless
of UI language, then resolved with SDK Roslyn to a banned documentation ID. Reports
print HC_DIAGNOSTIC file:line, symbol, member and old/new counts. Build error rules
(including VSTHRD100 and CS4014) cannot be grandfathered or suppressed by generated
warning IDs. Apps must migrate their async void handlers before build/enrollment;
a syntax baseline cannot exempt an analyzer error. HealthPublicApi is set for
Core's four published projects in eng/code-health/projects.props (H07d). Each
project keeps its own PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt. The analyzer
package adds both files as AdditionalFiles. The bundle props add no second copy, because a
duplicate makes the dotnet format workspace load warn, and the format stage fails on that
warning. The target HealthRequirePublicApiFiles fails the build before compile when a file is
missing. The format stage runs after the build stage and does not run that target.

Analyzer warnings repeated by format after prospective edits reuse fresh SARIF
occurrences in source order, independently gated by the build ledger. They are not
counted twice. Actual format changes use rule FORMAT:<DiagnosticId>; their symbol is the diagnostic ID.
Exit 2 is accepted only with a valid nonempty report whose complete findings are
within the ledger (or the explicit first Enroll). Exit 0 still requires a valid
report. Other exits, project-load failures, missing/malformed/empty nonzero reports
fail. Diagnostic and format fingerprints group equal identities and preserve their
occurrence count; unrelated removals never offset additions.

Diagnostic syntaxHash v1 uses the token at the 1-based diagnostic line and UTF-16
column. Select its nearest StatementSyntax excluding BlockSyntax, else its nearest
MemberDeclarationSyntax, else the compilation unit. Join DescendantTokens().Text
in source order with one ASCII space, excluding trivia. Hash UTF-8 without BOM
with SHA-256, encoded as lowercase 64-digit hex. Before hashing, every identity
hash (token text, file contentHash, entity and duplicate hashes) replaces CRLF and
a lone CR with LF, so a CRLF and an LF checkout of the same source give the same
fingerprints. Member identity uses H02's Roslyn
qualified type, callable signature, nested/local member, property or field group.
Locationless project warnings use member MSBuild and the trimmed invariant message
as hash input. Syntax metrics retain roslyn-physical-v1's recognized-node tokens.
Pragma/attribute covered statements additionally use rule suppressionScopes with
the same statement-token hash. A wider span, added or substituted covered code
fails per fingerprint; narrowing/removal lowers debt. Protected error suppressions,
all-warning disables and whole-file RS0030 disables fail.
Path and line are display-only; rule/project/member/symbol/syntaxHash plus count
form the debt identity, so a file rename retains its allowance.

The versioned seam wrapper is `{ "schemaVersion": 1, "entries": [] }`. Entries use
the plan's path/member/symbol/owner/reason/kind/review fields. Match exact path and
symbol plus exact member; member `*` grants that symbol to a reviewed file owner.
Directories and symbol globs never match. kind is permanent-seam. Authorized
RS0030 counts remain separate from debt; Core starts with zero entries. Adding
existing debt as a permanent seam requires owner review, not mechanical migration.

The lock records `schemaVersion=1`, `coreCommit` and a `files` map.
The commit uses 40 lowercase hex digits.
Each SHA-256 uses 64 lowercase hex digits.
The map requires the six distributed filenames.
It also accepts `repo-health.ps1` as a verified file.
The manifest lists the checker in `verifiedFiles`.
The generator does not distribute the checker.
Hash inputs use raw LF UTF-8 bytes without a BOM.
No Git filters apply.
An old application lock without `repo-health.ps1` skips the checker hash and blob checks.
All six distributed file checks still apply.

An application supplies `-CoreRoot <local approved Core checkout>` for Git blob checks.
Each distributed copy must match its canonical blob at `coreCommit`.
The running checker (`$PSCommandPath`) must match `tools/repo-checks/repo-health.ps1` at that pin.
Its SHA-256 must also match the lock when the checker entry is present.
When the lock has the checker entry, a missing checker path, a BOM or a CR is a tool error.
A pull request cannot drop the entry that the base lock has.
A lock-only run with `-BaseRef` also rejects a lock that adds the entry to a base lock without it, because the base
lock has no hash to compare. That change is a pin synchronization with `-CoreRoot`.
Core also requires that the running script has the same bytes as `tools/repo-checks/repo-health.ps1` of its tree.
The checker trusts the checkout named by `-CoreRoot`.
The owner reviews changes to CI and the checker that select this checkout.
The Core checkout needs a full clone.
A shallow clone fails with `a full clone of Core is required`.
An application may move its pin only with `-CoreRoot`.
The new pin must be reachable from that checkout's HEAD.
It must descend from the previous pin.
The six copies must match the new pin.
The checker must match the new pin when its lock entry is present.

Without `-CoreRoot`, an application runs in lock-only mode.
Each distributed file must match its lock hash.
The running checker must match its lock hash when that entry is present.
With `-BaseRef`, every lock hash must equal the merge-base lock.
The pin must also equal the merge-base pin.
Without `-BaseRef`, the pin must equal the committed HEAD pin.
Application CI must always pass `-BaseRef`.
Without it, matching edits to a file and its lock hash are accepted.

Core compares each distributed copy with its canonical file and lock hash.
Core always requires the `repo-health.ps1` lock entry.
It compares that hash with `tools/repo-checks/repo-health.ps1` in the working tree.
The old application lock exception does not apply to Core.
A Core change updates the canonical file, any distributed copy, the manifest and the lock hash.
The pin may stay behind the tree.
A change cannot name its own commit.
The pin must belong to HEAD's history.
Core needs a full clone (`fetch-depth: 0`).
A moved pin must descend from the previous pin.
Its six canonical bundle files must match this tree.
Its checker blob must also match this tree.
Core ignores `-CoreRoot`.
The owner reviews the Core change.
The steps are in `csharp/README.md`.
No network operation occurs.
A pin change has its own synchronization review.
Integrate the approved pin before using it as the base for adoption changes.

The managed root block uses `# BEGIN CORE HEALTH MANAGED BLOCK` and
`# END CORE HEALTH MANAGED BLOCK`. Its interior must exactly equal the canonical
EditorConfig bytes, including LF. We conservatively reject changed protected
non-severity settings in descendant/root-tail configuration and accept only equal
or stronger protected severities. The rule is conservative on purpose: a protected
`[*.cs]` key such as indent_size with a different value is rejected in ANY section,
even one that cannot match `.cs` (for example `[*.json]`); move or delete such lines.
Outside the managed block (every descendant file, and the root file's tail after the
block) an EditorConfig must also not contain a generated_code key, a
dotnet_analyzer_diagnostic.* key, a dotnet_code_quality.* option (it narrows analysis
without changing a severity), or a dotnet_diagnostic.<ID>.severity of none, silent or
suggestion; warning and error stay allowed. The line reader follows Roslyn: it drops a
leading BOM, accepts `=` or `:` as the separator, and ignores a trailing `#` or `;`
comment. File names are matched case-insensitively. Only Core skips
`tools/repo-checks/csharp/.editorconfig`, because it is the canonical file that Core compares with its copy. Per-project Debug, Release and other declared
configurations must retain the shared settings, SDK WarningsAsErrors IDs, exact
ledger warning exemptions and approved analyzer package references/assets/versions.
NoWarn must equal the suppression ledger in every evaluated configuration.
Multiple target frameworks currently fail closed because the bundle ErrorLog
contract has only one path per project; apps must add distinct per-TFM outputs
before multi-target adoption.

Enroll once after the owner has approved the current-tree evidence:

```powershell
./tools/repo-checks/repo-health.ps1 -Mode Enroll -Repo core -Root . -Solution Nvt.Core.sln
```

### Adopt: debt that a rule change reveals

A pin synchronization can bring a checker that measures more than the old one, for example a
code-only Avalonia view above 800 lines. The measured excess has no ledger entry, and neither
Enroll (it refuses an existing ledger) nor LowerBaseline (it never adds) can register it.

`-Mode Adopt -BaseRef <ref>` registers it under one condition: **the change is synchronization-only**.
Every path that git reports as changed against the merge base must be one of these:

- `eng/core-health.lock.json`, `eng/code-health/baseline.json` and `eng/code-health/test-debt.json`,
- `eng/code-health/projects.props`, and then only the inside of the generated warning-ID block. The
  file must hold exactly one block, every group inside it must be a `PropertyGroup` for one project
  that sets only `HealthBaselineWarningIds`, and the rest of the file must equal the base, because
  every project imports it,
- under `eng/core-health/` and `tools/repo-checks/`, the bundle files by exact name
  (`Directory.Build.props`, `.editorconfig`, `schema.json` and `bundle-manifest.json`, `doc-sync.core.json`),
  and any `.ps1`, `.py` or `.txt` file. Any other file there is refused, for example a C#, AXAML,
  project, props, targets, `.globalconfig`, `.json` (such as a nested `global.json` or `stylecop.json`),
  `nuget.config` or lock file. The lock hash and the Core comparison guard the bundle files' bytes,
- any `tests/repo-checks/*.py`, and any `*.md`.

No other path may differ from the base. The reason is that the measurement then reads the same
sources, projects and imports as the base. An excess that the base ledger lacks was revealed by the checker or by the
bundle, not caused by this change. This replaces any per-file reasoning: a base type in another file, a
linked file, an AXAML mapping and a fingerprint that merges several files cannot matter when no source differs.

- A single other changed path, for example one edited test or one new class, refuses Adopt. Run Adopt in a
  pull request of its own, before the code changes.
- The root `.editorconfig` and a root `Directory.Build.props` are not synchronization paths. A change of the
  bundle `.editorconfig` makes the managed block of the root `.editorconfig` change too, so that pin
  synchronization cannot use Adopt in one pull request. The bundle `.editorconfig` has not changed since
  the bundle was added.
- The `*.md`, `.ps1`, `.py` and `.txt` rules assume that no project globs those files. A project that
  compiles or embeds one of them must not be listed here.
- A path that git prints with other text glued to it matches nothing, so the check fails closed. A path
  that holds a line break is refused.
- Git lists the changed paths of the whole repository. When `-Root` is not the repository root, Adopt
  is refused and Verify accepts no growth, because a change outside `-Root` would not be seen. The
  "Contributing change" line then names a repository-relative path.
- A finding of the evaluated project (member `MSBuild`: `NoWarn` and similar) is never registered this way.
- A fingerprint that the ledger already has keeps its count. A measured increase of it fails.
- Adopt only adds. It never raises a ceiling or a count that the ledger has, and Verify does not
  accept such a raise in a synchronization-only change either. It writes the measured value as the
  ceiling, with the Enroll owner and the issue `health-refactor/adopted`. When it registered something, it
  also refreshes the generated warning-ID block.
- In the pull request that also moves an application's pin, run Adopt with `-CoreRoot` as well:
  Adopt runs the bundle check first, and that check accepts a moved pin only when `-CoreRoot`
  proves it (the pin is a commit of that checkout and descends from the pin it replaces).
- It writes nothing when any other failure remains, and it requires `-BaseRef`.

Verify applies the same condition after it reads the ledger: growth against the base ledger is
accepted only in a synchronization-only change. Verify still requires the ledger to equal the
measurement, so a hand-raised ceiling fails with `run LowerBaseline`. Verify cannot tell Adopt output
from a hand edit that equals the measurement, so the owner reviews the added entries at the Files
changed tab of `eng/code-health/baseline.json`. LowerBaseline applies the same check, and then writes
the measured values. A hand-written new entry in a synchronization-only change is therefore accepted by
LowerBaseline and rewritten with the measured value, as Adopt would write it.

Two limits fail closed and refuse an Adopt that would be legitimate: a base `projects.props` with a BOM or a
lone CR compares unequal, and a `-Root` that is a symbolic link or an 8.3 name does not equal the
repository root.

Adopt runs after the build and format stages. A failure of those stages stops it first. New analyzer
warning IDs without a ledger entry and format findings cannot be adopted with it.

### Test-duplication ledger

The seven test rules above (`timeWaitsInTests` to `sameNameFakes`) answer to the testing
conventions in `docs/core/testing.md`. They use their own ledger,
`eng/code-health/test-debt.json` (`-TestDebtPath` changes the path), and never touch
`baseline.json`. Clock classes and temp-directory lifecycles stay with
`fakeClockDuplicates` and `workspaceDuplicates`.

A file is a test file when its path has a `tests` or `test` folder or a folder ending in
`.Tests`/`.Test`, or when its project name ends in `Tests`. A folder or project ending in
`.TestSupport` is exempt: it is the one place where these helpers are built.

- `-Mode EnrollTestDebt -Solution <sln>` writes the ledger once. It evaluates the projects
  (so a file linked into two test projects counts for each, as in Verify) but does not build.
  Without `-Solution` it attributes by the nearest project file, which can disagree with
  Verify. It refuses to overwrite a ledger. The owner follows Enroll. The ledger has no `removeBy`.
- Verify fails with `New test duplication` when a fingerprint appears or its count grows.
  It also fails with `run LowerBaseline` when a recorded fingerprint shrinks and the ledger
  was not lowered. LowerBaseline lowers the ledger and never adds to it.
- The ledger of the base ref is the ceiling. A ledger that grows, or is deleted, against
  the base ref fails. When the base ref has no ledger, the change is the first enrollment.
- When the ledger is missing and a test rule has findings, Verify fails and names
  `EnrollTestDebt`.
- A repository that pins this script must seed its own ledger in its gate pull request.
  `baseline.json` and the bundle schema do not change.

### First enrollment (seeding)

Enroll builds the solution. A build with TreatWarningsAsErrors=true fails on every
analyzer that has no ledger entry yet, so the first Enroll of a repository needs a
seed. Build once with `-p:TreatWarningsAsErrors=false`, collect the diagnostic IDs
from the SARIF output, and write them into the generated `HealthBaselineWarningIds`
block of `eng/code-health/projects.props`. Then run Enroll, which regenerates that
block from the ledger. Core's own enrollment used the seed
`CA1031;EnableGenerateDocumentationFile;IDE1006;RS0030`.

`-Owner <name>` sets the owner written to entities and findings. Without it, Core
uses `NVT CORE` and any other `-Repo` uses its upper-case name (nfc becomes NFC).
The issue stays shared (`health-refactor/2026-10-31`). Enroll writes no `removeBy`.
Verify checks owner and issue only as non-empty strings, so a hand edit is not
rejected, but `-Owner` is the supported way.

`asyncVoid` counts async void methods and local functions and also async lambdas or
anonymous methods that are converted to void delegates. Only VSTHRD100 (async void
methods) is an error in the bundle `.editorconfig`. Lambdas converted to void
delegates are counted in the ledger but do not fail the build.

The syntax measurement has framework references only. It adds the global usings that
`ImplicitUsings` gives at build time, so `Task`, `Func<Task>` and `Action` resolve. It has no NuGet or project
references, so a lambda can stay unresolved (an argument of an xunit or Avalonia call, for example).
The checker looks for the name `Microsoft.VisualStudio.Threading.Analyzers` in the root `Directory.Packages.props`
and `Directory.Build.props`, in `eng/core-health/Directory.Build.props` and in every `*.csproj`. The result applies
to the whole repository.
- Found: VSTHRD101 reports a real async void lambda in the build. An unresolved async lambda that is a direct
  argument of a call (`Assert.ThrowsAsync(async () => ...)`) is then not a finding. An unresolved lambda in any
  other position stays a candidate: a subscription (`x.Opened += async ...`), an assignment, an initializer, a
  return value, or a parenthesized or cast operand.
- Not found: nothing else would report it, so every unresolved async lambda stays a candidate.

An application that does not name the analyzer has to add it before it gets the relaxation. That is part
of its pin move.

The ledger stores H02 structural excess and syntax findings plus analyzer and
format debt. Entities use issue health-refactor/2026-10-31 (Adopt writes health-refactor/adopted); findings have no removeBy,
all with the Enroll owner (NVT CORE for Core). Verify checks evaluated HealthBaselineWarningIds
against build findings in the ledger, per project. LowerBaseline lowers both the
ledger and generated warning-ID block; it never enrolls. Tooling/function-only
loading supports provider tests, and never executes enrollment or verification.

## Run Verify on a developer machine

Use `tools/repo-checks/run-verify.ps1` for a local run of Verify. It does not change `repo-health.ps1`, so it is not part of the checker bundle.

```powershell
pwsh -NoProfile -File tools/repo-checks/run-verify.ps1 -Repo core -Solution Nvt.Core.sln -BaseRef origin/main
```

Verify can fail on code that did not change, for two reasons that come from the machine and not from the code:

- A build server that another process started earlier (`VBCSCompiler`) keeps its own UI culture. A build that reuses it can print a localized message, which is a new fingerprint. The environment variables of `repo-health.ps1` do not change a server that is already running.
- A build that finds old output can print no analyzer message, so a finding looks gone (for example `EnableGenerateDocumentationFile` goes from 1 to 0).

The script stops every build server and deletes the `bin` and `obj` folders that sit directly next to a `*.csproj` file of the repository. Then it restores with `--locked-mode` and runs Verify. CI runs on a fresh runner and does not need it.

Safety rules of the script:

- It stops the compile server of every session on the machine. A build that runs in another session can fail, so run it when no other build runs.
- `-Root` must be the top of the Git working tree. It defaults to the repository that contains the script. Any other value stops the script before it deletes anything.
- It skips a project below a link, a `bin` or `obj` that is a link, and a project outside the root. Links inside `bin` or `obj` are removed as part of the folder.

## Known limitations

An independent review of the first gate found six gaps. The gate does not close them yet. Each has an Issue.

1. Dennis40816/nvt_fw_core#163: analyzer configuration outside files named `.editorconfig` (`EditorConfigFiles`, `GlobalAnalyzerConfigFiles`, removed `Analyzer` items) is not checked.
2. Dennis40816/nvt_fw_core#164: sources and EditorConfig files under any `bin`, `obj` or `artifacts` folder escape coverage.
3. Dennis40816/nvt_fw_core#165: a baseline entity can be re-pointed, so one structural allowance can offset another.
4. Dennis40816/nvt_fw_core#166: `eng/code-health/seam-owners.json` and the project `HealthLayer` are not compared with the merge base.
5. Dennis40816/nvt_fw_core#167: suppressed analyzer results leave no fingerprint.
6. Dennis40816/nvt_fw_core#168: the format ledger depends on the checkout line endings (see "Checkout line endings").

Policy-change rule until these are fixed: a pull request that changes any of the files below is a policy change. The reviewer reads the change as policy, not as routine code, and the pull request says so.

- `eng/code-health/seam-owners.json`, `eng/code-health/projects.props` (including `HealthLayer`), `eng/code-health/baseline.json`
- `eng/core-health/*`, `eng/core-health.lock.json`, and the managed block in `.editorconfig`
- any project, `Directory.Build.*` or `.editorconfig` change that adds analyzer, EditorConfig or suppression inputs
- a tracked file under a `bin`, `obj` or `artifacts` folder

### Checkout line endings

Identity hashes ignore line endings, but the `dotnet format whitespace` ledger does not. Core enrolled the baseline on a CRLF checkout, which is the Git for Windows default and what a Windows runner produces. A pure LF checkout of the same commit reports different WHITESPACE counts (187 format fingerprints differ in this repository). Run Verify on the same checkout style that enrolled the baseline. A repository that enrolls on Windows and verifies on Linux must pin the checkout style first.

```text
HEALTH_SKIP_HOST_TESTS=1 python -B -m unittest discover -s tests/repo-checks -p test*.py -v
python -B -m unittest discover -s tests/repo-checks -p test_health_enforcement.py -k HostMutationTests -v
```

The fast suite has a 600-second process limit in host checks. The host mutation
test requires restored assets: whole Verify must exit 0, then a banned call in a
copy must exit 1. It never restores, changes branches or writes the shared index.
