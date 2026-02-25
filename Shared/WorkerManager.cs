using System;
using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;

namespace Shared;

public enum WorkerAssignment
{
    Mining,
    Building,
    Scouting,
    Repairing,
    Idle,
    Defending
}

public class WorkerAssignmentInfo
{
    public WorkerAssignment Assignment { get; set; }
    public Unit? Target { get; set; }  // Target mineral, building to repair, etc.
    public UnitType? BuildingType { get; set; }  // For construction assignments
    public Position? BuildLocation { get; set; }  // For construction assignments
    public DateTime AssignedTime { get; set; }
    
    public WorkerAssignmentInfo(WorkerAssignment assignment, Unit? target = null, 
        UnitType? buildingType = null, Position? buildLocation = null)
    {
        Assignment = assignment;
        Target = target;
        BuildingType = buildingType;
        BuildLocation = buildLocation;
        AssignedTime = DateTime.Now;
    }
}

public class WorkerManager
{
    private Dictionary<Unit, int>? _mineralAssignments;
    private Dictionary<Unit, WorkerAssignmentInfo> _workerAssignments;
    private Game? _game;
    private Queue<Position> _scoutLocations;

    public WorkerManager(Game game)
    {
        _game = game;
        _workerAssignments = new Dictionary<Unit, WorkerAssignmentInfo>();
        _scoutLocations = new Queue<Position>();
        Initialize();
    }

    public void Initialize()
    {
        if (_game == null) return;
        
        // Initialize mineral tracking - count how many workers are assigned to each mineral patch
        _mineralAssignments = _game.GetMinerals()
            .Where(m => m.IsVisible())
            .ToDictionary(m => m, m => 0);
            
        // Initialize scout locations (you can customize these based on map)
        InitializeScoutLocations();
        
        // Clear existing assignments
        _workerAssignments.Clear();
    }
    
    private void InitializeScoutLocations()
    {
        if (_game == null) return;
        
        // Add some basic scout locations - enemy start locations, expansions, etc.
        var mapWidth = _game.MapWidth() * 32; // Convert from build tiles to pixels
        var mapHeight = _game.MapHeight() * 32;
        
        // Add corners and center of map as initial scout locations
        _scoutLocations.Enqueue(new Position(mapWidth / 4, mapHeight / 4));
        _scoutLocations.Enqueue(new Position(3 * mapWidth / 4, mapHeight / 4));
        _scoutLocations.Enqueue(new Position(mapWidth / 4, 3 * mapHeight / 4));
        _scoutLocations.Enqueue(new Position(3 * mapWidth / 4, 3 * mapHeight / 4));
        _scoutLocations.Enqueue(new Position(mapWidth / 2, mapHeight / 2));
    }

    public void ManageWorkers()
    {
        if (_game == null || _mineralAssignments == null) return;

        // Update worker assignments based on current state
        UpdateWorkerAssignments();
        
        // Get truly idle workers (not assigned to any task)
        var idleWorkers = GetUnassignedWorkers();
        
        foreach (var worker in idleWorkers)
        {
            AssignWorkerToMining(worker);
        }
    }
    
    private void UpdateWorkerAssignments()
    {
        if (_game == null) return;
        
        // Clean up assignments for workers that no longer exist or completed tasks
        var existingWorkers = _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Probe)
            .ToHashSet();
            
        var workersToRemove = _workerAssignments.Keys
            .Where(w => !existingWorkers.Contains(w))
            .ToList();
            
        foreach (var worker in workersToRemove)
        {
            RemoveWorkerAssignment(worker);
        }
        
