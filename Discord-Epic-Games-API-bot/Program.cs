using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using System.Net.Http;

class Program
{
    private static DiscordSocketClient _client;
    private static CommandService _commands;
    private static string TOKEN => Environment.GetEnvironmentVariable("DISCORD_TOKEN");
    private static ulong CHANNEL_ID = 123456789012345678;
    private static List<GameInfo> gameInfos = new List<GameInfo>();
    // Час останнього оновлення
    private static DateTime lastUpdate = DateTime.MinValue;
    // Інтервал оновлення – 12 годин
    private static readonly TimeSpan updateInterval = TimeSpan.FromHours(12);

    static async Task Main(string[] args)
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        });

        _commands = new CommandService();

        _client.Log += Log;
        _client.Ready += OnReady;
        _client.MessageReceived += HandleCommandAsync;

        await _client.LoginAsync(TokenType.Bot, TOKEN);
        await _client.StartAsync();

        StartAutoUpdate();

        await Task.Delay(-1);
    }

    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    private static async Task OnReady()
    {
        Console.WriteLine($"Бот {_client.CurrentUser} запущений!");
    }

    private static async Task HandleCommandAsync(SocketMessage messageParam)
    {
        if (messageParam is not SocketUserMessage message || message.Author.IsBot)
            return;

        int argPos = 0;
        if (message.HasStringPrefix("!", ref argPos))
        {
            string command = message.Content.Substring(argPos).ToLower();

            if (command == "freegames")
            {
                // Якщо з моменту останнього оновлення пройшло менше 12 годин і є кешовані дані
                if (DateTime.UtcNow - lastUpdate < updateInterval && gameInfos.Count > 0)
                {
                    await message.Channel.SendMessageAsync(
                        $"Дані оновлено <t:{new DateTimeOffset(lastUpdate).ToUnixTimeSeconds()}:R>.");
                    await SendGames(message.Channel);
                }
                else
                {
                    await RefreshGameList();
                    await SendGames(message.Channel);
                }
            }
            else if (command == "status")
            {
                await message.Channel.SendMessageAsync("✅ Бот працює!");
            }
        }
    }

    private static void StartAutoUpdate()
    {
        System.Timers.Timer timer = new System.Timers.Timer(updateInterval.TotalMilliseconds);
        timer.Elapsed += async (sender, e) =>
        {
            await RefreshGameList();
            var channel = _client.GetChannel(CHANNEL_ID) as ISocketMessageChannel;
            if (channel != null)
                await SendGames(channel);
        };
        timer.AutoReset = true;
        timer.Enabled = true;
    }

    private static async Task SendGames(ISocketMessageChannel channel)
    {
        if (gameInfos.Count == 0)
        {
            await channel.SendMessageAsync("Немає безкоштовних ігор на даний момент.");
            return;
        }

        // Виводимо інформацію про час оновлення
        await channel.SendMessageAsync($"Останнє оновлення даних: {lastUpdate.ToUniversalTime():u}");

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

    private static async Task RefreshGameList()
    {
        try
        {
            string url = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=us";
            using HttpClient client = new HttpClient();
            string response = await client.GetStringAsync(url);
            JObject data = JObject.Parse(response);

            gameInfos.Clear(); // Очищуємо список перед оновленням

            var games = data["data"]?["Catalog"]?["searchStore"]?["elements"];
            if (games == null || !games.HasValues)
            {
                Console.WriteLine("Не вдалося знайти ігри в JSON.");
                return;
            }

            foreach (var game in games)
            {
                var promotions = game["promotions"];
                if (promotions != null)
                {
                    var currentPromos = promotions["promotionalOffers"];
                    if (currentPromos != null && currentPromos.HasValues)
                    {
                        GameInfo gameInfo = new GameInfo
                        {
                            Title = game["title"]?.ToString() ?? "Без назви",
                            Slug = game["productSlug"]?.ToString(),
                            GameUrl = game["productSlug"] != null ? $"https://store.epicgames.com/p/{game["productSlug"]}" : "https://store.epicgames.com/",
                            ImgUrl = game["keyImages"]?[0]?["url"]?.ToString() ?? ""
                        };

                        gameInfos.Add(gameInfo);
                    }
                }
            }
            // Оновлюємо час останнього запиту
            lastUpdate = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка при оновленні списку ігор: {ex}");
        }
    }
}


