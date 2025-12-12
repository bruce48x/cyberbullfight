using System.Text;

// Minimal dependency console Snake game controlled with WASD.
var width = 32;
var height = 18;
var frameTime = TimeSpan.FromMilliseconds(120);

Console.CursorVisible = false;
Console.Clear();

var rng = new Random();
var snake = new LinkedList<Pos>();
Direction direction = Direction.Right;
Direction pendingDirection = direction;

// Seed the snake near the center.
var start = new Pos(width / 2, height / 2);
snake.AddFirst(start);
snake.AddLast(start with { X = start.X - 1 });
snake.AddLast(start with { X = start.X - 2 });

Pos food = SpawnFood();
var score = 0;
var gameOver = false;

while (!gameOver)
{
    var frameStart = DateTime.UtcNow;
    ReadInput();

    var nextHead = NextHead();
    if (IsCollision(nextHead))
    {
        gameOver = true;
        break;
    }

    var ateFood = nextHead.Equals(food);
    snake.AddFirst(nextHead);
    if (ateFood)
    {
        score += 1;
        food = SpawnFood();
    }
    else
    {
        snake.RemoveLast();
    }

    Draw(ateFood);
    SleepRemaining(frameStart);
}

ShowGameOver();
return;

// --- Local functions ---

void ReadInput()
{
    while (Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true).Key;
        pendingDirection = key switch
        {
            ConsoleKey.W => Direction.Up,
            ConsoleKey.A => Direction.Left,
            ConsoleKey.S => Direction.Down,
            ConsoleKey.D => Direction.Right,
            _ => pendingDirection
        };
    }

    // Prevent reversing into the body.
    if (!IsOpposite(direction, pendingDirection))
    {
        direction = pendingDirection;
    }
}

Pos NextHead()
{
    var head = snake.First!.Value;
    return direction switch
    {
        Direction.Up => head with { Y = head.Y - 1 },
        Direction.Down => head with { Y = head.Y + 1 },
        Direction.Left => head with { X = head.X - 1 },
        Direction.Right => head with { X = head.X + 1 },
        _ => head
    };
}

bool IsCollision(Pos pos) =>
    pos.X < 0 || pos.X >= width ||
    pos.Y < 0 || pos.Y >= height ||
    snake.Any(segment => segment.Equals(pos));

Pos SpawnFood()
{
    while (true)
    {
        var candidate = new Pos(rng.Next(0, width), rng.Next(0, height));
        if (!snake.Any(s => s.Equals(candidate)))
        {
            return candidate;
        }
    }
}

void Draw(bool ateFood)
{
    var sb = new StringBuilder();

    // Top border.
    sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();

    for (var y = 0; y < height; y++)
    {
        sb.Append('|');
        for (var x = 0; x < width; x++)
        {
            var pos = new Pos(x, y);
            if (snake.First!.Value.Equals(pos))
            {
                sb.Append('O');
            }
            else if (snake.Any(s => s.Equals(pos)))
            {
                sb.Append('o');
            }
            else if (food.Equals(pos))
            {
                sb.Append('@');
            }
            else
            {
                sb.Append(ateFood ? '.' : ' ');
            }
        }
        sb.Append('|').AppendLine();
    }

    // Bottom border.
    sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();
    sb.Append($"Score: {score}   Controls: WASD to move, Ctrl+C to quit");

    Console.SetCursorPosition(0, 0);
    Console.Write(sb.ToString());
}

void SleepRemaining(DateTime frameStart)
{
    var elapsed = DateTime.UtcNow - frameStart;
    var remaining = frameTime - elapsed;
    if (remaining > TimeSpan.Zero)
    {
        Thread.Sleep(remaining);
    }
}

void ShowGameOver()
{
    Console.SetCursorPosition(0, height + 2);
    Console.WriteLine();
    Console.WriteLine($"Game over! Final score: {score}");
    Console.WriteLine("Press any key to exit.");
    Console.ReadKey(intercept: true);
}

bool IsOpposite(Direction current, Direction next) =>
    (current, next) switch
    {
        (Direction.Up, Direction.Down) => true,
        (Direction.Down, Direction.Up) => true,
        (Direction.Left, Direction.Right) => true,
        (Direction.Right, Direction.Left) => true,
        _ => false
    };

// --- Helpers ---

record struct Pos(int X, int Y);

enum Direction
{
    Up,
    Down,
    Left,
    Right
}

