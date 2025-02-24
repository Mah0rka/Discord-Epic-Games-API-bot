using Newtonsoft.Json.Linq;

/// <summary>
/// Сервіс для роботи з даними ігор.
/// </summary>
public class GameService
{
    private List<GameInfo> _gameInfos = new List<GameInfo>();
    public DateTime LastUpdate { get; private set; } = DateTime.MinValue;
    public TimeSpan UpdateInterval { get; } = TimeSpan.FromMinutes(2);

    public List<GameInfo> CurrentGames => _gameInfos;

    /// <summary>
    /// Перевіряє, чи кешовані дані є актуальними.
    /// </summary>
    public bool IsDataCached()
    {
        return (DateTime.UtcNow - LastUpdate) < UpdateInterval && _gameInfos.Count > 0;
    }

    /// <summary>
    /// Завантажує дані з API та оновлює список ігор. Повертає true, якщо дані змінилися.
    /// </summary>
    public async Task<bool> RefreshGameList()
    {
        try
        {
            string url = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=us";
            using HttpClient client = new HttpClient();
            string response = await client.GetStringAsync(url);
            JObject data = JObject.Parse(response);

            var newGameList = new List<GameInfo>();
            var games = data["data"]?["Catalog"]?["searchStore"]?["elements"];
            if (games == null || !games.HasValues)
            {
                Console.WriteLine("Не вдалося знайти ігри в JSON.");
                return false;
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

                newGameList.Add(gameInfo);
            }

            // Порівнюємо новий список з попереднім
            bool changed = !newGameList.SequenceEqual(_gameInfos, new GameInfoComparer());
            if (changed)
            {
                _gameInfos = newGameList;
                LastUpdate = DateTime.UtcNow;
                Console.WriteLine($"Оновлено список ігор. Новий час оновлення: {LastUpdate}");
            }
            else
            {
                Console.WriteLine("Список ігор не змінився після оновлення.");
            }
            return changed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка при оновленні списку ігор: {ex}");
            return false;
        }
    }
}
