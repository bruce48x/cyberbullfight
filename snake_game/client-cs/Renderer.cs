using System.Text;
using SnakeGame.Client.Models;

namespace SnakeGame.Client;

class Renderer
{
    private readonly string _playerName;

    public Renderer(string playerName)
    {
        _playerName = playerName;
    }

    public void Draw(ServerState state, int myId)
    {
        var width = state.Width > 0 ? state.Width : 32;
        var height = state.Height > 0 ? state.Height : 18;
        var sb = new StringBuilder();

        sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();

        for (var y = 0; y < height; y++)
        {
            sb.Append('|');
            for (var x = 0; x < width; x++)
            {
                var pos = new Pos(x, y);
                char cell = ' ';

                if (state.Foods.Any(f => f.Equals(pos)))
                {
                    cell = '@';
                }

                foreach (var p in state.Players)
                {
                    if (!p.Alive) continue;
                    if (p.Segments.Count == 0) continue;
                    if (p.Segments[0].Equals(pos))
                    {
                        cell = p.Id == myId ? 'O' : 'Q';
                        break;
                    }
                    if (p.Segments.Skip(1).Any(s => s.Equals(pos)))
                    {
                        cell = p.Id == myId ? 'o' : 'q';
                    }
                }

                sb.Append(cell);
            }
            sb.Append('|').AppendLine();
        }

        sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();
        sb.Append($"You: {_playerName} (id {myId})  Controls: WASD | Players: ");
        foreach (var p in state.Players)
        {
            sb.Append($"{p.Name}[{p.Score}] ");
        }

        Console.SetCursorPosition(0, 0);
        Console.Write(sb.ToString());
    }
}

