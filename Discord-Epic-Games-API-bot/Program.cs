using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace DiscordEpicGamesBot
{
    /// <summary>
    /// Основний клас для Discord-бота.
    /// </summary>
    class Program
    {
        // ==================== Налаштування клієнта та команд ====================
        private static DiscordSocketClient _client;
        private static CommandService _commands;
        private static readonly string TOKEN = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

        // За замовчуванням використовуємо цей канал для автооновлення (можна змінити за допомогою команди)
        private static ulong defaultChannelId = 123456789012345678;

        // ==================== Захист від флуду ====================
        private static readonly TimeSpan commandCooldown = TimeSpan.FromMinutes(3);
        private static readonly ConcurrentDictionary<ulong, DateTime> userLastCommandTime = new ConcurrentDictionary<ulong, DateTime>();

        // ==================== Налаштування каналів для серверів ====================
        // Ключ – GuildId, значення – ChannelId для відправки повідомлень з іграми
        private static readonly ConcurrentDictionary<ulong, ulong> guildGameChannels = new ConcurrentDictionary<ulong, ulong>();

        // ==================== Сервіс для роботи з іграми ====================
        private static GameService _gameService;

        // ==================== Main ====================
        static async Task Main(string[] args)
        {
            InitializeDiscordClient();
            _gameService = new GameService(); // ініціалізуємо сервіс

            _client.Log += Log;
            _client.Ready += OnReady;
            _client.MessageReceived += HandleCommandAsync;

            await _client.LoginAsync(TokenType.Bot, TOKEN);
            await _client.StartAsync();

            StartAutoUpdate();

            await Task.Delay(-1);
        }

        private static void InitializeDiscordClient()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                               | GatewayIntents.GuildMessages
                               | GatewayIntents.MessageContent
            });
            _commands = new CommandService();
        }

        // ==================== Логування ====================
        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private static async Task OnReady()
        {
            Console.WriteLine($"Бот {_client.CurrentUser} запущений!");
            await Task.CompletedTask;
        }

        // ==================== Обробка команд ====================
        private static async Task HandleCommandAsync(SocketMessage messageParam)
        {
            if (messageParam is not SocketUserMessage message || message.Author.IsBot)
                return;

            int argPos = 0;
            if (!message.HasStringPrefix("!", ref argPos))
                return;

            string commandLine = message.Content.Substring(argPos);
            // Розділяємо команду та аргументи
            string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            string command = parts[0].ToLower();

            switch (command)
            {
                case "freegames":
                    await HandleFreeGamesCommand(message);
                    break;
                case "status":
                    await message.Channel.SendMessageAsync("✅ Бот працює!");
                    break;
                case "setgamechannel":
                    await HandleSetGameChannelCommand(message, parts);
                    break;
            }
        }

        private static async Task HandleFreeGamesCommand(SocketUserMessage message)
        {
            // Захист команди по кулдауну
            if (userLastCommandTime.TryGetValue(message.Author.Id, out DateTime lastUsed))
            {
                TimeSpan elapsed = DateTime.UtcNow - lastUsed;
                if (elapsed < commandCooldown)
                {
                    await message.Channel.SendMessageAsync(
                        $"Будь ласка, зачекайте 3 хвилини, перш ніж використовувати команду знову.");
                    return;
                }
            }
            userLastCommandTime[message.Author.Id] = DateTime.UtcNow;

            Console.WriteLine($"Отримано команду freegames від {message.Author.Username}");
            if (!_gameService.IsDataCached())
            {
                Console.WriteLine("Оновлюємо дані...");
                await _gameService.RefreshGameList();
            }
            await SendGames(message.Channel, _gameService.CurrentGames, _gameService.LastUpdate);
        }

        private static async Task HandleSetGameChannelCommand(SocketUserMessage message, string[] parts)
        {
            // Перевірка прав: перевіряємо, чи користувач має права адміністратора
            if (!(message.Author is SocketGuildUser guildUser) || !guildUser.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("У вас недостатньо прав для зміни налаштувань.");
                return;
            }

            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Будь ласка, вкажіть канал. Приклад: `!setgamechannel #games`");
                return;
            }

            // Спробуємо отримати канал із згадки
            SocketTextChannel targetChannel = message.MentionedChannels.FirstOrDefault() as SocketTextChannel;
            if (targetChannel == null)
            {
                await message.Channel.SendMessageAsync("Не вдалося знайти згаданий канал. Переконайтеся, що ви вказали канал.");
                return;
            }

            // Записуємо налаштування для даної гільдії (серверу)
            guildGameChannels[guildUser.Guild.Id] = targetChannel.Id;
            await message.Channel.SendMessageAsync($"Канал для повідомлень з іграми встановлено: {targetChannel.Mention}");
        }

        // ==================== Автооновлення ====================
        private static void StartAutoUpdate()
        {
            var timer = new System.Timers.Timer(_gameService.UpdateInterval.TotalMilliseconds);

            timer.Elapsed += async (sender, e) =>
            {
                Console.WriteLine("Автооновлення: оновлюємо дані.");
                bool changed = await _gameService.RefreshGameList();
                if (!changed)
                {
                    Console.WriteLine("Список ігор не змінився після оновлення.");
                    return;
                }

                // Відправляємо оновлення у всі налаштовані канали для серверів
                foreach (var kvp in guildGameChannels)
                {
                    ulong channelId = kvp.Value;
                    Console.WriteLine($"Відправляємо повідомлення в канал з ID: {channelId}");
                    if (_client.GetChannel(channelId) is ISocketMessageChannel channel)
                    {
                        await SendGames(channel, _gameService.CurrentGames, _gameService.LastUpdate);
                    }
                    else
                    {
                        Console.WriteLine($"Не вдалося отримати канал з ID: {channelId}");
                    }
                }
            };
            timer.AutoReset = true;
            timer.Enabled = true;
            Console.WriteLine("Таймер автооновлення запущено!");
        }

        // ==================== Відправлення ігор ====================
        private static async Task SendGames(ISocketMessageChannel channel, List<GameInfo> games, DateTime lastUpdate)
        {
            if (games.Count == 0)
            {
                await channel.SendMessageAsync("Немає безкоштовних ігор на даний момент.");
                return;
            }

            await channel.SendMessageAsync($"Дані оновлено <t:{new DateTimeOffset(lastUpdate).ToUnixTimeSeconds()}:R>.");

            foreach (var gameInfo in games)
            {
                var embed = new EmbedBuilder()
                    .WithTitle(gameInfo.Title)
                    .WithUrl(gameInfo.GameUrl)
                    .WithDescription("💥 **Безкоштовно зараз в Epic Games Store!** 💥")
                    .WithColor(Color.Green)
                    .WithImageUrl(gameInfo.ImgUrl)
                    .Build();

                await channel.SendMessageAsync(embed: embed);
            }
        }
    }

}
