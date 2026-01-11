namespace SnakeGame.Client.Models;

record struct Pos(int X, int Y);

enum Direction
{
    Up,
    Down,
    Left,
    Right
}

class ServerState
{
    public int Tick { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Pos> Foods { get; set; } = new();
    public List<PlayerView> Players { get; set; } = new();
}

class PlayerView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Alive { get; set; }
    public int Score { get; set; }
    public Direction Direction { get; set; }
    public List<Pos> Segments { get; set; } = new();
}

