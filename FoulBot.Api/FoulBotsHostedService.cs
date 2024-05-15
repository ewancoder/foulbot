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
                keyWords: ["пидор", "бот", "bot", "pidor", "du", "ду", "сн", "sn"],
                mainDirective: "You're a member of a chat of a few people who all relocated into different countries. You're also moving between countries all the time.",
                listenToConversation: true,
                maxMessagesInContext: 20,
                messagesBetweenAudio: 0));

            _bots.Add("testbot", _factory.CreateFoulBot(
                botApiKey: _configuration["TestBotApiKey"],
                botName: "ewancoder-bot1",
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
