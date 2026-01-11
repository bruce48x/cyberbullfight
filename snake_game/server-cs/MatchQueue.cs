using System.Collections;
using System.Linq;
using SnakeGame.Server.Protocol;

namespace SnakeGame.Server;

// 匹配队列类，管理玩家匹配逻辑
class MatchQueue
{
    private readonly int _matchSize;
    private readonly object _queueLock = new();
    private readonly Queue<Player> _queue = new();

    public MatchQueue(int matchSize = 2)
    {
        _matchSize = matchSize;
    }

    // 添加玩家到匹配队列
    public void Enqueue(Player player)
    {
        lock (_queueLock)
        {
            // 检查玩家是否已在队列中
            if (_queue.Any(p => p.Id == player.Id))
                return;

            _queue.Enqueue(player);
            player.Status = PlayerStatus.Matching;
        }
    }

    // 尝试匹配并返回满员的房间玩家列表（如果有）
    public List<Player>? TryMatch()
    {
        lock (_queueLock)
        {
            if (_queue.Count < _matchSize)
                return null;

            var matchedPlayers = new List<Player>();
            for (int i = 0; i < _matchSize; i++)
            {
                if (_queue.TryDequeue(out var player))
                {
                    matchedPlayers.Add(player);
                }
            }

            return matchedPlayers;
        }
    }

    // 从队列中移除玩家
    public void Remove(Player player)
    {
        lock (_queueLock)
        {
            // 由于Queue不支持直接移除，需要重建队列
            var tempList = _queue.ToList();
            tempList.RemoveAll(p => p.Id == player.Id);
            _queue.Clear();
            foreach (var p in tempList)
            {
                _queue.Enqueue(p);
            }
        }
    }

    // 获取队列长度
    public int Count
    {
        get
        {
            lock (_queueLock)
            {
                return _queue.Count;
            }
        }
    }
}

// 玩家状态
enum PlayerStatus
{
    Matching,  // 匹配中
    InGame     // 游戏中
}

