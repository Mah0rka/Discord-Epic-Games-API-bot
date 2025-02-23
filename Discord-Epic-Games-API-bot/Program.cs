using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

class Program
{
    // ==================== Налаштування клієнта та команд ====================
    private static DiscordSocketClient _client;
    private static CommandService _commands;
    private static readonly string TOKEN = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
    // За замовчуванням використовуємо цей канал для автооновлення (можна змінити за допомогою команди)
    private static ulong defaultChannelId = 123456789012345678;

    // ==================== Дані гри та кешування ====================
    private static List<GameInfo> gameInfos = new List<GameInfo>();
    private static DateTime lastUpdate = DateTime.MinValue;
    private static readonly TimeSpan updateInterval = TimeSpan.FromHours(12);

    // ==================== Захист від флуду ====================
    private static readonly TimeSpan commandCooldown = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<ulong, DateTime> userLastCommandTime = new ConcurrentDictionary<ulong, DateTime>();

    // ==================== Налаштування каналів для серверів ====================
    // Ключ – GuildId, значення – ChannelId для відправки повідомлень з іграми
    private static readonly ConcurrentDictionary<ulong, ulong> guildGameChannels = new ConcurrentDictionary<ulong, ulong>();

    // ==================== Main ====================
    static async Task Main(string[] args)
    {
        InitializeDiscordClient();

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
                TimeSpan waitTime = commandCooldown - elapsed;
                await message.Channel.SendMessageAsync(
                    $"Будь ласка, зачекайте ще {waitTime.Minutes} хвилин {waitTime.Seconds} секунд, перш ніж використовувати команду знову.");
                return;
            }
        }
        userLastCommandTime[message.Author.Id] = DateTime.UtcNow;

        Console.WriteLine($"Отримано команду freegames від {message.Author.Username}");
        Console.WriteLine($"Час останнього оновлення: {lastUpdate}, поточний час: {DateTime.UtcNow}");
        Console.WriteLine($"Різниця: {(DateTime.UtcNow - lastUpdate).TotalHours} годин, Кількість кешованих ігор: {gameInfos.Count}");

        if (IsDataCached())
        {
            await message.Channel.SendMessageAsync(
                $"Дані оновлено <t:{new DateTimeOffset(lastUpdate).ToUnixTimeSeconds()}:R>. Використовую кешований результат.");
        }
        else
        {
            Console.WriteLine("Оновлюємо дані...");
            await RefreshGameList();
        }
        await SendGames(message.Channel);
    }

    private static async Task HandleSetGameChannelCommand(SocketUserMessage message, string[] parts)
    {
        // Перевірка прав: можна додатково перевіряти чи користувач має права адміністратора
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

        // Спробуємо отримати канал з згадки
        SocketTextChannel targetChannel = message.Channel as SocketTextChannel;
        if (targetChannel == null)
        {
            // Обробка ситуації, якщо канал не є текстовим
            await message.Channel.SendMessageAsync("Не вдалося знайти згаданий канал. Переконайтеся, що ви вказали канал.");
            return;
        }

        // Записуємо налаштування для даної гільдії (серверу)
        guildGameChannels[message.Channel.Id] = targetChannel.Id;
        await message.Channel.SendMessageAsync($"Канал для повідомлень з іграми встановлено: {targetChannel.Mention}");
    }

    private static bool IsDataCached()
    {
        return (DateTime.UtcNow - lastUpdate) < updateInterval && gameInfos.Count > 0;
    }

    // ==================== Автооновлення ====================
    private static void StartAutoUpdate()
    {
        var timer = new System.Timers.Timer(updateInterval.TotalMilliseconds);
        timer.Elapsed += async (sender, e) =>
        {
            Console.WriteLine("Автооновлення: оновлюємо дані.");
            await RefreshGameList();

            // Ітеруємо по налаштованим каналам
            foreach (var kvp in guildGameChannels)
            {
                ulong channelId = kvp.Value;
                Console.WriteLine($"ID: {channelId}");
                if (_client.GetChannel(channelId) is ISocketMessageChannel channel)
                {
                    await SendGames(channel);
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
    private static async Task SendGames(ISocketMessageChannel channel)
    {
        if (gameInfos.Count == 0)
        {
            await channel.SendMessageAsync("Немає безкоштовних ігор на даний момент.");
            return;
        }

        await channel.SendMessageAsync($"Дані оновлено <t:{new DateTimeOffset(lastUpdate).ToUnixTimeSeconds()}:R>.");

        foreach (var gameInfo in gameInfos)
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

    // ==================== Оновлення списку ігор ====================
    private static async Task RefreshGameList()
    {
        try
        {
            string url = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=us";
            using HttpClient client = new HttpClient();
            string response = await client.GetStringAsync(url);
            JObject data = JObject.Parse(response);

            gameInfos.Clear();
            var games = data["data"]?["Catalog"]?["searchStore"]?["elements"];
            if (games == null || !games.HasValues)
            {
                Console.WriteLine("Не вдалося знайти ігри в JSON.");
                return;
            }

            foreach (var game in games)
            {
                // Перевірка наявності промоцій та безкоштовної гри
                var promotions = game["promotions"];
                if (promotions == null || promotions.Type != JTokenType.Object)
                    continue;

                var currentPromos = promotions["promotionalOffers"];
                if (currentPromos == null || !currentPromos.HasValues)
                    continue;

                int discountPrice = game["price"]?["totalPrice"]?["discountPrice"]?.Value<int>() ?? -1;
                if (discountPrice != 0)
                    continue;

                var gameInfo = new GameInfo
                {
                    Title = game["title"]?.ToString() ?? "Без назви",
                    Slug = game["productSlug"]?.ToString(),
                    GameUrl = game["productSlug"] != null
                                ? $"https://store.epicgames.com/p/{game["productSlug"]}"
                                : "https://store.epicgames.com/",
                    ImgUrl = game["keyImages"]?[0]?["url"]?.ToString() ?? ""
                };

                gameInfos.Add(gameInfo);
            }

            lastUpdate = DateTime.UtcNow;
            Console.WriteLine($"Оновлено список ігор. Новий час оновлення: {lastUpdate}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка при оновленні списку ігор: {ex}");
        }
    }
}