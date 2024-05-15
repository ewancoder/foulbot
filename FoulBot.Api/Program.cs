using System;
using FoulBot.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder();

var aiApiKey = builder.Configuration["OpenAIKey"];
var debugMode = DebugMode.Console;
var useConsoleInsteadOfTelegram = false;

builder.Services.AddSingleton(new FoulBotGatewayFactory(aiApiKey, debugMode, useConsoleInsteadOfTelegram));
builder.Services.AddSingleton<FoulBotsHostedService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<FoulBotsHostedService>());

var app = builder.Build();
var service = app.Services.GetRequiredService<FoulBotsHostedService>();

app.MapGet("/api/diag", () =>
{
    return DateTime.UtcNow;
});

app.MapGet("/api/bots", () =>
{
    return service.Bots;
});

app.MapPut("/api/bots/{botName}", (string botName, string mainDirective) =>
{
    service.ChangeMainDirective(botName, mainDirective);
    return service.Bots;
});

await app.RunAsync();

public enum DebugMode
{
    Disabled = 0,
    Console = 1,
    Message = 2
}
