using System.Collections;
using System.Net.Sockets;
using System.Text.Json;
using SnakeGame.Server.Protocol;

namespace SnakeGame.Server;

// 房间状态
enum RoomStatus
{
    Waiting,  // 等待中
    Playing   // 游戏中
}

// 房间类，管理一个房间内的游戏逻辑
class Room
{
    private readonly int _roomId;
    private readonly int _width;
    private readonly int _height;
    private readonly TimeSpan _tick;
    private readonly Random _rng;
    private readonly object _stateLock = new();

    private RoomStatus _status = RoomStatus.Waiting;
    private readonly Dictionary<int, Player> _players = new();
    private readonly List<Pos> _foods = new();

    public int RoomId => _roomId;
    public RoomStatus Status => _status;
    public int PlayerCount => _players.Count;

    public Room(int roomId, int width = 32, int height = 18, TimeSpan tick = default)
    {
        _roomId = roomId;
        _width = width;
        _height = height;
        _tick = tick == default ? TimeSpan.FromMilliseconds(160) : tick;
        _rng = new Random();
        EnsureFood();
    }

    // 添加玩家到房间
    public bool AddPlayer(Player player)
    {
        lock (_stateLock)
        {
            if (_status != RoomStatus.Waiting)
                return false;

            if (_players.ContainsKey(player.Id))
                return false;

            // 初始化玩家位置
            var pos = FindSpawnPosition();
            var segs = new LinkedList<Pos>();
            segs.AddFirst(pos);
            segs.AddLast(pos with { X = pos.X - 1 });
            segs.AddLast(pos with { X = pos.X - 2 });

            player.Segments = segs;
            player.Direction = Direction.Right;
            player.Pending = Direction.Right;
            player.Alive = true;
            player.Score = 0;
            player.RoomId = _roomId;

            _players[player.Id] = player;
            return true;
        }
    }

    // 开始游戏
    public async Task StartGameAsync()
    {
        ServerState? initialState = null;
        lock (_stateLock)
        {
            if (_status != RoomStatus.Waiting || _players.Count == 0)
                return;

            _status = RoomStatus.Playing;
            EnsureFood();
            initialState = GetCurrentState();
        }

        // 在锁外部发送初始状态
        if (initialState != null)
        {
            await BroadcastStateAsync(initialState);
        }
    }

