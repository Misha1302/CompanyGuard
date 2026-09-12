using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CompanyGuard.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestrictedApiAnalyzer : DiagnosticAnalyzer
{
    public const string RestrictedApiId = "CG001";
    public const string InvalidConfigId = "CG000";

    private static readonly DiagnosticDescriptor RestrictedApiRule = new(
        RestrictedApiId,
        "Restricted API usage",
        "{0} is restricted by {1}. Allowed namespaces: {2}",
        "CompanyGuard.Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidConfigRule = new(
        InvalidConfigId,
        "Invalid CompanyGuard configuration",
        "{0}",
        "CompanyGuard.Configuration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RestrictedApiRule, InvalidConfigRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var parse = ParseConfiguration(startContext);
            if (parse.Error is not null)
            {
                startContext.RegisterCompilationEndAction(endContext =>
                    endContext.ReportDiagnostic(Diagnostic.Create(InvalidConfigRule, Location.None, parse.Error)));
                return;
            }

            var resolvedRules = ResolveRules(startContext.Compilation, parse.Rules!, out var resolutionError);
            if (resolutionError is not null)
            {
                startContext.RegisterCompilationEndAction(endContext =>
                    endContext.ReportDiagnostic(Diagnostic.Create(InvalidConfigRule, Location.None, resolutionError)));
                return;
            }

            if (resolvedRules!.Count == 0)
                return;

            startContext.RegisterOperationAction(c => Analyze(c, resolvedRules),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, ImmutableDictionary<ISymbol, ResolvedRule> rules)
    {
        ISymbol? symbol = context.Operation switch
        {
            IInvocationOperation i => i.TargetMethod,
            IObjectCreationOperation o => o.Constructor,
            IPropertyReferenceOperation p => p.Property,
            IFieldReferenceOperation f => f.Field,
            _ => null
        };

        if (symbol is null || !rules.TryGetValue(symbol.OriginalDefinition, out var rule))
            return;

        var ns = context.ContainingSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (rule.AllowedNamespacePrefixes.Any(prefix => IsNamespaceWithin(ns, prefix)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            RestrictedApiRule,
            context.Operation.Syntax.GetLocation(),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            rule.Name,
            string.Join(", ", rule.AllowedNamespacePrefixes)));
    }

    public static bool IsNamespaceWithin(string actual, string prefix) =>
        actual == prefix || actual.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static ParseResult ParseConfiguration(CompilationStartAnalysisContext context)
    {
        var file = context.Options.AdditionalFiles.FirstOrDefault(f =>
            string.Equals(System.IO.Path.GetFileName(f.Path), "companyguard.json", StringComparison.OrdinalIgnoreCase));
        if (file is null)
            return new ParseResult(Array.Empty<RuleConfig>(), null);

        try
        {
            var text = file.GetText(context.CancellationToken)?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return new ParseResult(null, "companyguard.json is empty.");

            var config = JsonSerializer.Deserialize<GuardConfiguration>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config is null || config.Version != 1)
                return new ParseResult(null, "Unsupported or missing configuration version. Expected version 1.");
            if (config.Rules is null)
                return new ParseResult(null, "Missing rules collection.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in config.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Name)) return new ParseResult(null, "Rule name is required.");
                if (!names.Add(rule.Name)) return new ParseResult(null, $"Duplicate rule name: {rule.Name}.");
                if (string.IsNullOrWhiteSpace(rule.SymbolId)) return new ParseResult(null, $"Rule {rule.Name} requires symbolId.");
                if (rule.AllowedNamespacePrefixes is null || rule.AllowedNamespacePrefixes.Length == 0)
                    return new ParseResult(null, $"Rule {rule.Name} requires at least one allowedNamespacePrefix.");
            }
            return new ParseResult(config.Rules, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new ParseResult(null, $"Invalid companyguard.json: {ex.Message}");
        }
    }

    private static ImmutableDictionary<ISymbol, ResolvedRule>? ResolveRules(
        Compilation compilation,
        IReadOnlyList<RuleConfig> rules,
        out string? error)
    {
        var builder = ImmutableDictionary.CreateBuilder<ISymbol, ResolvedRule>(SymbolEqualityComparer.Default);
        foreach (var rule in rules)
        {
            var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(rule.SymbolId!, compilation);
            if (symbol is null)
            {
                error = $"Unable to resolve symbolId {rule.SymbolId} for rule {rule.Name}.";
                return null;
            }
            builder[symbol.OriginalDefinition] = new ResolvedRule(rule.Name!, rule.AllowedNamespacePrefixes!);
        }
        error = null;
        return builder.ToImmutable();
    }

    private sealed class ParseResult
    {
        public ParseResult(IReadOnlyList<RuleConfig>? rules, string? error) { Rules = rules; Error = error; }
        public IReadOnlyList<RuleConfig>? Rules { get; }
        public string? Error { get; }
    }

    private sealed class ResolvedRule
    {
        public ResolvedRule(string name, string[] allowedNamespacePrefixes) { Name = name; AllowedNamespacePrefixes = allowedNamespacePrefixes; }
        public string Name { get; }
        public string[] AllowedNamespacePrefixes { get; }
    }
    private sealed class GuardConfiguration { public int Version { get; set; } public RuleConfig[]? Rules { get; set; } }
    private sealed class RuleConfig { public string? Name { get; set; } public string? SymbolId { get; set; } public string[]? AllowedNamespacePrefixes { get; set; } }
}
