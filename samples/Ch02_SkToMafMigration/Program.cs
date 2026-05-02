// Chapter 2 — Migrating from Semantic Kernel to the Microsoft Agent Framework.
//
// One concrete before/after pair: a "weather agent" that has a single tool
// and answers questions in natural language. The "Before" (SK) is shown as
// a documentation comment block — readers can copy it into an SK project to
// verify it is the canonical pattern. The "After" (MAF) below is the real,
// runnable code in this file.
//
// Rename map (mission brief §3 + the Microsoft migration guide):
//
//   Semantic Kernel                         |  Microsoft Agent Framework 1.x
//   ----------------------------------------+--------------------------------------------
//   Kernel.CreateBuilder().Add* + .Build()  |  IChatClient (via M.E.AI) + ChatClientAgent
//   [KernelFunction] on a method            |  AIFunctionFactory.Create(method, name, ...)
//   kernel.Plugins.AddFromObject(plugin)    |  ChatClientAgentOptions.ChatOptions.Tools = [...]
//   ChatHistory                             |  AgentThread (agent.GetNewThread())
//   KernelArguments                         |  AgentRunOptions (passed to RunAsync)
//   IChatCompletionService.GetChat...Async  |  agent.RunAsync(...) / RunStreamingAsync(...)
//
// Run:
//   dotnet run --project samples/Ch02_SkToMafMigration

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RagInDotNet.Samples.Ch02_SkToMafMigration;
using SmartDocs.Core.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Services.AddSmartDocsCore(builder.Configuration);
using var host = builder.Build();

var chat = host.Services.GetRequiredService<IChatClient>();

var answer = await WeatherAgent.AskAsync(chat, "What's the weather in Paris?");

Console.WriteLine();
Console.WriteLine("=== Answer (MAF) ===");
Console.WriteLine(answer);
