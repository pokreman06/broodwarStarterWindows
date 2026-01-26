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
    Dictionary<Unit, int>? InitialMinerals;



    public event Action? StatusChanged;

    public void Connect()
    {
        _bwClient = new BWClient(this);
        var _ = Task.Run(() => _bwClient.StartGame());
        IsRunning = true;
        StatusChanged?.Invoke();
    }

    public void Disconnect()
    {
        if (_bwClient != null)
        {
            (_bwClient as IDisposable)?.Dispose();
        }
        _bwClient = null;
        IsRunning = false;
        InGame = false;
        StatusChanged?.Invoke();
    }

    // Bot Callbacks below
    public override void OnStart()
    {
        InGame = true;
        StatusChanged?.Invoke();
        Game?.EnableFlag(Flag.UserInput); // let human control too
        InitialMinerals = Game?.GetMinerals().Where(m => m.IsVisible()).ToDictionary(m => m, m => 0);
    }

    public override void OnEnd(bool isWinner)
    {
        InGame = false;
        StatusChanged?.Invoke();
    }

    public override void OnFrame()
    {
        if (Game == null)
            return;
        if (GameSpeedToSet != null)
        {
            Game.SetLocalSpeed(GameSpeedToSet.Value);
            GameSpeedToSet = null;
        }
        Game.DrawTextScreen(100, 100, "Hello Bot!");
        NewMethod();
    }

    private void NewMethod()
    {
        if (Game == null)
            return;
        var idleWorkers = Game.Self().GetUnits().Where(u => u.GetUnitType() == UnitType.Terran_SCV).Where(u => u.IsIdle());
        foreach (var worker in idleWorkers)
        {
            var mineral = InitialMinerals?.OrderBy(m => m.Key.GetDistance(worker)).Where(m => m.Value == 0).Select(m => m.Key).FirstOrDefault<Unit?>();

            if (mineral != null)
            {
                worker.Gather(mineral);
                InitialMinerals![mineral]++;
            }
            else
            {
                mineral = InitialMinerals?.OrderBy(m => m.Key.GetDistance(worker)).Where(m => m.Value == 1).Select(m => m.Key).FirstOrDefault<Unit?>();
            }
        }
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
