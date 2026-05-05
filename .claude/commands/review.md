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

**Patch coverage** — run this block in order before reporting findings:

1. Collect unit-test coverage (net10, no Docker required):
   ```
   dotnet test --framework net10.0 --filter-not-trait "Category=Integration" --coverage --coverage-output-format cobertura --results-directory ./TestResults/review-cov 2>&1 | tail -5
   ```

2. Cross-reference the cobertura XML against the added lines in `src/` (PowerShell):
   ```powershell
   $diff = git diff HEAD -U0 -- src/
   $added = @{}; $cur = $null
   foreach ($ln in ($diff -split "`n")) {
       if ($ln -match '^\+\+\+ b/(.+)') { $cur = $Matches[1].Replace('\','/'); $added[$cur] = [System.Collections.Generic.HashSet[int]]::new() }
       elseif ($ln -match '^@@' -and $cur -and $ln -match '\+(\d+)(?:,(\d+))?') {
           $s=[int]$Matches[1]; $n=if($Matches[2]){[int]$Matches[2]}else{1}
           for($i=$s;$i-lt$s+$n;$i++){$added[$cur].Add($i)|Out-Null}
       }
   }
   $bad=@()
   foreach ($xf in (Get-ChildItem './TestResults/review-cov' -Filter '*.cobertura.xml' -Recurse -EA SilentlyContinue)) {
       $xml=[xml](Get-Content $xf.FullName)
       foreach ($cls in $xml.SelectNodes('//class')) {
           $fn=$cls.filename.Replace('\','/').TrimStart('/')
           if(-not $added.ContainsKey($fn)){continue}
           foreach ($l in $cls.SelectNodes('lines/line')) {
               if($added[$fn].Contains([int]$l.number)-and $l.hits-eq'0'){$bad+="${fn}:$($l.number)"}
           }
       }
   }
   if($bad){$bad}else{'OK - all new src/ lines covered.'}
   ```

3. Clean up: `Remove-Item -Recurse -Force ./TestResults/review-cov -ErrorAction SilentlyContinue`

Report any uncovered lines from step 2 as **Major** findings.

## Output format

For each issue found, output:

> **[Severity]** `path/to/file.cs:line` — short description
> ```
> // offending snippet (optional)
> ```
> Suggested fix or explanation.

Severity levels: `Critical` · `Major` · `Minor` · `Nit`

Finish with a one-paragraph summary verdict: approve, approve with minor comments, or request changes.
