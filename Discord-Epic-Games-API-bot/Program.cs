using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;

class Program
{
    private static DiscordSocketClient _client;
    private static CommandService _commands;
    private static string TOKEN => Environment.GetEnvironmentVariable("DISCORD_TOKEN"); // Замініть на свій токен
    private static ulong CHANNEL_ID = 123456789012345678; // Замініть на ID вашого каналу
    private static List<string> previousGames = new List<string>(); // Список попередніх ігор

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
        _client.MessageReceived += StatusHandleCommandAsync;

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
        if (messageParam is not SocketUserMessage message || message.Author.IsBot) return;

        int argPos = 0;
        if (message.HasStringPrefix("!", ref argPos))
        {
            var context = new SocketCommandContext(_client, message);
            string command = message.Content.Substring(argPos).ToLower();

            if (command == "freegames")
            {
                await SendFreeGames(context.Channel);
            }
        }
    }

    private static async Task StatusHandleCommandAsync(SocketMessage messageParam)
    {
        if (messageParam is not SocketUserMessage message || message.Author.IsBot) return;

        int argPos = 0;
        if (message.HasStringPrefix("!", ref argPos))
        {
            var context = new SocketCommandContext(_client, message);
            string command = message.Content.Substring(argPos).ToLower();

            if (command == "status")
            {
                await message.Channel.SendMessageAsync("Усе заїбісь");
            }
        }
    }

    private static void StartAutoUpdate()
    {
        System.Timers.Timer timer = new System.Timers.Timer(12 * 60 * 60 * 1000); // 24 годин
        timer.Elapsed += async (sender, e) => await AutoSendFreeGames(); // Обробник події Elapsed
        timer.AutoReset = true;
        timer.Enabled = true;
    }

    private static async Task AutoSendFreeGames()
    {
        var channel = _client.GetChannel(CHANNEL_ID) as ISocketMessageChannel;
        if (channel != null)
        {
            await SendFreeGames(channel);
        }
    }

    private static async Task SendFreeGames(ISocketMessageChannel channel)
    {
        string url = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=us";
        using HttpClient client = new HttpClient();
        string response = await client.GetStringAsync(url);
        JObject data = JObject.Parse(response);

        var games = data["data"]["Catalog"]["searchStore"]["elements"];
        bool foundFreeGames = false;
        List<string> currentGames = new List<string>(); // Список поточних ігор

        foreach (var game in games)
        {
            var promotions = game["promotions"];
            if (promotions != null)
            {
                var currentPromos = promotions["promotionalOffers"];

                if (currentPromos != null && currentPromos.HasValues)
                {
                    string title = game["title"]?.ToString() ?? "Без назви";
                    string slug = game["productSlug"]?.ToString();
                    string gameUrl = slug != null ? $"https://store.epicgames.com/p/{slug}" : "https://store.epicgames.com/";
                    string imgUrl = game["keyImages"]?[0]?["url"]?.ToString() ?? "";

                    currentGames.Add(title); // Додаємо гру до поточного списку

                    var embed = new EmbedBuilder()
                        .WithTitle(title)
                        .WithUrl(gameUrl)
                        .WithDescription("💥 **Безкоштовно зараз в Epic Games Store!** 💥")
                        .WithColor(Color.Green)
                        .WithImageUrl(imgUrl)
                        .Build();

                    await channel.SendMessageAsync(embed: embed);
                    foundFreeGames = true;
                }
            }
        }

        // Якщо є нові ігри, відправляємо, інакше не відправляємо
        if (!foundFreeGames)
        {
            await channel.SendMessageAsync("Зараз немає безкоштовних ігор у Epic Games Store.");
        }
        else
        {
            // Перевіряємо, чи змінився список ігор
            if (!currentGames.SequenceEqual(previousGames))
            {
                previousGames = new List<string>(currentGames); // Оновлюємо список ігор
            }
            else
            {
                await channel.SendMessageAsync("Немає нових безкоштовних ігор.");
            }
        }
    }
}
