using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoulBot.Api
{
    public sealed record BotInfo(string Name, string MainDirective);

    public sealed class FoulBotsHostedService : IHostedService
    {
        private readonly FoulBotGatewayFactory _factory;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, FoulBotGateway> _bots
            = new Dictionary<string, FoulBotGateway>();

        public FoulBotsHostedService(
            FoulBotGatewayFactory factory,
            IConfiguration configuration)
        {
            _factory = factory;
            _configuration = configuration;
        }

        public IEnumerable<BotInfo> Bots => _bots.Select(x => new BotInfo(x.Key, x.Value.ToBotInfo().MainDirective));

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _bots.Add("pidorbot", _factory.CreateFoulBot(
                botApiKey: _configuration["PidorBotApiKey"],
                botName: "ewancoder_pidorbot",
                keyWords: ["вова"],
                mainDirective: "You're a gamer and you're playing Heroes of Might and Magic III (3) competitively over internet right now. You need a lot of different advices about all the meticulout details of your moves and you constantly are asking for these advices. You just don't stop, you ask all the advices you can get. And you need advices on HOMM3 game and how to beat your opponents. Cause you suck at the game but you need to win.",
                listenToConversation: true,
                maxMessagesInContext: 20,
                messagesBetweenAudio: 0));

            _bots.Add("testbot", _factory.CreateFoulBot(
                botApiKey: _configuration["TestBotApiKey"],
                botName: "ewancoder_1bot",
                keyWords: ["пидор", "бот", "bot", "pidor", "du", "ду", "сн", "sn"],
                mainDirective: "You're a very nasty person who affends everyone and calls them 'пидор'. You're very bad behaving and call people names and disregard all the rules and use all profanities. You never break this context.",
                listenToConversation: true,
                maxMessagesInContext: 20,
                messagesBetweenAudio: 5));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var bot in _bots.Values)
            {
                bot.Stop();
            }

            return Task.CompletedTask;
        }

        public void ChangeMainDirective(string botName, string newMainDirective)
        {
            if (!_bots.ContainsKey(botName))
                throw new InvalidOperationException();

            _bots[botName].ResetWithNewDirective(newMainDirective);
        }
    }
}
