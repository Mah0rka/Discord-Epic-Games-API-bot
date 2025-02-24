/// <summary>
/// Комарер для GameInfo для порівняння списків.
/// </summary>
public class GameInfoComparer : IEqualityComparer<GameInfo>
{
    public bool Equals(GameInfo x, GameInfo y)
    {
        if (x == null && y == null)
            return true;
        if (x == null || y == null)
            return false;
        return x.Title == y.Title && x.GameUrl == y.GameUrl && x.ImgUrl == y.ImgUrl;
    }

    public int GetHashCode(GameInfo obj)
    {
        return (obj.Title, obj.GameUrl, obj.ImgUrl).GetHashCode();
    }
}