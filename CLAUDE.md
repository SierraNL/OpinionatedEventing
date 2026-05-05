# CLAUDE.md — OpinionatedEventing

See [AGENTS.md](AGENTS.md) for the full project context, design rules, conventions, and workflow.

## Claude-specific

- **Before committing:** always run `/review` on staged changes and address any findings.
- **GitHub interactions:** use the `gh` CLI for all GitHub operations (issues, PRs, comments, etc.). The repository is `SierraNL/OpinionatedEventing` (case-sensitive — the `O` and `E` are uppercase).

## Implementing an issue (mandatory steps — do not skip)

1. `git fetch origin && git reset --hard origin/main` — start clean from main
2. Create branch: `git checkout -b issue/<number>-<short-description>` (e.g. `issue/42-fix-outbox-retry`)
3. Present a plan and wait for explicit approval before writing any code (see AGENTS.md § Planning)
4. Implement, then run `/review` on staged changes before committing
5. Ensure every new line in `src/` is covered by a unit test — CI enforces this via `codecov/patch`
6. Push the branch first (`git push -u origin <branch>`), then open the PR: `gh pr create --repo SierraNL/OpinionatedEventing --title "..." --body "..."` — `gh pr create` will fail if the branch has not been pushed yet
7. After pushing, wait for all CI checks to finish (`gh pr checks <number> --watch`), then verify the `codecov/patch` check passes — if it fails, add tests to cover the uncovered lines before considering the PR done
