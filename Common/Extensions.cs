namespace Common;

public static class Extensions
{
    public static T Also<T>(this T t, Action<T> action)
    {
        action(t);
        return t;
    }

    public static T2 Map<T1, T2>(this T1 t, Func<T1, T2> m)
    {
        return m(t);
    }

    public static Vector2di Step(this Direction dir) => dir switch
    {
        Direction.L => new Vector2di(-1, 0),
        Direction.R => new Vector2di(1, 0),
        Direction.U => new Vector2di(0, -1),
        Direction.D => new Vector2di(0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, $"Unexpected direction {dir}")
    };

    public static bool IsObstacle(this TileType type) => type == TileType.Bedrock;
}