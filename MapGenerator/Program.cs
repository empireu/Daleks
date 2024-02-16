using System.Text;
using Common;
using MapGenerator;

Console.WriteLine(Simulacru.TimeSeed());

var gr = Simulacru.Generate(new Vector2ds(127, 127), 3141, true);

var sb = new StringBuilder();

for (var y = 0; y < gr.Size.Y; y++)
{
    for (var x = 0; x < gr.Size.X; x++)
    {
        sb.Append(gr[x, y].Char());
        sb.Append(' ');
    }

    sb.AppendLine();
}

File.WriteAllText("a.txt", sb.ToString());