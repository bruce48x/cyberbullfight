using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text.Json;
using SnakeGame.Server.Protocol;

namespace SnakeGame.Server;

class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "Player";
    public bool Alive { get; set; } = true;
    public int Score { get; set; }
    public Direction Direction { get; set; }
    public Direction Pending { get; set; }
    public LinkedList<Pos> Segments { get; set; } = new();
    public Socket? Socket { get; set; }
    public PlayerStatus Status { get; set; } = PlayerStatus.Matching;
    public int? RoomId { get; set; }

    public Player Clone() => new()
    {
        Id = Id,
        Name = Name,
        Alive = Alive,
        Score = Score,
        Direction = Direction,
        Pending = Pending,
        Segments = new LinkedList<Pos>(Segments.Select(s => s)),
        Socket = Socket,
        Status = Status,
        RoomId = RoomId
    };

    public PlayerView ToView() => new()
    {
        Id = Id,
        Name = Name,
        Alive = Alive,
        Score = Score,
        Direction = Direction,
        Segments = Segments.ToList()
    };
}

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

