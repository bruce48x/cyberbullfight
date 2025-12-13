using SnakeGame.Client.Models;

namespace SnakeGame.Client;

class GameStateTracker
{
    private int _lastScore = 0;
    private bool _wasAlive = false;
    private bool _gameStarted = false;
    private readonly string _logPrefix;
    private readonly int _myId;

    public GameStateTracker(int myId, string logPrefix)
    {
        _myId = myId;
        _logPrefix = logPrefix;
    }

    public void ProcessGameState(ServerState state)
    {
        var myPlayer = state.Players.FirstOrDefault(p => p.Id == _myId);
        
        if (myPlayer == null) return;

        // 检测玩家从死亡状态复活（新游戏开始）
        // 如果玩家之前死亡了，现在又复活了，说明新游戏开始了
        if (!_wasAlive && myPlayer.Alive)
        {
            _gameStarted = true;
            Console.WriteLine($"{_logPrefix} 游戏开始 - Player ID: {_myId}, Name: {myPlayer.Name}");
            _wasAlive = true;
            _lastScore = 0; // 新游戏开始时，重置分数为0
        }
        // 检测首次游戏开始（玩家从未死亡过）
        else if (!_gameStarted && myPlayer.Alive)
        {
            _gameStarted = true;
            Console.WriteLine($"{_logPrefix} 游戏开始 - Player ID: {_myId}, Name: {myPlayer.Name}");
            _wasAlive = true;
            _lastScore = 0; // 新游戏开始时，重置分数为0
        }

        // 检测吃食（分数增加）
        if (myPlayer.Alive && myPlayer.Score > _lastScore)
        {
            Console.WriteLine($"{_logPrefix} 吃食 - Score: {_lastScore} -> {myPlayer.Score}");
            _lastScore = myPlayer.Score;
        }

        // 检测死亡
        if (_wasAlive && !myPlayer.Alive)
        {
            Console.WriteLine($"{_logPrefix} 死亡 - Final Score: {myPlayer.Score}");
            _wasAlive = false;
        }

        // 检测游戏结束（所有玩家都死亡，或者只剩一个玩家存活获胜）
        var aliveCount = state.Players.Count(p => p.Alive);
        if (_gameStarted && state.Players.Count > 0)
        {
            if (aliveCount == 0)
            {
                // 所有玩家都死亡
                Console.WriteLine($"{_logPrefix} 游戏结束 - All players dead");
                _gameStarted = false;
                _lastScore = 0; // 重置分数，为下一局游戏做准备
            }
            else if (aliveCount == 1)
            {
                // 只剩一个玩家存活，该玩家获胜
                var winner = state.Players.First(p => p.Alive);
                if (winner.Id == _myId)
                {
                    Console.WriteLine($"{_logPrefix} 游戏结束 - 获胜! Final Score: {myPlayer.Score}");
                }
                else
                {
                    Console.WriteLine($"{_logPrefix} 游戏结束 - Player {winner.Id} ({winner.Name}) 获胜");
                }
                _gameStarted = false;
                _lastScore = 0; // 重置分数，为下一局游戏做准备（包括获胜玩家）
            }
        }
    }

    public bool IsGameStarted => _gameStarted;
}