        // Update assignments based on current worker state
        foreach (var worker in existingWorkers)
        {
            if (_workerAssignments.ContainsKey(worker))
            {
                var assignment = _workerAssignments[worker];
                
                // Check if worker completed building task
                if (assignment.Assignment == WorkerAssignment.Building && !worker.IsConstructing())
                {
                    RemoveWorkerAssignment(worker);
                }
                // Check if scout reached destination
                else if (assignment.Assignment == WorkerAssignment.Scouting && 
                         assignment.BuildLocation.HasValue && 
                         worker.GetDistance(assignment.BuildLocation.Value) < 100)
                {
                    // Scout reached location, send to next location or back to mining
                    RemoveWorkerAssignment(worker);
                }
                // Check if miner is actually idle (stopped mining)
                else if (assignment.Assignment == WorkerAssignment.Mining && worker.IsIdle())
                {
                    // Worker stopped mining, remove assignment so it can be reassigned
                    RemoveWorkerAssignment(worker);
                }
            }
        }
    }

    private IEnumerable<Unit> GetUnassignedWorkers()
    {
        if (_game == null) return Enumerable.Empty<Unit>();
        
        return _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Probe)
            .Where(u => u.IsIdle() && !_workerAssignments.ContainsKey(u));
    }

    private IEnumerable<Unit> GetIdleWorkers()
    {
        if (_game == null) return Enumerable.Empty<Unit>();
        
        return _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Probe)
            .Where(u => u.IsIdle());
    }

    private void AssignWorkerToMining(Unit worker)
    {
        if (_mineralAssignments == null) return;

        // First, try to find minerals with no workers assigned
        var availableMineral = _mineralAssignments
            .OrderBy(m => m.Key.GetDistance(worker))
            .Where(m => m.Value == 0)
            .Select(m => m.Key)
            .FirstOrDefault();

        if (availableMineral != null)
        {
            worker.Gather(availableMineral);
            _mineralAssignments[availableMineral]++;
            _workerAssignments[worker] = new WorkerAssignmentInfo(WorkerAssignment.Mining, availableMineral);
        }
        else
        {
            // If no completely free minerals, assign to minerals with only 1 worker
            var lessLoadedMineral = _mineralAssignments
                .OrderBy(m => m.Key.GetDistance(worker))
                .Where(m => m.Value == 1)
                .Select(m => m.Key)
                .FirstOrDefault();

            if (lessLoadedMineral != null)
            {
                worker.Gather(lessLoadedMineral);
                _mineralAssignments[lessLoadedMineral]++;
                _workerAssignments[worker] = new WorkerAssignmentInfo(WorkerAssignment.Mining, lessLoadedMineral);
            }
        }
    }

    public bool AssignWorkerToBuilding(Unit worker, UnitType buildingType, Position buildLocation)
    {
        if (worker == null) return false;
        
        // Remove from previous assignment
        RemoveWorkerAssignment(worker);
        
        // Command worker to build the specified building at the given location
        // Convert Position to TilePosition for the build command
        var tilePos = new TilePosition(buildLocation.X / 32, buildLocation.Y / 32);
        if (worker.Build(buildingType, tilePos))
        {
            _workerAssignments[worker] = new WorkerAssignmentInfo(
                WorkerAssignment.Building, 
                null, 
                buildingType, 
                buildLocation);
            return true;
        }
        return false;
    }
    
    public bool AssignWorkerToScouting(Unit? worker = null, Position? targetLocation = null)
    {
        // If no worker specified, find an available one
        if (worker == null)
        {
            worker = GetAvailableWorkerForReassignment();
            if (worker == null) return false;
        }
        
        // If no target specified, get next scout location
        if (targetLocation == null)
        {
            if (_scoutLocations.Count == 0) return false;
            targetLocation = _scoutLocations.Dequeue();
            _scoutLocations.Enqueue(targetLocation.Value); // Re-queue for future scouting
        }
        
        // Remove from previous assignment
        RemoveWorkerAssignment(worker);
        
        // Send worker to scout location
        worker.Move(targetLocation.Value);
        _workerAssignments[worker] = new WorkerAssignmentInfo(
            WorkerAssignment.Scouting, 
            null, 
            null, 
            targetLocation.Value);
        
        return true;
    }
    
    private Unit? GetAvailableWorkerForReassignment()
    {
        if (_game == null) return null;
        
        // Prefer idle workers first
        var idleWorker = GetUnassignedWorkers().FirstOrDefault();
        if (idleWorker != null) return idleWorker;
        
        // If no idle workers, take a miner (but not builders or scouts)
        var minerWorker = _workerAssignments
            .Where(kv => kv.Value.Assignment == WorkerAssignment.Mining)
            .Select(kv => kv.Key)
            .FirstOrDefault();
            
        return minerWorker;
    }
    
    public bool AssignWorkerToRepair(Unit worker, Unit targetUnit)
    {
        if (worker == null || targetUnit == null) return false;
        
        // Remove from previous assignment
        RemoveWorkerAssignment(worker);
        
        worker.Repair(targetUnit);
        _workerAssignments[worker] = new WorkerAssignmentInfo(WorkerAssignment.Repairing, targetUnit);
        return true;
    }
    
    private void RemoveWorkerAssignment(Unit worker)
    {
        if (!_workerAssignments.ContainsKey(worker)) return;
        
        var assignment = _workerAssignments[worker];
        
        // If worker was mining, decrease mineral assignment count
        if (assignment.Assignment == WorkerAssignment.Mining && 
            assignment.Target != null && 
            _mineralAssignments != null &&
            _mineralAssignments.ContainsKey(assignment.Target))
        {
            _mineralAssignments[assignment.Target] = Math.Max(0, _mineralAssignments[assignment.Target] - 1);
        }
        
        _workerAssignments.Remove(worker);
    }


    public int GetWorkerCount()
    {
        if (_game == null) return 0;
        
        return _game.Self().GetUnits()
            .Count(u => u.GetUnitType() == UnitType.Protoss_Probe);
    }

    public int GetIdleWorkerCount()
    {
        if (_game == null) return 0;
        
        // Count workers that are truly idle (game says they're idle AND not assigned to active tasks)
        return _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Probe)
            .Count(u => u.IsIdle() && (!_workerAssignments.ContainsKey(u) || 
                                      _workerAssignments[u].Assignment != WorkerAssignment.Building));
    }
    
    public int GetAssignedWorkerCount(WorkerAssignment assignmentType)
    {
        return _workerAssignments.Count(kv => kv.Value.Assignment == assignmentType);
    }
    
    public IEnumerable<Unit> GetWorkersWithAssignment(WorkerAssignment assignmentType)
    {
        return _workerAssignments
            .Where(kv => kv.Value.Assignment == assignmentType)
            .Select(kv => kv.Key);
    }
    
    public WorkerAssignmentInfo? GetWorkerAssignment(Unit worker)
    {
        return _workerAssignments.ContainsKey(worker) ? _workerAssignments[worker] : null;
    }

    public void OnWorkerDestroyed(Unit worker)
    {
        // Use the new assignment tracking system
        RemoveWorkerAssignment(worker);
    }

    public Dictionary<Unit, int>? GetMineralAssignments()
    {
        return _mineralAssignments;
    }
}