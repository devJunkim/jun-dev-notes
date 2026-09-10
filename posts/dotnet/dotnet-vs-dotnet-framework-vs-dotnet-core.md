---
title: ".NET vs .NET Framework vs .NET Core: What's the Difference?"
excerpt: "Understand how .NET Framework, .NET Core, and modern .NET relate, which platforms they support, and what to choose for new or existing applications."
category: ".NET"

seo:
  focusKeyword: ".NET vs .NET Framework vs .NET Core"
  description: "Learn the history and practical differences between .NET Framework, .NET Core, and modern .NET, including platform support and migration choices."
  socialTitle: ".NET vs .NET Framework vs .NET Core"
  socialDescription: "A practical guide to the .NET naming history, platform differences, and choosing the right runtime for new and legacy applications."
---

# .NET vs .NET Framework vs .NET Core: What's the Difference?

The names `.NET`, `.NET Framework`, and `.NET Core` are easy to confuse because they describe related platforms from different periods of Microsoft's development ecosystem. They are not three equal products competing for every new project.

`.NET Framework` is the original Windows-focused implementation. `.NET Core` was the cross-platform redesign introduced alongside it. Starting with `.NET 5`, the Core line became the main unified platform and dropped “Core” from the product name. Today, when Microsoft documentation says **.NET**, it generally means modern .NET: the actively evolving successor to .NET Core.

> **Quick answer:** Choose a supported modern .NET release for new development. Maintain .NET Framework when an existing Windows application depends on Framework-only technologies or when migration cost outweighs its current benefit. Treat “.NET Core” as the historical name for versions 1.0 through 3.1, not the name of current releases.

## .NET vs .NET Framework vs .NET Core at a Glance

| Platform | Relevant versions | Operating systems | Current role |
| --- | --- | --- | --- |
| .NET Framework | 1.0–4.8.1 | Windows | Supported maintenance platform for existing applications |
| .NET Core | 1.0–3.1 | Windows, Linux, macOS | Historical predecessor to modern .NET; releases are out of support |
| Modern .NET | 5 and later | Windows, Linux, macOS | Preferred platform for new development |

The platforms share C#, the base class library lineage, garbage collection, and many APIs. They differ in runtime implementation, application models, deployment, support policy, and compatibility with older Windows technologies.

## How the .NET Family Evolved

### .NET Framework: the original platform

Microsoft released .NET Framework in the early 2000s as a Windows development platform. It became the foundation for technologies such as ASP.NET, Windows Forms, Windows Presentation Foundation (WPF), Windows Communication Foundation (WCF), and later versions of the C# language.

.NET Framework is installed and serviced as a Windows component. Version 4.x updates are largely in-place: a newer 4.x installation replaces the older 4.x runtime on the machine. This model works for mature Windows environments but limits application-level side-by-side runtime choices.

Framework remains supported, particularly its current Windows-supported releases, but it is primarily in maintenance. It receives security, reliability, and compatibility fixes rather than serving as the main destination for new platform innovation.

### .NET Core: the cross-platform redesign

.NET Core introduced a modular, open-source, cross-platform implementation. It supported Windows, Linux, and macOS; allowed application-local and side-by-side runtime deployment; used the SDK-style project format; and became the runtime beneath ASP.NET Core.

Early versions had a smaller API surface than .NET Framework, so migration was not always straightforward. The platform expanded quickly through .NET Core 2.x and 3.x, including support for Windows desktop applications in .NET Core 3.

The final product carrying the `.NET Core` name was .NET Core 3.1. Those releases are no longer supported and should not be selected for new work.

### .NET 5 and later: the unified modern platform

Microsoft skipped the name “.NET Core 4” and released `.NET 5` as the next version after .NET Core 3.1. The naming made the direction clear: this was intended to be the primary .NET platform going forward, not a secondary subset of .NET Framework.

