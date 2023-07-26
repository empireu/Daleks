namespace Daleks;

internal static class Extensions
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
}