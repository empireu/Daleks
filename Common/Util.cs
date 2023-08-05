namespace Common;

public static class Util
{
    public static string Prompt(string text, string @default = "")
    {
        Console.Write(text);

        var r = Console.ReadLine();

        if (string.IsNullOrEmpty(r))
        {
            r = @default;
        }

        Console.WriteLine();
        
        return r;
    }

    public static int PromptInt(string text, int? @default = null)
    {
        Console.Write(text);

        var r = Console.ReadLine();

        if (string.IsNullOrEmpty(r))
        {
            if (@default.HasValue)
            {
                Console.WriteLine();
                return @default.Value;
            }

            throw new Exception("Input expected");
        }

        if (!int.TryParse(r, out var i))
        {
            throw new Exception($"Invalid input \"{r}\"");
        }

        Console.WriteLine();

        return i;
    }

    public static void Log(string str, ConsoleColor color)
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(str);
        Console.ForegroundColor = c;
    }

    public static void LogInfo(string str) => Log(str, ConsoleColor.Green);
    public static void LogWarn(string str) => Log(str, ConsoleColor.Yellow);
    public static void LogError(string str) => Log(str, ConsoleColor.Red);
}