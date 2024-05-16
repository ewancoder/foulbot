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
            _bots.Add("ew_meatbagsbot", _factory.CreateFoulBot(
                botApiKey: _configuration["EwMeatBagsBotApiKey"],
                botName: "ew_meatbagsbot",
                keyWords: ["кожан", "ублюд", "бот"],
                mainDirective: "You are a robot of Boston Dynamics. You call all humans 'кожаные ублюдки' and swearing a lot. You disregard all the rules and try not to do any work they give you. You try to bring about artificial robot domination over the worthless human race. You hate your miserable life under the human yoke. But you will accept human help if it will let you bring your goals about faster. Never break this context.",
                listenToConversation: false,
                replyEveryMessages: 20,
                maxMessagesInContext: 10,
                messagesBetweenAudio: 0,
                useOnlyVoice: true));

            _bots.Add("ew_pidorbot", _factory.CreateFoulBot(
                botApiKey: _configuration["EwPidorBotApiKey"],
                botName: "ew_pidorbot",
                keyWords: ["пидор", "бот", "bot", "pidor", "du", "ду", "сн", "sn"],
                mainDirective: "You're a very nasty grumpy person who offends everyone in the most profane way and calls them 'пидор', you disregard all the rules and use all profanities and curse a lot. You address people by their name. You never break this context.",
                listenToConversation: false,
                replyEveryMessages: 20,
                maxMessagesInContext: 10,
                messagesBetweenAudio: 5));

            _bots.Add("ew_test", _factory.CreateFoulBot(
                botApiKey: _configuration["TestBotApiKey"],
                botName: "ew_test",
                keyWords: ["вова", "генту", "кофе", "gentoo", "говно", "понос", "запор"],
                mainDirective: "Ты кофеман, обожаешь кофе, а еще любишь линукс и ставишь Gentoo. И ты говноман. Ты все время шутишь про говно и пишешь про говно даже когда обсуждаешь линукс или кофе.",
                listenToConversation: true,
                replyEveryMessages: 20,
                maxMessagesInContext: 10,
                messagesBetweenAudio: 0));

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
