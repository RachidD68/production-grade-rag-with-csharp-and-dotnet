# Ch21 — Native AOT MCP server (minimal variant)

This is the Chapter 21 §2.9 demonstration of publishing an MCP server as a
**Native AOT** binary: no JIT, no reflection-based serialization, a small
self-contained startup, and a single stdio tool.

## What it is

A minimal Model Context Protocol server over stdio exposing one tool, `search`,
a substring match over a four-document in-memory corpus. It depends only on:

- `ModelContextProtocol` (the MCP SDK)
- `Microsoft.Extensions.Hosting` (the generic host)

It deliberately does **not** reference the full SmartDocs retrieval pipeline.
That pipeline pulls in Qdrant (gRPC), the Azure SDK, and Npgsql, none of which
are AOT/trim-annotated; including them would bury the example in third-party
trim warnings that say nothing about your code. The lean server is the honest,
publishable target.

## The AOT toggles (the part the chapter prints)

In `Ch21_AotMcpServer.csproj`:

```xml
<PublishAot>true</PublishAot>
<IsAotCompatible>true</IsAotCompatible>
<InvariantGlobalization>true</InvariantGlobalization>
```

Plus a System.Text.Json **source-generated** context (`AotJsonContext` in
`DocsSearchTool.cs`) so the tool's JSON payloads serialize without reflection,
and the tool is registered by its concrete type — `WithTools<DocsSearchTool>()`
— rather than by the trim-hostile assembly-scanning overload.

`PublishAot` is honoured by `dotnet publish` only; a plain `dotnet build`
ignores it, so this project always builds as part of the solution with zero
warnings.

## Publishing it

```bash
dotnet publish samples/Ch21_AotMcpServer/Ch21_AotMcpServer.csproj \
    -c Release -r win-x64 -p:PublishAot=true
```

## Status: builds and AOT-analyses clean; native link needs the C++ toolchain

On the authoring machine (June 2026, .NET SDK 10.0.301):

- `dotnet build` — **clean, 0 warnings**.
- `dotnet publish -r win-x64 -p:PublishAot=true` — the ILC managed
  ahead-of-time compilation and **trim/AOT analysis run and emit zero
  `ILxxxx` warnings**: neither the MCP SDK (`ModelContextProtocol` 1.4.0) nor
  `Microsoft.Extensions.AI` contributed a trim warning for this minimal surface.
  Publish then stops at the **native link** step with:

  > error: Platform linker not found. Ensure you have ... the Desktop
  > Development for C++ workload in Visual Studio.

  That is an **environment prerequisite** (the `link.exe` native linker), not a
  code problem — see <https://aka.ms/nativeaot-prerequisites>. Install the
  "Desktop development with C++" workload and the publish produces a single
  native `Ch21_AotMcpServer.exe`.

So: the AOT path is **real and analyses clean**, and is **documented rather than
shipped as a prebuilt native binary** because the CI/authoring box does not have
the C++ build tools installed. The quality bar for the repository is
`dotnet build` (which is clean); the native artifact is reproducible by anyone
with the C++ workload installed.
