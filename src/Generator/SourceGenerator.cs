using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;

record OpenApi(string Title, string Description, string Route, string SchemaVersion, string Url, string Version);

record FunctionDoc(string? Summary, string? Remarks, string? Returns, Dictionary<string, string> Parameters);

record HttpFunction(IMethodSymbol Method, string Route, string[] Verbs);

[Generator]
public class SourceGenerator : ISourceGenerator
{
    static readonly Regex urlParameters = new Regex(@"\{(?<name>\w+)(?<optional>\?)?\}", RegexOptions.Compiled);

    public void Initialize(GeneratorInitializationContext context) { }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.DebugSwaggerGenerator", out var debugValue) &&
            bool.TryParse(debugValue, out var shouldDebug) &&
            shouldDebug)
        {
            Debugger.Launch();
        }

        var functionAttr = context.Compilation.GetTypeByMetadataName("Microsoft.Azure.WebJobs.FunctionNameAttribute");
        var triggerAttr = context.Compilation.GetTypeByMetadataName("Microsoft.Azure.WebJobs.HttpTriggerAttribute");

        if (functionAttr == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "DSG001", "Compiler",
                "Could not find 'Microsoft.Azure.WebJobs.FunctionNameAttribute' type in the compilation.",
                DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0)); ;
            return;
        }
        if (triggerAttr == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "DSG002", "Compiler",
                "Could not find 'Microsoft.Azure.WebJobs.HttpTriggerAttribute' type in the compilation.",
                DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0)); ;
            return;
        }

        var visitor = new TriggeredFunctionVisitor(functionAttr, triggerAttr);
        context.Compilation.GlobalNamespace.Accept(visitor);

        if (visitor.Functions.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "DSG003", "Compiler",
                "Did not find any HTTP-triggered functions in the compilation.",
                DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, true, 4)); ;
            return;
        }

        if (!context.TryGetBuildProperty("IntermediateOutputPath", out var outputPath) ||
            !context.TryGetBuildProperty("MSBuildProjectDirectory", out var projectDir))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "DSG004", "Compiler",
                "Could not retrieve IntermediateOutputPath or MSBuildProjectDirectory for the project.",
                DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, true, 4)); ;
            return;
        }

        var openApis = context.AdditionalFiles
            .Where(f => context.AnalyzerConfigOptions
                .GetOptions(f)
                .TryGetValue("build_metadata.AdditionalFiles.SourceItemType", out var itemType) &&
                itemType == "OpenApi")
            .Select(f =>
            {
                if (context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.Title", out var title) &&
                    context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.Description", out var description) &&
                    context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.Route", out var route) &&
                    context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.SchemaVersion", out var schemaVersion) &&
                    context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.Url", out var url) &&
                    context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.OpenApi.Version", out var version))
                {
                    if (!route.StartsWith("/"))
                        route = "/" + route;

                    if (schemaVersion == "2" || schemaVersion == "3")
                        return new OpenApi(title, description, route, schemaVersion, url, version);

                    context.ReportDiagnostic(Diagnostic.Create(
                        "DSG006", "Compiler",
                        $"Invalid OpenAPI schema version {schemaVersion}. Must be either '2' or '3'.",
                        DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, true, 4)); ;
                }

                return null;
            })
            .Where(x => x != null)
            .Cast<OpenApi>()
            .ToArray();

        if (openApis.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "DSG005", "Compiler",
                "No OpenApi item was provided containing the server Url metadata.",
                DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0)); ;
            return;
        }

        OpenApiLicense? license = null;
        if (context.TryGetBuildProperty("PackageLicenseExpression", out var expression))
        {
            // See https://docs.microsoft.com/en-us/nuget/nuget-org/licenses.nuget.org
            if (expression.Contains(' '))
                expression = "(" + expression + ")";

            license = new OpenApiLicense
            {
                Name = expression,
                Url = new Uri("https://licenses.nuget.org/" + expression)
            };
        }

        var paths = new OpenApiPaths();
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = openApis[0].Title,
                Description = openApis[0].Description,
                License = license,
                Version = openApis[0].Version,
            },
            Servers = openApis.Select(x => new OpenApiServer
            {
                Description = x.Description,
                Url = x.Url
            }).ToList(),
            Paths = paths,
        };

        foreach (var function in visitor.Functions)
        {
            // Remove the optional ? specifier from the operation route. Causes 
            // swagger UI to send wrong values for the URL parameters. Doesn't 
            // affect proper description of whether the argument is required or not.
            var route = function.Route.Replace("?}", "}");
            if (!route.StartsWith("/"))
                route = "/" + route;

            if (!paths.TryGetValue(route, out var path))
            {
                path = new OpenApiPathItem();
                paths[route] = path;
            }

            var doc = GetDocumentation(function.Method);
            var parameters = new List<OpenApiParameter>();
            var urlMatch = urlParameters.Match(function.Route);
            while (urlMatch.Success)
            {
                var name = urlMatch.Groups["name"].Value;
                var desc = "";
                doc?.Parameters.TryGetValue(name, out desc);

                parameters.Add(new OpenApiParameter
                {
                    In = ParameterLocation.Path,
                    Name = name,
                    Required = !urlMatch.Groups["optional"].Success,
                    Description = desc
                });
                urlMatch = urlMatch.NextMatch();
            }

            foreach (var verb in function.Verbs)
            {
                if (!Enum.TryParse<OperationType>(verb, true, out var operation))
                    continue;

                path.AddOperation(operation, new OpenApiOperation
                {
                    Summary = doc?.Summary ?? "",
                    Description = doc?.Remarks ?? "",
                    Parameters = parameters,
                    Responses = new OpenApiResponses
                    {
                        {
                            "default",
                            new OpenApiResponse
                            {
                                Description = doc?.Returns ?? ""
                            }
                        }
                    }
                });
            }
        }

        foreach (var api in openApis)
        {
            using var writer = new StringWriter();
            var jsonWriter = new OpenApiJsonWriter(writer);

            if (api.SchemaVersion == "2")
                document.SerializeAsV2(jsonWriter);
            else
                document.SerializeAsV3(jsonWriter);

            jsonWriter.Flush();

            var filePath = Path.Combine(outputPath, api.Route.TrimStart('/', '\\'));
            if (!Path.IsPathRooted(filePath))
                filePath = Path.Combine(projectDir, filePath);

            filePath = Path.GetFullPath(filePath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, writer.ToString());

            var endpoint = new StringBuilder(EmbeddedResource.GetContent("OpenApiFunction.txt"));
            var suffix = openApis.Length == 1 ? "" : api.SchemaVersion;
            endpoint
                .Replace("$suffix$", suffix)
                .Replace("$route$", api.Route.TrimStart('/'));

            // For relative URI to openapi, replace that from the relative path 
            // being built to the server-side file path, since that is typically 
            // the '/api' default prefix for web apis.
            if (api.Url.StartsWith("/"))
                endpoint.Replace("$baseUrl$", api.Url);
            else
                endpoint.Replace("$baseUrl$", "");

            context.AddSource($"OpenApiFunction{suffix}.cs", endpoint.ToString());
        }

        var defaultApi = openApis[0];
        var defaultRoute = defaultApi.Url.TrimEnd('/') + "/" + defaultApi.Route.TrimStart('/', '\\');
        var ui = EmbeddedResource.GetContent("OpenApiUI.txt").Replace("$default$", defaultRoute);

        context.AddSource($"OpenApiUI.cs", ui);
    }

    FunctionDoc? GetDocumentation(IMethodSymbol method)
    {
        var doc = method.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(doc))
            return null;

        try
        {
            var xml = XElement.Parse(doc);

            var summary = xml.Element("summary")?.Value?.Trim();
            var remarks = xml.Element("remarks")?.Value.Trim();
            var returns = xml.Element("returns")?.Value.Trim();
            var parameters = xml.Elements("param")
                .Select(p => new KeyValuePair<string?, string?>(p.Attribute("name")?.Value, p.Value))
                .Where(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value))
                .ToDictionary(p => p.Key!, p => p.Value!.Trim());


            return new FunctionDoc(summary, remarks, returns, parameters);
        }
        catch
        {
            return null;
        }
    }

    class TriggeredFunctionVisitor : SymbolVisitor
    {
        readonly INamedTypeSymbol functionAttr;
        readonly INamedTypeSymbol triggerAttr;

        public List<HttpFunction> Functions { get; } = new();

        public TriggeredFunctionVisitor(INamedTypeSymbol functionAttr, INamedTypeSymbol triggerAttr)
            => (this.functionAttr, this.triggerAttr)
            = (functionAttr, triggerAttr);

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var child in symbol.GetMembers())
                child.Accept(this);
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            foreach (var child in symbol.GetMembers())
                child.Accept(this);
        }

        public override void VisitMethod(IMethodSymbol symbol)
        {
            var func = symbol.GetAttributes().FirstOrDefault(attr
                => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, functionAttr));

            if (func == null || func.ConstructorArguments[0].Value?.ToString().StartsWith("openapi") == true)
                return;

            var trigger = symbol.Parameters
                .Select(prm => prm.GetAttributes().FirstOrDefault(attr =>
                    SymbolEqualityComparer.Default.Equals(triggerAttr, attr.AttributeClass)))
                .Where(attr => attr != null)
                .FirstOrDefault();

            if (trigger == null)
                return;

            Functions.Add(new HttpFunction(symbol,
                trigger.NamedArguments
                    .Where(x => x.Key == "Route")
                    .Select(x => (string)x.Value.Value!)
                    .FirstOrDefault() ?? (string)func.ConstructorArguments[0].Value!,
                trigger.ConstructorArguments
                    .Where(x => x.Kind == TypedConstantKind.Array)
                    .SelectMany(x => x.Values)
                    .Select(x => x.Value)
                    .OfType<string>()
                    .ToArray()));
        }
    }
}
