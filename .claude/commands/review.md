Review the staged and unstaged changes in this repository as if you were a senior .NET engineer performing a pull request review. Focus on correctness, not style.

## What to check

**Architecture & contracts**
- Do changes respect the abstractions/infra separation (`OpinionatedEventing.Abstractions` must have no infrastructure dependencies; `OpinionatedEventing` root package may depend on `Microsoft.Extensions.*`)?
- Is `IPublisher` → `IOutboxStore` the only outbound path (no direct broker publishes)?
- Do domain events on aggregates go through `DomainEventInterceptor`, not `IPublisher`?
- Are `ICommand` / `IEvent` marker interfaces used correctly (commands point-to-point, events fan-out)?

**Correctness**
- Are all async methods returning `Task` (not `void`) with a `CancellationToken` parameter?
- Is there any static mutable state?
- Are `SaveChanges` / outbox writes atomic within the caller's transaction?
- Does saga state get persisted correctly, and are saga timeouts driven by `TimeProvider`?

**Safety & security**
- Any command injection, SQL injection, or other OWASP top-10 issues?
- Any secrets or credentials in code or config?

**Dependencies**
- Are new `<PackageReference>` entries missing a `Version` attribute (central package management)?
- Does any `src/` library take a new dependency on Serilog, NLog, OTel packages, or a mocking framework?
- Is serialisation always via `System.Text.Json`?
- Is logging always via `ILogger<T>`?
- Is tracing/metrics via `ActivitySource` / `Meter` only?

**Tests**
- Are integration tests tagged `[Trait("Category", "Integration")]`?
- Are hand-written fakes used instead of mocking frameworks?
- Do new public members in `src/` have XML doc comments?

**Nullable & warnings**
- Is `#nullable enable` respected (no `!` suppressions without a comment explaining why)?
- Would `TreatWarningsAsErrors=true` pass cleanly?

## Output format

For each issue found, output:

> **[Severity]** `path/to/file.cs:line` — short description
> ```
> // offending snippet (optional)
> ```
> Suggested fix or explanation.

Severity levels: `Critical` · `Major` · `Minor` · `Nit`

Finish with a one-paragraph summary verdict: approve, approve with minor comments, or request changes.
