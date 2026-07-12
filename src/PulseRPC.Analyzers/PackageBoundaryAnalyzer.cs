using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PulseRPC.Analyzers;

/// <summary>
/// Enforces the dependency, namespace, and public implementation boundaries of the core packages.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PackageBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string ForbiddenReferenceDiagnosticId = "PRPC2001";
    public const string NamespaceOwnershipDiagnosticId = "PRPC2002";
    public const string PublicImplementationDiagnosticId = "PRPC2003";

    private const string AbstractionsAssembly = "PulseRPC.Abstractions";
    private const string SharedAssembly = "PulseRPC.Shared";
    private const string CanonicalAbstractionsNamespace = "PulseRPC.Abstractions";
    private const string Category = "PulseRPC.Architecture";

    private static readonly string[] ImplementationSuffixes =
    {
        "Backplane",
        "Buffer",
        "Controller",
        "Directory",
        "Factory",
        "Manager",
        "Metrics",
        "Pool",
        "Provider",
        "Scheduler",
        "Store",
        "Transport"
    };

    private static readonly DiagnosticDescriptor ForbiddenReferenceRule = new(
        ForbiddenReferenceDiagnosticId,
        "Abstractions cannot reference an implementation package",
        "Assembly '{0}' cannot reference implementation assembly '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PulseRPC.Abstractions is the bottom contract layer and cannot reference another PulseRPC runtime assembly.");

    private static readonly DiagnosticDescriptor NamespaceOwnershipRule = new(
        NamespaceOwnershipDiagnosticId,
        "Public type is declared in a namespace owned by another package",
        "Public type '{0}' in assembly '{1}' must not be added to namespace '{2}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Existing shipped types retain their namespace for compatibility, but new public types must follow package ownership.");

    private static readonly DiagnosticDescriptor PublicImplementationRule = new(
        PublicImplementationDiagnosticId,
        "Abstractions cannot add a public implementation type",
        "Public type '{0}' is implementation-shaped and must be internal or moved out of PulseRPC.Abstractions",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PulseRPC.Abstractions contains contracts and DTOs; runtime managers, pools, transports, and contract implementations belong in an implementation package.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ForbiddenReferenceRule, NamespaceOwnershipRule, PublicImplementationRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var assemblyName = startContext.Compilation.AssemblyName;
            if (assemblyName is not (AbstractionsAssembly or SharedAssembly))
            {
                return;
            }

            var shippedTypes = ReadShippedTypes(startContext.Options.AdditionalFiles, startContext.CancellationToken);
            if (assemblyName == AbstractionsAssembly)
            {
                startContext.RegisterCompilationEndAction(AnalyzeAbstractionsReferences);
            }

            startContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedType(symbolContext, shippedTypes),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeAbstractionsReferences(CompilationAnalysisContext context)
    {
        foreach (var reference in context.Compilation.ReferencedAssemblyNames)
        {
            if (!reference.Name.StartsWith("PulseRPC.", StringComparison.Ordinal)
                || reference.Name == AbstractionsAssembly)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenReferenceRule,
                Location.None,
                AbstractionsAssembly,
                reference.Name));
        }
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        ImmutableHashSet<string> shippedTypes)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsPublicSurface(type))
        {
            return;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (shippedTypes.Contains(typeName))
        {
            return;
        }

        var assemblyName = context.Compilation.AssemblyName;
        var namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();

        if (IsNamespaceViolation(assemblyName, namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NamespaceOwnershipRule,
                type.Locations.FirstOrDefault(),
                typeName,
                assemblyName,
                namespaceName));
            return;
        }

        if (assemblyName == AbstractionsAssembly && IsImplementationShaped(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PublicImplementationRule,
                type.Locations.FirstOrDefault(),
                typeName));
        }
    }

    private static bool IsNamespaceViolation(string? assemblyName, string namespaceName)
    {
        if (assemblyName == AbstractionsAssembly)
        {
            return namespaceName != CanonicalAbstractionsNamespace
                && !namespaceName.StartsWith(CanonicalAbstractionsNamespace + ".", StringComparison.Ordinal);
        }

        return assemblyName == SharedAssembly
            && (namespaceName == CanonicalAbstractionsNamespace
                || namespaceName.StartsWith(CanonicalAbstractionsNamespace + ".", StringComparison.Ordinal));
    }

    private static bool IsImplementationShaped(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        if (ImplementationSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return true;
        }

        return type.TypeKind == TypeKind.Class
            && type.AllInterfaces.Any(interfaceType =>
                interfaceType.ContainingAssembly?.Name == AbstractionsAssembly);
    }

    private static bool IsPublicSurface(INamedTypeSymbol type)
    {
        if (type.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        for (var containingType = type.ContainingType; containingType is not null; containingType = containingType.ContainingType)
        {
            if (containingType.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableHashSet<string> ReadShippedTypes(
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var file in additionalFiles)
        {
            if (!string.Equals(Path.GetFileName(file.Path), "PublicAPI.Shipped.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = file.GetText(cancellationToken);
            if (text is null)
            {
                continue;
            }

            foreach (var line in text.Lines)
            {
                var value = line.ToString().Trim();
                if (value.Length > 0 && value[0] != '#' && value.IndexOf(' ') < 0)
                {
                    builder.Add(value);
                }
            }
        }

        return builder.ToImmutable();
    }
}
