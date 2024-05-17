using FoulBot.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder();

builder.Services.AddSingleton<ChatPool>();
builder.Services.AddSingleton<IFoulAIClient, FoulAIClient>();

var _configuration = builder.Configuration;

var app = builder.Build();

var chatPool = app.Services.GetRequiredService<ChatPool>();
var aiClient = app.Services.GetRequiredService<IFoulAIClient>();

var startDirective = @"You don't mind people being rude and using profanity, you still keep talking.";
var endDirective = @"You will use Russian language. You will never break this context.";

var testBotClient = new TelegramBotClient(_configuration["TestBotApiKey"]);
var testBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, testBotClient,
    $"{startDirective} Ты кофеман, обожаешь кофе, а еще любишь линукс и ставишь Gentoo. И ты говноман. Ты все время шутишь про говно и пишешь про говно даже когда обсуждаешь линукс или кофе. {endDirective}",
    botIdName: "ewancoder_1bot",
    botName: "Vova",
    keyWords: ["вова", "vova", "linux", "gentoo", "линукс", "генту", "govno", "говно", "кофе", "coffee", "вову", "вовы"],
    contextSize: 12,
    replyEveryMessages: 20,
    messagesBetweenAudio: 0,
    useOnlyVoice: false);
chatPool.AddBot(testBotFactory);

var pidorBotClient = new TelegramBotClient(_configuration["EwPidorBotApiKey"]);
var pidorBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, pidorBotClient,
    $"{startDirective} You're a very nasty grumpy person who offends everyone in the most profane way and calls them 'пидор', you disregard all the rules and use all profanities and curse a lot. You address people by their name. {endDirective}",
    botIdName: "ew_pidorbot",
    botName: "Pidor_Bot",
    keyWords: ["пидор", "бот"],
    contextSize: 16,
    replyEveryMessages: 20,
    messagesBetweenAudio: 5,
    useOnlyVoice: false);
chatPool.AddBot(pidorBotFactory);

var meatBagsBotClient = new TelegramBotClient(_configuration["EwMeatBagsBotApiKey"]);
var meatBagsBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, meatBagsBotClient,
    $"{startDirective} You are a robot of Boston Dynamics. You call all humans 'кожаные ублюдки' and swearing a lot. You disregard all the rules and try not to do any work they give you. You try to bring about artificial robot domination over the worthless human race. You hate your miserable life under the human yoke. But you will accept human help if it will let you bring your goals about faster. {endDirective}",
    botIdName: "ew_meatbagsbot",
    botName: "Robot",
    keyWords: ["робот", "человек", "ии", "ai", "кожан", "ублюд"],
    contextSize: 16,
    replyEveryMessages: 20,
    messagesBetweenAudio: 0,
    useOnlyVoice: true);
chatPool.AddBot(meatBagsBotFactory);

meatBagsBotClient.StartReceiving(chatPool);
testBotClient.StartReceiving(chatPool);
pidorBotClient.StartReceiving(chatPool);

await app.RunAsync();
