# Pidor Bot

A grumpy Telegram bot that is a member of our chats. It curses, calls everyone "пидор", has moods, remembers people
and what happened, and does things in the chat like a person would: replies, reacts to messages, sends stickers and pictures,
and sets reminders when asked in plain speech.

It's the successor of [FoulBot](https://github.com/ewancoder/foulbot), focused on a single bot. It runs on the same
infrastructure and keeps all the FoulBot data: chat history, hardcore mode, allowed chats, reminders, custom names.

## How it works

- **Private bot.** It only works in chats where someone wrote `$activate` (`$deactivate` turns it off).
- **When it talks.** In private chats it replies to everything. In groups it replies to replies to its messages, to mentions,
  and to "бот" / "пидор" as whole words. It also interjects on "бот" / "пидор" inside other words (like "работа"), at most once an hour.
  It sometimes glances at other messages and maybe reacts to them, and when a group has been quiet for 6–24 hours it writes something on its own (never at night).
- **Tools.** On each turn the chat model (Grok) gets tools and decides what to do: write, `react` to a message, `send_sticker`,
  `send_image`, `set_reminder`, `cancel_reminder`. Any combination works, or it does nothing. It sees the photos people send.
  It learns stickers from the stickers people send, including their whole sticker sets.
- **Modes.** `mild` (the default: a polite old gentleman) and `hardcore` (filthy, angry). Switch with `$pidormode` or `$mode <name>`.
  Each chat has its own mode. To add a mode, add it to `Brain/Modes.cs`.
- **Moods.** Every 25–40 minutes the thinking model (Grok reasoning) comes up with a new mood. It's based on the mode, the chat memory,
  recent messages and the time of day. The hardcoded moods of the mode are the fallback. Moods are refreshed in the background, so replies don't wait.
- **Memory.** Every ~40 messages (or after 4 hours with some activity) the thinking model rewrites the chat memory:
  who the people are and how the bot feels about them, recent topics, the vibe of the chat, and what the bot has said about itself (so it stays consistent).
  The memory goes into every prompt. The bot sees only the last 10 messages directly, the memory covers the rest.
- **Voice.** Every 11th reply is a voice message (OpenAI TTS).

All prompt text lives in `Brain/Modes.cs` and `Brain/Prompts.cs`.

## Chat commands

| Command | |
|---|---|
| `$activate` / `$deactivate` | Turn the bot on / off in this chat |
| `$pidormode` | Toggle hardcore mode |
| `$mode`, `$mode <name>` | List modes / switch mode |
| `$status` | Current mode, mood, memory, reminders |
| `$memory` | What the bot remembers about this chat |
| `$forget` | Wipe the memory |
| `$newmood` | Think of a new mood right now |

## Configuration

Secrets have the same names as in FoulBot (`/data/foulbot/secrets.env` on the server):
`EwPidorBotApiKey`, `OpenAIKey`, `GrokApiKey`, `RedisConnectionString`, `SeqApiKey`.

Optional, as `PidorBot__<Name>` environment variables:

| Name | Default | |
|---|---|---|
| `ChatModel` | `grok/grok-4-1-fast-non-reasoning` | Talks in the chat (`grok/…` or `openai/…`) |
| `ThinkingModel` | `grok/grok-4-1-fast-reasoning` | Moods and memory |
| `ImageModel` | `openai/gpt-image-1-mini` | Pictures |
| `ChatTemperature` | `1.3` | Empty to not send it |
| `GlanceChance` | `0.2` | Chance to look at a message not addressed to the bot |
| `VoiceEveryReplies` | `10` | `0` disables voice |
| `VoiceModel`, `VoiceName` | `tts-1`, `onyx` | |
| `LlmMoods`, `Memory` | `true` | |
| `TimeZone` | `Asia/Tbilisi` | Time zone of the chat people |
| `SeqUrl` | `http://31.146.143.167:5341` | Not used in Development |
| `DataDirectory` | `.` | Where `allowed_chats` and `reminders/` are |

## Storage

| | Where | Shared with FoulBot |
|---|---|---|
| Chat history | Redis `chat_context_{chatId}` (`…_{botUsername}` for private chats) | yes |
| Hardcore mode | Redis `special_mode_ew_pidorbot_{chatId}` is read, `pidorbot:{chatId}:mode` is written | migrated on switch |
| Custom names | Redis `hard_names` (read only) | yes |
| Mood, memory | Redis `pidorbot:{chatId}:mood`, `pidorbot:{chatId}:memory` | new |
| Stickers | Redis `pidorbot:{botUsername}:stickers` | new |
| Allowed chats | `/app/allowed_chats` file, entries of other FoulBot bots are kept | yes |
| Reminders | `/app/reminders/{chatId}---{botUsername}` files | yes |

## Development

```sh
docker compose up -d                      # Redis
dotnet user-secrets --project src/PidorBot set EwTest1BotApiKey <test bot token>
dotnet run --project src/PidorBot         # Development: uses the test bot, no Seq
dotnet test
```

The user secrets id is the same as FoulBot's, so existing local secrets work.

## Deployment

Pushing to `main` runs the tests, builds `ghcr.io/<repo>:main`, and deploys over SSH using the same secrets as FoulBot
(`HOST`, `USERNAME`, `PASSWORD`, `KEY`, `PORT`). `docker-compose-production.yml` has the same service names, container names,
volumes and network as FoulBot's, so `docker compose up -d` replaces the old bot in place.

## Code

```
src/PidorBot/
  Program.cs            Wiring
  Settings.cs           Configuration
  Telegram/             Receiving updates, sending messages
  Chats/                ChatHub (all chats, access), ChatSession (one chat: history, when to reply, talking on its own),
                        ChatTurn (carries out what the bot decided to do)
  Brain/                Modes and prompts, ReplyPolicy, Responder (LLM turn with tools), MoodKeeper, MemoryKeeper
  AI/                   LLM (Grok/OpenAI via OpenAI SDK), voice, images
  Storage/              Redis and files, compatible with FoulBot
  Reminders/            Reminders storage
```
