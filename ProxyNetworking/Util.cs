namespace ProxyNetworking;

public static class Util
{
    public static string Prompt(string text)
    {
        Console.Write(text);
        var r = Console.ReadLine() ?? string.Empty;
        Console.WriteLine();
        return r;
    }

    public static int PromptInt(string text)
    {
        if (!int.TryParse(Prompt(text), out var i))
        {
            throw new Exception("Invalid input");
        }

        return i;
    }
}