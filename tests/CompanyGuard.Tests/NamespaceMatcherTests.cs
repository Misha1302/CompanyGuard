using CompanyGuard.Analyzers;
using Xunit;

namespace CompanyGuard.Tests;

public sealed class NamespaceMatcherTests
{
    [Theory]
    [InlineData("Acme.Payments.Infrastructure", "Acme.Payments.Infrastructure", true)]
    [InlineData("Acme.Payments.Infrastructure.Services", "Acme.Payments.Infrastructure", true)]
    [InlineData("Acme.Payments", "Acme.Payments.Infrastructure", false)]
    [InlineData("Acme.Payments.Infrastructure2", "Acme.Payments.Infrastructure", false)]
    public void Matches_namespace_components(string actual, string prefix, bool expected)
        => Assert.Equal(expected, RestrictedApiAnalyzer.IsNamespaceWithin(actual, prefix));
}
