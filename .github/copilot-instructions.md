## pilotfish / Agent Orchestration

Repository-local brake over the global pilotfish policy (`~/.claude/CLAUDE.md`, v1.3.10).
For this repository, do not delegate by default.

Discretionary delegation requires at least one of these conditions:
- repository-wide exploration is required;
- stable same-shape mechanical repetition over more than five files, fully
  specifiable in one brief (the count is a repo-local floor; pilotfish itself
  qualifies on shape and brief stability, not on a number);
- the task can be specified without architectural ambiguity;
- fresh-context independence is worth more than the cost of reconstructing context.

Keep architecture decisions, reverse-engineering conclusions, rendering decisions,
engine/editor architecture, coupled single-bug investigation, and small localized
changes in the main session.

This brake governs discretionary delegation only; it does not waive the
risk-triggered review gates. Security or trust boundaries, destructive /
irreversible / external mutation, data / schema / serialization / migration work,
releases, and material cross-component acceptance make `plan-verifier` (before
approval) and `verifier` (after implementation) mandatory. Reverse-engineering
conclusions and PSX-to-C# port fidelity calls are not by themselves risk triggers.

Role usage:
- `scout` / `Explore` — broad code discovery only; their findings are inputs, not verified outputs;
- `plan-verifier` — read-only readiness review of one Plan envelope or slice before approval; returns READY or REVISE;
- `security-reviewer` — read-only pre-approval security analysis; security work never goes to the general executors;
- `mech-executor` — fully specified mechanical edits, as one collected worker rather than a fan-out of writers;
- `executor` — implementation tasks with clear scope and done criteria;
- `security-executor` — approved security work only, after `security-reviewer`;
- `verifier` — non-trivial completed changes before reporting them done; returns CONFIRMED, REFUTED, or INCONCLUSIVE.

Invoke named roles without a `model` argument: their routing lives in the agent
definitions. Only a truly ad-hoc agent sets `model` explicitly — never let a
fan-out inherit the main-session model.

# Shell tools available

The repository is developed on Windows.

Available tools:
- `rg` / ripgrep is installed and should be preferred for fast code search.
- `rtk` is installed and should be used when command output may be large or noisy.
- `fd` is installed and must be preferred for file discovery.
- `jq` is installed and must be used for JSON inspection.
- `yq` is installed and must be used for YAML/XML/INI/CSV inspection.
- `ast-grep` is installed and should be used for structural code search when plain text search would be too noisy.

Usage rules:
- Prefer `rg "pattern" .` for precise code search.
- Prefer `rtk rg "pattern" .` when the search may return a lot of output.
- Prefer `rtk git status`, `rtk git diff`, and `rtk git log` for Git commands.
- If `rtk` fails or is unavailable, fall back to the normal command.
- If `rg` fails or is unavailable, fall back to VS Code search or PowerShell search.
- Never run broad recursive listing commands like `dir /s`, `tree /f`, or unfiltered `Get-ChildItem -Recurse`.
- Prefer `fd` with extension and depth filters.
- Prefer `rg` with path, glob, context, and file-type filters.
- Prefer `jq`/`yq` to extract only relevant fields from structured files.

At the start of a task, the agent may verify tools with:
- `rg --version`
- `rtk --version`
- `rtk gain`
- `fd --version`
- `jq --version`
- `yq --version`
- `ast-grep --version`

# RTK usage rules

Before running shell commands, prefer RTK-wrapped commands to reduce context noise.

Use:
- `rtk git status` instead of `git status`
- `rtk git diff` instead of `git diff`
- `rtk git log -n 20` instead of `git log -n 20`
- `rtk grep "pattern" .` instead of `rg "pattern" .` when the output may be large
- `rtk test <command>` for verbose test commands
- `rtk dotnet test` or `rtk test "dotnet test"` for .NET test output if supported

At the start of a task, verify RTK is available with:
- `rtk --version`
- `rtk gain`

If RTK is not available, fall back to normal commands.