Modern .NET continues the architecture and tooling lineage of .NET Core. Releases use names such as `.NET 8`, `.NET 9`, and `.NET 10`; they are not called “.NET Core 8” or “.NET Core 10.” The official [Introduction to .NET](https://learn.microsoft.com/en-us/dotnet/core/introduction) describes modern .NET as the cross-platform, open-source implementation that is actively evolving.

The word “unified” does not mean every old Framework technology was moved unchanged. It means the Core product line became the common modern platform for server, cloud, console, desktop, mobile, and other workloads, with workload-specific libraries and frameworks layered on top.

## Platform and Deployment Differences

### Operating-system support

.NET Framework runs on Windows. It integrates deeply with Windows and remains appropriate for applications tied to Framework-era Windows technologies.

Modern .NET is cross-platform. A compatible application can run on Windows, Linux, or macOS, and server workloads commonly run in Linux containers. Cross-platform support does not magically make every application portable: code that calls the Windows registry, COM, native Windows libraries, or Windows-only UI frameworks still has platform constraints.

```csharp
Console.WriteLine($"Runtime: {Environment.Version}");
Console.WriteLine($"OS: {Environment.OSVersion}");
Console.WriteLine($"64-bit process: {Environment.Is64BitProcess}");
```

The same modern .NET console application can run on multiple supported operating systems, provided its dependencies are also compatible.

### Deployment model

Modern .NET supports framework-dependent deployment, where the target machine provides a compatible runtime, and self-contained deployment, where the application carries its runtime.

```bash
dotnet publish --configuration Release --runtime linux-x64 --self-contained true
```

Self-contained deployment improves runtime isolation but produces larger artifacts and still requires servicing through application redeployment. Framework-dependent deployment is smaller and can use a centrally installed runtime.

.NET Framework applications rely on the compatible Framework installed and serviced with Windows. This can simplify centrally managed Windows estates, but it provides less application-level runtime isolation than modern .NET.

### Side-by-side versions

Multiple modern .NET runtime families can exist on the same machine. Applications declare a target framework such as `net8.0` or `net10.0`, and runtime roll-forward rules determine which compatible installed runtime starts the application.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

.NET Framework 4.x is an in-place Windows component. You can target different Framework versions during compilation, but the machine does not host independent Framework 4.x runtimes in the same way.

## Application Models and Libraries

Modern .NET is the natural home for:

- ASP.NET Core web applications and APIs
- Worker services and background processing
- Console applications and command-line tools
- Cloud services, containers, and microservices
- Cross-platform libraries
- Modern Windows Forms and WPF applications on Windows
- Mobile and cross-platform UI workloads through .NET MAUI

.NET Framework remains necessary or pragmatic for some existing technologies, including:

- ASP.NET Web Forms
- ASP.NET Web Pages
- Existing `System.Web` applications
- Some WCF server implementations and workflow technologies
- Older third-party libraries available only for .NET Framework
- Windows applications with extensive Framework-specific integrations

WPF and Windows Forms are not limited to .NET Framework; modern .NET supports both on Windows. The decision for a desktop application therefore depends on its dependencies and migration effort, not just its UI framework name.

### What .NET Standard does—and does not do

.NET Standard is an API specification that helped libraries target a common surface shared by multiple .NET implementations. It is not a runtime and does not launch applications.

A library targeting .NET Standard 2.0 can often be consumed by both .NET Framework and modern .NET, making it useful during incremental migrations. New libraries used only by modern .NET applications can target modern .NET directly and gain access to newer APIs.

```xml
<PropertyGroup>
  <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
</PropertyGroup>
```

Multi-targeting lets a library provide a broad compatibility build and a modern build, but it increases testing and maintenance requirements.

## Choosing Modern .NET for New Development

For a new application, use a currently supported modern .NET version unless a verified requirement forces another choice. Benefits include:

- Cross-platform hosting and development
- Current runtime and C# improvements
- ASP.NET Core and modern hosting APIs
- Better container support
- Side-by-side runtime deployment
- Active performance and diagnostics work
- A clear annual release and support lifecycle

Choose between a Long Term Support (LTS) and Standard Term Support (STS) release based on your organization's upgrade cadence. “LTS” does not mean indefinite support; every release has an end date and needs security updates during its supported lifetime.

A typical new API starts with the SDK:

```bash
dotnet new webapi --name Orders.Api
dotnet run --project Orders.Api
```

Do not choose an unsupported .NET Core release because an old tutorial uses it. Update the tutorial's concepts and packages to a supported modern target.

## When Maintaining .NET Framework Still Makes Sense

Migration is a business and engineering decision, not a purity test. Continuing to maintain .NET Framework can be reasonable when:

- A stable application meets its requirements and runs on supported Windows infrastructure.
- It depends heavily on Web Forms, `System.Web`, workflow, or another unavailable technology.
- Critical third-party or proprietary dependencies have no modern replacement.
- Migration risk is high and the expected operational or product benefit is currently low.
- A planned replacement makes a large in-place migration wasteful.

Maintenance still requires supported Windows versions, patched Framework installations, supported dependencies, monitoring, and security review. “Legacy” must not become an excuse to ignore risk.

Microsoft's [.NET versus .NET Framework guidance](https://learn.microsoft.com/en-us/dotnet/standard/choosing-core-framework-server) recommends modern .NET for server development and identifies specific reasons to retain Framework.

## Planning a Framework-to-.NET Migration

Start with evidence rather than changing the target framework and hoping it builds.

1. Inventory projects, NuGet packages, direct assembly references, and deployment assumptions.
2. Identify Framework-only APIs and application models.
3. Upgrade dependencies and move reusable libraries toward SDK-style projects.
4. Add characterization and integration tests around business-critical behavior.
5. Choose an in-place, incremental, or replacement strategy.
6. Validate deployment, authentication, serialization, globalization, and performance.

A large ASP.NET application may benefit from incremental migration: extract libraries, place a modern ASP.NET Core application beside the old system, and move endpoints or capabilities over time. A small console application might migrate directly.

Compatibility analyzers can find API issues, but they cannot decide whether runtime behavior, deployment, or operational assumptions remain correct.

## Common Mistakes and Misconceptions

### Calling current releases “.NET Core”

`.NET Core` correctly names versions 1.0 through 3.1. `.NET 5` and later are modern `.NET`. “ASP.NET Core” and “Entity Framework Core” retained “Core” in their product names, which is one reason the confusion persists.

### Assuming .NET Framework is a newer or fuller modern .NET

“Framework” does not mean premium or complete. It identifies the original Windows implementation. Modern .NET has its own broader current platform capabilities, while Framework retains some older Windows-specific technologies.

### Assuming cross-platform means every API works everywhere

The runtime is cross-platform, but an application can still use platform-specific APIs. Analyze dependencies and test on every supported operating system.

### Migrating only to obtain a newer C# syntax

Language version, compiler, target framework, and runtime are related but distinct. Some newer language features can target older runtimes, while features that depend on runtime types or APIs cannot. Migration decisions should consider the complete platform.

### Leaving applications on unsupported .NET Core releases

.NET Core 3.1 and earlier releases are out of support. A working application can still carry unpatched runtime risk. Plan upgrades to a supported modern release.

### Assuming every Framework application must migrate immediately

Some migrations deliver major security, deployment, and maintenance benefits; others consume years without proportional value. Assess the application, dependencies, support requirements, and roadmap.

## Interview-Oriented Questions

### Did .NET Framework become .NET Core?

No. .NET Core was a separate cross-platform implementation developed alongside .NET Framework. The .NET Core line then evolved into modern .NET beginning with .NET 5.

### Is .NET Core still the current product name?

No. The last release with that name was .NET Core 3.1. Current releases are named `.NET` followed by the version number.

### Is .NET Framework cross-platform?

No. It is a Windows platform. Mono historically provided a separate cross-platform implementation of much of the Framework ecosystem, but that does not make .NET Framework itself cross-platform.

### Why keep a .NET Framework application?

It may depend on Framework-only technologies, Windows integration, or third-party components, and migration may not yet justify its cost and risk. It still needs a supported, patched environment.

### What should a new web API target?

Normally, a currently supported modern .NET release with ASP.NET Core. Confirm organizational support policy, hosting compatibility, and dependency requirements.

## Summary

.NET Framework, .NET Core, and modern .NET belong to one ecosystem but occupy different historical and practical roles:

- `.NET Framework` is the original Windows implementation and remains relevant for existing Framework-dependent applications.
- `.NET Core` was the cross-platform redesign covering versions 1.0 through 3.1.
- `.NET 5` and later continue that Core lineage as the unified, actively developed modern `.NET` platform.

Use supported modern .NET for new applications. Keep .NET Framework when real dependencies or migration economics justify it, and revisit that decision as support, security, and business needs change.
