using BWAPI.NET;

namespace Shared;

// library from https://www.nuget.org/packages/BWAPI.NET

public class MyStarcraftBot : DefaultBWListener
{
    private BWClient? _bwClient = null;
    public Game? Game => _bwClient?.Game;

    public bool IsRunning { get; private set; } = false;
    public bool InGame { get; private set; } = false;
    public int? GameSpeedToSet { get; set; } = null;
    private bool _shouldDisconnect = false;
    private TaskCompletionSource<bool>? _disconnectTcs = null;

    public event Action? StatusChanged;

    public void Connect()
    {
        _bwClient = new BWClient(this);
        IsRunning = true;
        StatusChanged?.Invoke();
        _bwClient.StartGame();
        Console.WriteLine("done with game");
    }

    public async Task Disconnect()
    {
        if (!IsRunning && !InGame)
            return;

        _disconnectTcs = new TaskCompletionSource<bool>();
        _shouldDisconnect = true;
        await _disconnectTcs.Task;
    }

    // Bot Callbacks below
    public override void OnStart()
    {
        InGame = true;
        StatusChanged?.Invoke();
        Game?.EnableFlag(Flag.UserInput); // let human control too
    }

    public override void OnEnd(bool isWinner)
    {
        InGame = false;
        IsRunning = false;
        _bwClient = null;
        _shouldDisconnect = false;
        StatusChanged?.Invoke();
        _disconnectTcs?.TrySetResult(true);
        _disconnectTcs = null;
    }

    public override void OnFrame()
    {
        if (Game == null)
            return;

        if (_shouldDisconnect)
        {
            Console.WriteLine("Leaving Game");
            Game.LeaveGame();
            return;
        }

        if (GameSpeedToSet != null)
        {
            Game.SetLocalSpeed(GameSpeedToSet.Value);
            GameSpeedToSet = null;
        }
        Game.DrawTextScreen(100, 100, "Hello Bot!");
    }

    public override void OnUnitComplete(Unit unit) { }

    public override void OnUnitDestroy(Unit unit) { }

    public override void OnUnitMorph(Unit unit) { }

    public override void OnSendText(string text) { }

    public override void OnReceiveText(Player player, string text) { }

    public override void OnPlayerLeft(Player player) { }

    public override void OnNukeDetect(Position target) { }

    public override void OnUnitEvade(Unit unit) { }

    public override void OnUnitShow(Unit unit) { }

    public override void OnUnitHide(Unit unit) { }

    public override void OnUnitCreate(Unit unit) { }

    public override void OnUnitRenegade(Unit unit) { }

    public override void OnSaveGame(string gameName) { }

    public override void OnUnitDiscover(Unit unit) { }
}
