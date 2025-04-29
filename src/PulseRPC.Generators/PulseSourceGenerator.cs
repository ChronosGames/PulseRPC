using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace PulseRPC.Generators;

[Generator(LanguageNames.CSharp)]
public class PulseSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 注册语法树提供器
        var syntaxProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // 注册编译单元
        var compilation = context.CompilationProvider.Combine(syntaxProvider.Collect());

        // 注册源代码生成
        context.RegisterSourceOutput(compilation,
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 } ||
               node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private static TypeDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;

        foreach (var attributeList in typeDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is not IMethodSymbol attributeSymbol)
                {
                    continue;
                }

                var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                var fullName = attributeContainingTypeSymbol.ToDisplayString();

                if (fullName == "PulseRPC.ServiceContractAttribute" ||
                    fullName == "PulseRPC.HubContractAttribute")
                {
                    return typeDeclaration;
                }
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<TypeDeclarationSyntax?> types, SourceProductionContext context)
    {
        if (types.IsDefaultOrEmpty)
        {
            return;
        }

        var distinctTypes = types.Where(x => x is not null).Distinct();

        foreach (var type in distinctTypes)
        {
            if (type == null) continue;

            var model = compilation.GetSemanticModel(type.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(type);
            if (typeSymbol == null) continue;

            var isService = HasAttribute(typeSymbol, "PulseRPC.ServiceContractAttribute");
            var isHub = HasAttribute(typeSymbol, "PulseRPC.HubContractAttribute");

            if (!isService && !isHub) continue;

            if (isService)
            {
                GenerateServiceProxy(typeSymbol, context);
                GenerateServiceHandler(typeSymbol, context);
            }
            else
            {
                GenerateHubProxy(typeSymbol, context);
                GenerateHubHandler(typeSymbol, context);
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes().Any(ad => ad.AttributeClass?.ToDisplayString() == attributeName);
    }

    private static void GenerateServiceProxy(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        var source = GenerateServiceProxySource(typeSymbol);
        context.AddSource($"{typeSymbol.Name}ServiceProxy.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void GenerateServiceHandler(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        var source = GenerateServiceHandlerSource(typeSymbol);
        context.AddSource($"{typeSymbol.Name}ServiceHandler.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void GenerateHubProxy(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        var source = GenerateHubProxySource(typeSymbol);
        context.AddSource($"{typeSymbol.Name}HubProxy.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void GenerateHubHandler(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        var source = GenerateHubHandlerSource(typeSymbol);
        context.AddSource($"{typeSymbol.Name}HubHandler.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string GenerateServiceProxySource(INamedTypeSymbol typeSymbol)
    {
        var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;
        var methods = GetMethodsToImplement(typeSymbol);

        var methodImplementations = new StringBuilder();
        foreach (var method in methods)
        {
            methodImplementations.AppendLine(GenerateProxyMethodImplementation(method));
        }

        var source = $@"// <auto-generated/>
using System;
using System.Threading.Tasks;
using System.Text.Json;
using PulseRPC;

namespace {namespaceName}
{{
    public class {className}ServiceProxy : IServiceProxy, {className}
    {{
        private readonly IServiceClient _client;

        public {className}ServiceProxy(IServiceClient client)
        {{
            _client = client;
        }}

        {methodImplementations}
    }}
}}";

        return source;
    }

    private static string GenerateServiceHandlerSource(INamedTypeSymbol typeSymbol)
    {
        var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;
        var methods = GetMethodsToImplement(typeSymbol);

        var methodImplementations = new StringBuilder();
        foreach (var method in methods)
        {
            methodImplementations.AppendLine(GenerateHandlerMethodImplementation(method));
        }

        var source = $@"// <auto-generated/>
using System;
using System.Threading.Tasks;
using System.Text.Json;
using PulseRPC;

namespace {namespaceName}
{{
    public class {className}ServiceHandler : IServiceHandler
    {{
        private readonly {className} _implementation;

        public {className}ServiceHandler({className} implementation)
        {{
            _implementation = implementation;
        }}

        public async ValueTask<byte[]?> HandleAsync(string methodName, byte[]? payload)
        {{
            switch (methodName)
            {{
                {GenerateHandlerSwitchCases(methods)}
                default:
                    throw new InvalidOperationException($""Method '{{methodName}}' not found."");
            }}
        }}

        {methodImplementations}
    }}
}}";

        return source;
    }

    private static IEnumerable<IMethodSymbol> GetMethodsToImplement(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public);
    }

    private static string GenerateProxyMethodImplementation(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var parameterNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var returnType = method.ReturnType.ToDisplayString();
        var isAsync = returnType.StartsWith("System.Threading.Tasks");
        var methodName = method.Name;

        var requestPayload = method.Parameters.Any()
            ? $"JsonSerializer.SerializeToUtf8Bytes(new {{ {parameterNames} }})"
            : "null";

        if (returnType == "System.Threading.Tasks.Task")
        {
            return $@"
        public async Task {methodName}({parameters})
        {{
            await _client.InvokeAsync(""{methodName}"", {requestPayload});
        }}";
        }
        else if (returnType.StartsWith("System.Threading.Tasks.Task<"))
        {
            var actualReturnType = returnType.Substring(28, returnType.Length - 29); // Extract T from Task<T>
            return $@"
        public async Task<{actualReturnType}> {methodName}({parameters})
        {{
            var response = await _client.InvokeAsync(""{methodName}"", {requestPayload});
            return response != null
                ? JsonSerializer.Deserialize<{actualReturnType}>(response)
                : throw new InvalidOperationException(""Server returned null for non-nullable type"");
        }}";
        }
        else
        {
            return $@"
        public {returnType} {methodName}({parameters})
        {{
            throw new NotSupportedException(""Synchronous methods are not supported in PulseRPC"");
        }}";
        }
    }

    private static string GenerateHandlerMethodImplementation(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var parameterNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var returnType = method.ReturnType.ToDisplayString();
        var methodName = method.Name;

        if (method.Parameters.Any())
        {
            return $@"
        private async Task<byte[]?> Handle{methodName}Async(byte[] payload)
        {{
            var request = JsonSerializer.Deserialize<{GenerateRequestType(method)}>(payload);
            {(returnType.StartsWith("System.Threading.Tasks.Task<")
                ? $@"var result = await _implementation.{methodName}({GenerateParameterAccess(method)});
            return JsonSerializer.SerializeToUtf8Bytes(result);"
                : $@"await _implementation.{methodName}({GenerateParameterAccess(method)});
            return null;")}
        }}";
        }
        else
        {
            return $@"
        private async Task<byte[]?> Handle{methodName}Async(byte[]? payload)
        {{
            {(returnType.StartsWith("System.Threading.Tasks.Task<")
                ? $@"var result = await _implementation.{methodName}();
            return JsonSerializer.SerializeToUtf8Bytes(result);"
                : $@"await _implementation.{methodName}();
            return null;")}
        }}";
        }
    }

    private static string GenerateHandlerSwitchCases(IEnumerable<IMethodSymbol> methods)
    {
        var cases = new StringBuilder();
        foreach (var method in methods)
        {
            cases.AppendLine($@"
                case ""{method.Name}"":
                    return await Handle{method.Name}Async(payload ?? throw new ArgumentNullException(nameof(payload)));");
        }
        return cases.ToString();
    }

    private static string GenerateRequestType(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        return $"({{parameters}})";
    }

    private static string GenerateParameterAccess(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p => $"request.{p.Name}"));
    }

    private static string GenerateHubProxySource(INamedTypeSymbol typeSymbol)
    {
        var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;
        var methods = GetMethodsToImplement(typeSymbol);

        var methodImplementations = new StringBuilder();
        foreach (var method in methods)
        {
            methodImplementations.AppendLine(GenerateHubProxyMethodImplementation(method));
        }

        var source = $@"// <auto-generated/>
using System;
using System.Threading.Tasks;
using System.Text.Json;
using PulseRPC;

namespace {namespaceName}
{{
    public class {className}HubProxy : IHubProxy, {className}
    {{
        private readonly IHubClient _client;
        private readonly string _hubName;

        public {className}HubProxy(IHubClient client)
        {{
            _client = client;
            _hubName = ""{className}"";
        }}

        public Task ConnectAsync()
        {{
            return _client.ConnectAsync(_hubName);
        }}

        public Task DisconnectAsync()
        {{
            return _client.DisconnectAsync(_hubName);
        }}

        {methodImplementations}
    }}
}}";

        return source;
    }

    private static string GenerateHubHandlerSource(INamedTypeSymbol typeSymbol)
    {
        var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;
        var methods = GetMethodsToImplement(typeSymbol);

        var methodImplementations = new StringBuilder();
        foreach (var method in methods)
        {
            methodImplementations.AppendLine(GenerateHubHandlerMethodImplementation(method));
        }

        var source = $@"// <auto-generated/>
using System;
using System.Threading.Tasks;
using System.Text.Json;
using PulseRPC;

namespace {namespaceName}
{{
    public class {className}HubHandler : IHubHandler
    {{
        private readonly {className} _implementation;
        private readonly IHubContext _hubContext;
        private readonly string _hubName;

        public {className}HubHandler({className} implementation, IHubContext hubContext)
        {{
            _implementation = implementation;
            _hubContext = hubContext;
            _hubName = ""{className}"";
        }}

        public async ValueTask<byte[]?> HandleAsync(string connectionId, string methodName, byte[]? payload)
        {{
            switch (methodName)
            {{
                {GenerateHubHandlerSwitchCases(methods)}
                default:
                    throw new InvalidOperationException($""Method '{{methodName}}' not found."");
            }}
        }}

        public async Task BroadcastAsync(string methodName, object? payload = null)
        {{
            var serializedPayload = payload != null ? JsonSerializer.SerializeToUtf8Bytes(payload) : null;
            await _hubContext.BroadcastAsync(_hubName, methodName, serializedPayload);
        }}

        public async Task SendToClientAsync(string connectionId, string methodName, object? payload = null)
        {{
            var serializedPayload = payload != null ? JsonSerializer.SerializeToUtf8Bytes(payload) : null;
            await _hubContext.SendToClientAsync(_hubName, connectionId, methodName, serializedPayload);
        }}

        public async Task SendToGroupAsync(string groupName, string methodName, object? payload = null)
        {{
            var serializedPayload = payload != null ? JsonSerializer.SerializeToUtf8Bytes(payload) : null;
            await _hubContext.SendToGroupAsync(_hubName, groupName, methodName, serializedPayload);
        }}

        public Task AddToGroupAsync(string connectionId, string groupName)
        {{
            return _hubContext.AddToGroupAsync(_hubName, connectionId, groupName);
        }}

        public Task RemoveFromGroupAsync(string connectionId, string groupName)
        {{
            return _hubContext.RemoveFromGroupAsync(_hubName, connectionId, groupName);
        }}

        {methodImplementations}
    }}
}}";

        return source;
    }

    private static string GenerateHubProxyMethodImplementation(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var parameterNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var returnType = method.ReturnType.ToDisplayString();
        var methodName = method.Name;

        var requestPayload = method.Parameters.Any()
            ? $"JsonSerializer.SerializeToUtf8Bytes(new {{ {parameterNames} }})"
            : "null";

        if (returnType == "System.Threading.Tasks.Task")
        {
            return $@"
        public async Task {methodName}({parameters})
        {{
            await _client.InvokeAsync(_hubName, ""{methodName}"", {requestPayload});
        }}";
        }
        else if (returnType.StartsWith("System.Threading.Tasks.Task<"))
        {
            var actualReturnType = returnType.Substring(28, returnType.Length - 29);
            return $@"
        public async Task<{actualReturnType}> {methodName}({parameters})
        {{
            var response = await _client.InvokeAsync(_hubName, ""{methodName}"", {requestPayload});
            return response != null
                ? JsonSerializer.Deserialize<{actualReturnType}>(response)
                : throw new InvalidOperationException(""Server returned null for non-nullable type"");
        }}";
        }
        else
        {
            return $@"
        public {returnType} {methodName}({parameters})
        {{
            throw new NotSupportedException(""Synchronous methods are not supported in PulseRPC"");
        }}";
        }
    }

    private static string GenerateHubHandlerMethodImplementation(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var parameterNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var returnType = method.ReturnType.ToDisplayString();
        var methodName = method.Name;

        if (method.Parameters.Any())
        {
            return $@"
        private async Task<byte[]?> Handle{methodName}Async(string connectionId, byte[] payload)
        {{
            var request = JsonSerializer.Deserialize<{GenerateRequestType(method)}>(payload);
            {(returnType.StartsWith("System.Threading.Tasks.Task<")
                ? $@"var result = await _implementation.{methodName}({GenerateParameterAccess(method)});
            return JsonSerializer.SerializeToUtf8Bytes(result);"
                : $@"await _implementation.{methodName}({GenerateParameterAccess(method)});
            return null;")}
        }}";
        }
        else
        {
            return $@"
        private async Task<byte[]?> Handle{methodName}Async(string connectionId, byte[]? payload)
        {{
            {(returnType.StartsWith("System.Threading.Tasks.Task<")
                ? $@"var result = await _implementation.{methodName}();
            return JsonSerializer.SerializeToUtf8Bytes(result);"
                : $@"await _implementation.{methodName}();
            return null;")}
        }}";
        }
    }

    private static string GenerateHubHandlerSwitchCases(IEnumerable<IMethodSymbol> methods)
    {
        var cases = new StringBuilder();
        foreach (var method in methods)
        {
            cases.AppendLine($@"
                case ""{method.Name}"":
                    return await Handle{method.Name}Async(connectionId, payload ?? throw new ArgumentNullException(nameof(payload)));");
        }
        return cases.ToString();
    }
}
