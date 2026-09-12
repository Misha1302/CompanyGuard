# CompanyGuard

**Stop reviewing the same .NET rule twice.**

CompanyGuard is a Roslyn analyzer prototype for enforcing company-specific API usage rules at compile time. It turns rules that normally live in ADRs, documentation, and code review into semantic IDE/build diagnostics.

## Problem

Generic analyzers cannot know that an internal low-level payment gateway may only be used from an infrastructure layer, or that a legacy company API must not escape a bounded context.

## Example

`companyguard.json`:

```json
{
  "version": 1,
  "rules": [{
    "name": "Raw payment API boundary",
    "symbolId": "M:Acme.Payments.RawPaymentGateway.ChargeAsync(System.Decimal)",
    "allowedNamespacePrefixes": ["Acme.Payments.Infrastructure"]
  }]
}
```

Using that API from `Acme.Orders` produces `CG001`. Aliases and fully-qualified names do not bypass the rule because matching is based on Roslyn symbols rather than source text.

## Current MVP

- `companyguard.json` via MSBuild `AdditionalFiles`
- Documentation Comment ID → semantic `ISymbol` resolution
- methods, constructors, properties and fields via `IOperation`
- namespace-component boundary checks
- `CG001` restricted API diagnostic
- `CG000` invalid/unresolved configuration diagnostic
- concurrent execution and generated-code exclusion
- sample projects and CI

## Run

```bash
dotnet restore CompanyGuard.slnx
dotnet build CompanyGuard.slnx
```

The sample intentionally contains a violation so `Acme.Application` demonstrates the analyzer diagnostic.

## Scope

The first milestone intentionally implements one rule family: `RestrictedApi`. This is a portfolio/commercial vertical slice, not a universal policy DSL.

## Next milestone

- complete analyzer test matrix
- safe static-symbol replacement CodeFix + Fix All
- NuGet analyzer packaging
- analyzer timing benchmark
- short before/after demo
