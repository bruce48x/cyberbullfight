using SnakeGame.Client.Models;

namespace SnakeGame.Client;

class AIController
{
    private readonly int _myId;

    public AIController(int myId)
    {
        _myId = myId;
    }

    public string? ChooseDirection(ServerState state, PlayerView myPlayer)
    {
        if (myPlayer.Segments.Count == 0) return null;

        var head = myPlayer.Segments[0];
        var currentDir = myPlayer.Direction;

        // 简单的AI策略：优先朝向食物移动，避免撞墙和自己的身体
        var foods = state.Foods;
        var allSegments = new HashSet<Pos>(myPlayer.Segments);
        
        // 获取所有其他玩家的身体位置
        foreach (var p in state.Players)
        {
            if (p.Id != _myId && p.Alive)
            {
                foreach (var seg in p.Segments)
                {
                    allSegments.Add(seg);
                }
            }
        }

        // 找到最近的食物
        Pos? nearestFood = null;
        double minDistance = double.MaxValue;
        foreach (var food in foods)
        {
            var dist = Math.Abs(food.X - head.X) + Math.Abs(food.Y - head.Y);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearestFood = food;
            }
        }

        // 尝试朝向食物移动
        if (nearestFood.HasValue)
        {
            var target = nearestFood.Value;
            var candidates = new List<(string dir, Pos pos, int priority)>();

            // 计算各个方向的优先级
            if (target.X > head.X && currentDir != Direction.Left)
            {
                var nextPos = new Pos(head.X + 1, head.Y);
                if (IsSafeMove(nextPos, state, allSegments))
                {
                    var priority = target.X - head.X;
                    candidates.Add(("Right", nextPos, priority));
                }
            }
            if (target.X < head.X && currentDir != Direction.Right)
            {
                var nextPos = new Pos(head.X - 1, head.Y);
                if (IsSafeMove(nextPos, state, allSegments))
                {
                    var priority = head.X - target.X;
                    candidates.Add(("Left", nextPos, priority));
                }
            }
            if (target.Y > head.Y && currentDir != Direction.Up)
            {
                var nextPos = new Pos(head.X, head.Y + 1);
                if (IsSafeMove(nextPos, state, allSegments))
                {
                    var priority = target.Y - head.Y;
                    candidates.Add(("Down", nextPos, priority));
                }
            }
            if (target.Y < head.Y && currentDir != Direction.Down)
            {
                var nextPos = new Pos(head.X, head.Y - 1);
                if (IsSafeMove(nextPos, state, allSegments))
                {
                    var priority = head.Y - target.Y;
                    candidates.Add(("Up", nextPos, priority));
                }
            }

            // 选择优先级最高的安全方向
            if (candidates.Count > 0)
            {
                var best = candidates.OrderByDescending(c => c.priority).First();
                return best.dir;
            }
        }

        // 如果没有安全的方向朝向食物，尝试保持当前方向或选择任意安全方向
        var safeDirections = new List<string>();
        var directions = new[] { "Up", "Down", "Left", "Right" };
        
        foreach (var dir in directions)
        {
            if (IsOpposite(currentDir, dir)) continue;
            
            var nextPos = dir switch
            {
                "Up" => new Pos(head.X, head.Y - 1),
                "Down" => new Pos(head.X, head.Y + 1),
                "Left" => new Pos(head.X - 1, head.Y),
                "Right" => new Pos(head.X + 1, head.Y),
                _ => head
            };

            if (IsSafeMove(nextPos, state, allSegments))
            {
                safeDirections.Add(dir);
            }
        }

        if (safeDirections.Count > 0)
        {
            // 优先保持当前方向
            var currentDirStr = currentDir.ToString();
            if (safeDirections.Contains(currentDirStr))
            {
                return currentDirStr;
            }
            return safeDirections[Random.Shared.Next(safeDirections.Count)];
        }

        // 如果所有方向都不安全，至少避免反向
        var oppositeDir = GetOppositeDirection(currentDir);
        var allDirs = directions.Where(d => d != oppositeDir).ToList();
        return allDirs.Count > 0 ? allDirs[Random.Shared.Next(allDirs.Count)] : null;
    }

    private bool IsSafeMove(Pos pos, ServerState state, HashSet<Pos> obstacles)
    {
        // 检查边界
        if (pos.X < 0 || pos.X >= state.Width || pos.Y < 0 || pos.Y >= state.Height)
            return false;

        // 检查是否撞到障碍物（不包括尾部，因为尾部会移动）
        return !obstacles.Contains(pos);
    }

    private bool IsOpposite(Direction current, string dir)
    {
        return (current, dir) switch
        {
            (Direction.Up, "Down") => true,
            (Direction.Down, "Up") => true,
            (Direction.Left, "Right") => true,
            (Direction.Right, "Left") => true,
            _ => false
        };
    }

    private string GetOppositeDirection(Direction dir)
    {
        return dir switch
        {
            Direction.Up => "Down",
            Direction.Down => "Up",
            Direction.Left => "Right",
            Direction.Right => "Left",
            _ => "Up"
        };
    }
}

