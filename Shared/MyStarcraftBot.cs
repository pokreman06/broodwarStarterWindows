using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BWAPI.NET;

namespace Shared;

// library from https://www.nuget.org/packages/BWAPI.NET

public class ProtossBot : DefaultBWListener
{
    private BWClient? _bwClient = null;
    public Game? Game => _bwClient?.Game;
    
    // AI and build management
    private PlacementManager? _placementManager;
    private BuildingRoleManager? _roleManager;
    private IAIController? _aiController;
    private BuildQueueManager? _queueManager;
    private WorkerManager? _workerManager;
    private BaseManager? _baseManager;

    public bool IsRunning { get; private set; } = false;
    public bool InGame { get; private set; } = false;
    public int? GameSpeedToSet { get; set; } = null;



    public event Action? StatusChanged;

    public void Connect()
    {
        _bwClient = new BWClient(this);
        IsRunning = true;
        _bwClient.StartGame();
    }


    // Bot Callbacks below
    public override void OnStart()
    {
        InGame = true;
        Game?.EnableFlag(Flag.UserInput); // let human control too
        
        // Initialize AI systems
        if (Game != null)
        {
            _roleManager = new BuildingRoleManager();
            _placementManager = new PlacementManager(Game, _roleManager);
            _aiController = new DefaultAIController(_placementManager);
            _baseManager = new BaseManager();
            _queueManager = new BuildQueueManager(_aiController, _placementManager, _baseManager);
            _workerManager = new WorkerManager(Game);
            
            // Add some initial build order items for Protoss
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Probe, false));
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Pylon, false));
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Probe, false));
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Gateway, false));
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Probe, false));
            _queueManager.AddToBuildQueue(new BuildOrderItem(UnitType.Protoss_Nexus, false)); // Test expansion
        }
    }

    public override void OnEnd(bool isWinner)
    {
        InGame = false;
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
        Game.DrawTextScreen(100, 100, "Hello Protoss Bot!");
        
        // Process AI build queue
        if (_queueManager != null)
        {
            _queueManager.UpdateGameState(Game);
            _queueManager.ProcessBuildQueue();
            
            // Display current build queue
            var buildOrder = _queueManager.GetCurrentBuildOrder();
            for (int i = 0; i < Math.Min(buildOrder.Count, 5); i++)
            {
                var item = buildOrder[i];
                var text = $"{i + 1}. {item.UnitType}" + (item.IsFixed ? " (Fixed)" : "");
                Game.DrawTextScreen(10, 120 + (i * 15), text);
            }
        }
        
        // Manage workers
        _workerManager?.ManageWorkers();
        
        // Display enhanced worker statistics
        var workerCount = _workerManager?.GetWorkerCount() ?? 0;
        var idleWorkerCount = _workerManager?.GetIdleWorkerCount() ?? 0;
        var scoutCount = _workerManager?.GetAssignedWorkerCount(WorkerAssignment.Scouting) ?? 0;
        var builderCount = _workerManager?.GetAssignedWorkerCount(WorkerAssignment.Building) ?? 0;
        var minerCount = _workerManager?.GetAssignedWorkerCount(WorkerAssignment.Mining) ?? 0;
        
        Game.DrawTextScreen(10, 200, $"Probes: {workerCount} (Idle: {idleWorkerCount})");
        Game.DrawTextScreen(10, 215, $"Mining: {minerCount}, Building: {builderCount}, Scouting: {scoutCount}");
        
        // Display resource reservation info
        var (reservedMinerals, reservedGas) = _queueManager?.GetReservedResources() ?? (0, 0);
        var reservationCount = _queueManager?.GetActiveReservationCount() ?? 0;
        
        // Calculate and display available resources
        var (availableMinerals, availableGas, totalMinerals, totalGas) = _queueManager?.GetAvailableResources() ?? (0, 0, 0, 0);
        
        Game.DrawTextScreen(10, 290, $"Resources: {totalMinerals}M/{totalGas}G (Available: {availableMinerals}M/{availableGas}G)");
        if (reservationCount > 0)
        {
            Game.DrawTextScreen(10, 305, $"Reserved: {reservedMinerals}M, {reservedGas}G ({reservationCount} builds)");
        }
        
        // Display base expansion info
        var allBases = _baseManager?.GetAllBases() ?? new List<BaseLocation>().AsReadOnly();
        var expansions = _baseManager?.GetPotentialExpansions() ?? new List<BaseLocation>().AsReadOnly();
        
        Game.DrawTextScreen(10, 335, $"Bases: {allBases.Count} (Expansions available: {expansions.Count})");
        if (expansions.Any())
        {
            var bestExpansion = expansions.FirstOrDefault();
            if (bestExpansion != null)
            {
                Game.DrawTextScreen(10, 350, $"Next expansion: ({bestExpansion.Location.X}, {bestExpansion.Location.Y}) - {bestExpansion.MineralCount}M, {bestExpansion.GeyserCount}G");
            }
        }
        
        // Auto-scout every 2000 frames (roughly every 1.5 minutes) if we have enough workers
        if (Game.GetFrameCount() % 2000 == 0 && workerCount > 4)
        {
            _workerManager?.AssignWorkerToScouting();
            Game.DrawTextScreen(10, 230, "Scout sent!");
        }
        
        // Example: Assign worker to build when we have resources and no builders
        if (Game.GetFrameCount() % 1500 == 500 && // Offset timing to avoid conflicts
            workerCount >= 6 && 
            Game.Self().Minerals() >= 100 &&
            builderCount == 0) // Only if no one is currently building
        {
            var availableWorker = _workerManager?.GetWorkersWithAssignment(WorkerAssignment.Mining).FirstOrDefault();
            if (availableWorker != null)
            {
                // Simple build location near the worker
                var buildPos = new Position(availableWorker.GetPosition().X + 100, availableWorker.GetPosition().Y + 100);
                if (_workerManager?.AssignWorkerToBuilding(availableWorker, UnitType.Protoss_Pylon, buildPos) == true)
                {
                    Game.DrawTextScreen(10, 245, "Worker assigned to build Pylon!");
                }
            }
        }
    }

    public override void OnUnitComplete(Unit unit) 
    {
        // Release any resource reservations for completed units/buildings
        _queueManager?.ReleaseResourceReservation(unit);
        
        // When a new probe is completed, it will automatically be managed by WorkerManager
        if (unit.GetUnitType() == UnitType.Protoss_Probe)
        {
            Game?.DrawTextScreen(10, 260, "New Probe completed!");
        }
        
        // When buildings complete, workers assigned to them become available
        if (unit.GetUnitType() == UnitType.Protoss_Pylon ||
            unit.GetUnitType() == UnitType.Protoss_Gateway ||
            unit.GetUnitType() == UnitType.Protoss_Nexus ||
            unit.GetUnitType() == UnitType.Protoss_Assimilator)
        {
            Game?.DrawTextScreen(10, 275, $"{unit.GetUnitType()} completed!");
            
            // If it's a pylon or nexus, the power field tracking will be updated next frame
            if (unit.GetUnitType() == UnitType.Protoss_Pylon)
            {
                Game?.DrawTextScreen(10, 305, "New power field available!");
            }
            else if (unit.GetUnitType() == UnitType.Protoss_Assimilator)
            {
                Game?.DrawTextScreen(10, 305, "Gas mining operational!");
            }
        }
    }

    public override void OnSendText(string text) { }

    public override void OnReceiveText(Player player, string text) { }


    public override void OnNukeDetect(Position target) { }

    public override void OnUnitEvade(Unit unit) { }


    public override void OnUnitHide(Unit unit) { }

    public override void OnUnitCreate(Unit unit) 
    {
        // Notify build queue manager that a unit started construction
        if (unit.GetPlayer() == Game?.Self())
        {
            _queueManager?.OnUnitStartedConstruction(unit);
            
            // Log construction start
            Console.WriteLine($"DEBUG: {unit.GetUnitType()} construction started at ({unit.GetTilePosition().X}, {unit.GetTilePosition().Y})");
            
            var unitType = unit.GetUnitType();
            
            if (unitType.IsWorker())
            {
                Game?.DrawTextScreen(10, 320, $"{unitType} training started!");
            }
            else if (unitType.IsBuilding())
            {
                Game?.DrawTextScreen(10, 320, $"{unitType} construction started!");
            }
            else if (!unitType.IsWorker() && !unitType.IsBuilding())
            {
                // Combat units or other non-worker, non-building units
                Game?.DrawTextScreen(10, 320, $"{unitType} training started!");
            }
        }
    }

    public override void OnUnitRenegade(Unit unit) { }

    public override void OnSaveGame(string gameName) { }

    public override void OnUnitDestroy(Unit unit) 
    {
        // Handle worker destruction for Protoss
        if (unit.GetUnitType() == UnitType.Protoss_Probe)
        {
            _workerManager?.OnWorkerDestroyed(unit);
        }
    }
    
    // Public methods for build order management
    public void AddToBuildQueue(BuildOrderItem item)
    {
        _queueManager?.AddToBuildQueue(item);
    }
    
    public void ClearBuildQueue()
    {
        _queueManager?.ClearBuildQueue();
    }
    
    public IReadOnlyList<BuildOrderItem>? GetCurrentBuildOrder()
    {
        return _queueManager?.GetCurrentBuildOrder();
    }
    
    public override void OnPlayerLeft(Player player) { }
    public override void OnUnitShow(Unit unit) { }
    public override void OnUnitDiscover(Unit unit) { }
}
