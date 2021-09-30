![Icon](https://raw.githubusercontent.com/devlooped/Azure.Functions.OpenApi/main/assets/img/icon-32.png) OpenAPI/Swagger Source Generator for C# Azure Functions
============

[![Version](https://img.shields.io/nuget/v/Devlooped.Azure.Functions.OpenApi.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.Azure.Functions.OpenApi)
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.Azure.Functions.OpenApi.svg?color=green)](https://www.nuget.org/packages/Devlooped.Azure.Functions.OpenApi)
[![License](https://img.shields.io/github/license/devlooped/Azure.Functions.OpenApi.svg?color=blue)](https://github.com/devlooped/Azure.Functions.OpenApi/blob/main/license.txt)
[![Build](https://github.com/devlooped/Azure.Functions.OpenApi/workflows/build/badge.svg?branch=main)](https://github.com/devlooped/Azure.Functions.OpenApi/actions)

A zero-code basic OpenAPI (Swagger) generator for C# Azure Functions.

# Usage

Just install the package and run the app. By default, you will get an endpoint 
`openapi` that returns the Swagger UI for browing your API, as well as an endpoint
with the standard `swagger.json` file. The generated swagger file will contain all 
HTTP-triggered functions in the compilation.

![endpoints screenshot](https://raw.githubusercontent.com/devlooped/Azure.Functions.OpenApi/main/assets/img/endpoints.png)

This assumes the `routePrefix` has been configured as empty (to override the default of `/api`) with:

```json
{
  ...
  "extensions": {
    "http": {
      "routePrefix": ""
    }
  }
}
```

Opening the `openapi` endpoint renders the SwaggerUI:

![swagger UI screenshot](https://raw.githubusercontent.com/devlooped/Azure.Functions.OpenApi/main/assets/img/swaggerui.png)

The generated `swagger.json` can be inspected in the project's intermediate output path 
(by default, `obj\Debug\[TFM]\openapi\v2\swagger.json`). 

The `swagger.json` as well as the function endpoints are generated at build-time by a 
[C# source generator](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview). 
As such, you can inspect the generated code by setting the `EmitCompilerGeneratedFiles` project 
property to `true` like:

```xml
  <PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
```

This will emit the generated functions source files under 
`$(IntermediateOutputPath)\generated\Devlooped.Azure.Functions.OpenApi\SourceGenerator`:

![generated sources screenshot](https://raw.githubusercontent.com/devlooped/Azure.Functions.OpenApi/main/assets/img/sourcegenerated.png)

## Customization

There are several ways of customizing the generation, all driven by MSBuild.

The main generation driver is an MSBuild item `OpenApi`, which contains various 
pieces of metadata to tweak the output. Its item definition is as follows:

```xml
  <ItemDefinitionGroup>
    <OpenApi>
      <Title />
      <Description />
      <Version />
      <Route />
      <Url />
      <SchemaVersion>2</SchemaVersion>
    </OpenApi>
  </ItemDefinitionGroup>

```

If no `<OpenApi Include="..">` is provided, one is automatically added, with the default values applied.

The default values are:
- `Title`: first with a non-empty value from `$(AssemblyTitle)`, `$(Product)`, `$(ProductName)`, `$(Title)`.
- `Description`: `$(Description)`
- `Version`: first with non-empty value from `$(Version)`, `$(InformationalVersion)`, `$(AssemblyVersion)`
- `Route`: `/openapi/v%(SchemaVersion)/swagger.json`, with `SchemaVersion` being `2` by default.
- `Url`: `/api` or `extensions.http.routePrefix` in `host.json` if set to a non-empty value, `/` otherwise.


# Dogfooding

[![CI Version](https://img.shields.io/endpoint?url=https://shields.kzu.io/vpre/Devlooped.Azure.Functions.OpenApi/main&label=nuget.ci&color=brightgreen)](https://pkg.kzu.io/index.json)
[![Build](https://github.com/devlooped/Azure.Functions.OpenApi/workflows/build/badge.svg?branch=main)](https://github.com/devlooped/Azure.Functions.OpenApi/actions)

We also produce CI packages from branches and pull requests so you can dogfood builds as quickly as they are produced. 

The CI feed is `https://pkg.kzu.io/index.json`. 

The versioning scheme for packages is:

- PR builds: *42.42.42-pr*`[NUMBER]`
- Branch builds: *42.42.42-*`[BRANCH]`.`[COMMITS]`



## Sponsors

[![sponsored](https://raw.githubusercontent.com/devlooped/oss/main/assets/images/sponsors.svg)](https://github.com/sponsors/devlooped) [![clarius](https://raw.githubusercontent.com/clarius/branding/main/logo/byclarius.svg)](https://github.com/clarius)[![clarius](https://raw.githubusercontent.com/clarius/branding/main/logo/logo.svg)](https://github.com/clarius)

*[get mentioned here too](https://github.com/sponsors/devlooped)!*