    // 游戏循环
    public async Task GameLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_tick, cancellationToken);

            ServerState? state = null;
            lock (_stateLock)
            {
                if (_status != RoomStatus.Playing)
                    continue;

                AdvanceWorld();

                // 统计存活玩家数量
                var alivePlayers = _players.Values.Where(p => p.Alive).ToList();
                var aliveCount = alivePlayers.Count;

                // 如果只剩一个玩家存活，判定该玩家获胜，结束游戏
                if (aliveCount == 1)
                {
                    var winner = alivePlayers[0];
                    Console.WriteLine($"Room {_roomId}: Player {winner.Id} ({winner.Name}) wins with score {winner.Score}!");
                    _status = RoomStatus.Waiting;
                    state = GetCurrentState();
                }
                // 如果所有玩家都死亡，结束游戏
                else if (aliveCount == 0)
                {
                    Console.WriteLine($"Room {_roomId}: All players dead, game over.");
                    _status = RoomStatus.Waiting;
                    state = GetCurrentState();
                }
                else
                {
                    // 游戏继续，正常发送状态
                    state = GetCurrentState();
                }
            }

            // 在锁外部广播状态
            if (state != null)
            {
                await BroadcastStateAsync(state);
            }
        }
    }

    // 移除玩家
    public void RemovePlayer(int playerId)
    {
        lock (_stateLock)
        {
            _players.Remove(playerId);
        }
    }

    // 获取房间内的所有玩家ID
    public List<int> GetPlayerIds()
    {
        lock (_stateLock)
        {
            return _players.Keys.ToList();
        }
    }

    // 检查房间是否可以关闭（没有玩家或游戏已结束）
    public bool CanClose()
    {
        lock (_stateLock)
        {
            // 如果没有玩家，可以关闭
            if (_players.Count == 0)
                return true;
            
            // 如果游戏状态是Waiting（游戏已结束），可以关闭
            if (_status == RoomStatus.Waiting)
                return true;
            
            return false;
        }
    }

    // 处理玩家移动指令
    public void HandlePlayerMove(int playerId, Direction dir)
    {
        lock (_stateLock)
        {
            if (!_players.TryGetValue(playerId, out var player))
                return;

            if (!IsOpposite(player.Direction, dir))
            {
                player.Pending = dir;
            }
        }
    }

    // 获取当前游戏状态
    private ServerState GetCurrentState()
    {
        return new ServerState
        {
            Tick = Environment.TickCount,
            Width = _width,
            Height = _height,
            Foods = _foods.ToList(),
            Players = _players.Values.Select(p => p.ToView()).ToList()
        };
    }

    // 广播状态给房间内所有玩家
    private async Task BroadcastStateAsync(ServerState state)
    {
        var stateData = JsonSerializer.SerializeToUtf8Bytes(state);
        var pushMsg = Message.Encode(0, MessageType.Push, false, "snake.state", stateData);
        var dataPkg = Package.Encode(PackageType.Data, pushMsg);

        // 在锁内获取玩家列表的副本
        List<Player> playersCopy;
        lock (_stateLock)
        {
            playersCopy = _players.Values.ToList();
        }

        var failed = new List<int>();
        foreach (var player in playersCopy)
        {
            try
            {
                if (player.Socket != null)
                {
                    // 使用 Socket.SendAsync 直接发送，减少数据复制
                    int totalSent = 0;
                    var data = new ReadOnlyMemory<byte>(dataPkg);
                    while (totalSent < data.Length)
                    {
                        var remaining = data.Slice(totalSent);
                        var sent = await player.Socket.SendAsync(remaining, System.Net.Sockets.SocketFlags.None);
                        if (sent == 0) break;
                        totalSent += sent;
                    }
                }
            }
            catch
            {
                failed.Add(player.Id);
            }
        }

        if (failed.Count > 0)
        {
            lock (_stateLock)
            {
                foreach (var id in failed)
                {
                    _players.Remove(id);
                }
            }
        }
    }

    // 推进游戏世界
    private void AdvanceWorld()
    {
        EnsureFood();
        if (_players.Count == 0) return;

        // 构建占用地图
        var occupancy = new HashSet<Pos>();
        foreach (var p in _players.Values.Where(p => p.Alive))
        {
            if (p.Segments.Count == 0) continue;
            foreach (var seg in p.Segments)
            {
                occupancy.Add(seg);
            }
        }

        foreach (var p in _players.Values.Where(p => p.Alive))
        {
            if (p.Segments.Count == 0) continue;
            var lastNode = p.Segments.Last;
            var firstNode = p.Segments.First;
            if (lastNode is null || firstNode is null) continue;

            // 允许移动到尾部，因为它会空出来
            occupancy.Remove(lastNode.Value);

            if (!IsOpposite(p.Direction, p.Pending))
            {
                p.Direction = p.Pending;
            }

            var nextHead = Step(firstNode.Value, p.Direction);
            var hitWall = nextHead.X < 0 || nextHead.X >= _width || nextHead.Y < 0 || nextHead.Y >= _height;
            var hitBody = occupancy.Contains(nextHead);
            if (hitWall || hitBody)
            {
                p.Alive = false;
                p.Segments.Clear();
                continue;
            }

            var ate = false;
            for (var i = 0; i < _foods.Count; i++)
            {
                if (_foods[i].Equals(nextHead))
                {
                    _foods.RemoveAt(i);
                    ate = true;
                    p.Score += 1;
                    break;
                }
            }

            p.Segments.AddFirst(nextHead);
            if (!ate)
            {
                p.Segments.RemoveLast();
            }
            occupancy.Add(nextHead);
        }

        EnsureFood();
    }

    // 确保有足够的食物
    private void EnsureFood()
    {
        const int targetFood = 1;
        while (_foods.Count < targetFood)
        {
            var candidate = new Pos(_rng.Next(0, _width), _rng.Next(0, _height));
            if (_players.Values.Any(p => p.Segments.Any(s => s.Equals(candidate)))) continue;
            _foods.Add(candidate);
        }
    }

    // 查找生成位置
    private Pos FindSpawnPosition()
    {
        int attempts = 0;
        while (true)
        {
            var pos = new Pos(_rng.Next(2, _width - 2), _rng.Next(2, _height - 2));
            var body = new[]
            {
                pos,
                pos with { X = pos.X - 1 },
                pos with { X = pos.X - 2 }
            };

            var collision = _players.Values.Any(p => p.Segments.Any(s => body.Contains(s)));
            if (!collision || attempts++ > 100)
            {
                return pos;
            }
        }
    }

    private static Pos Step(Pos p, Direction d) =>
        d switch
        {
            Direction.Up => p with { Y = p.Y - 1 },
            Direction.Down => p with { Y = p.Y + 1 },
            Direction.Left => p with { X = p.X - 1 },
            Direction.Right => p with { X = p.X + 1 },
            _ => p
        };

    private static bool IsOpposite(Direction a, Direction b) =>
        (a, b) switch
        {
            (Direction.Up, Direction.Down) => true,
            (Direction.Down, Direction.Up) => true,
            (Direction.Left, Direction.Right) => true,
            (Direction.Right, Direction.Left) => true,
            _ => false
        };
}

