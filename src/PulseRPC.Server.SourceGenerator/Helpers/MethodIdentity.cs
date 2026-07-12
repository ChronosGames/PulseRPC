using System.Linq;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Server.SourceGenerator.Helpers;

/// <summary>
/// Stable CLR method identity for generator lookup and de-duplication.
/// The canonical protocol signature remains separate so existing wire IDs do not change.
/// </summary>
internal static class MethodIdentity
{
    private static readonly SymbolDisplayFormat IdentityTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string CreateLookupKey(IMethodSymbol method)
    {
        var declaringInterface = method.ContainingType.ToDisplayString(IdentityTypeFormat);
        return $"{declaringInterface}|{CreateImplementationSignatureKey(method)}";
    }

    public static string CreateImplementationSignatureKey(IMethodSymbol method)
    {
        var parameters = method.Parameters
            .Where(parameter => !IsCancellationToken(parameter.Type))
            .Select(parameter => $"{parameter.RefKind}:{parameter.Type.ToDisplayString(IdentityTypeFormat)}");

        return $"{method.Name}`{method.Arity}({string.Join(",", parameters)})";
    }

    public static string CreateClrSignatureKey(IMethodSymbol method)
    {
        var parameters = method.Parameters
            .Select(parameter => $"{parameter.RefKind}:{parameter.Type.ToDisplayString(IdentityTypeFormat)}");

        return $"{method.Name}`{method.Arity}({string.Join(",", parameters)})";
    }

    public static bool IsCancellationToken(ITypeSymbol type)
        => type.Name == "CancellationToken" &&
           type.ContainingNamespace?.ToDisplayString() == "System.Threading";
}
