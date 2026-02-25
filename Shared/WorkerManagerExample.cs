using System;
using BWAPI.NET;
using Shared;

namespace Shared;

// Example usage of the enhanced WorkerManager
public static class WorkerManagerExample
{
    public static void DemonstrateWorkerManager(WorkerManager workerManager, Game game)
    {
        // Get statistics about worker assignments
        var totalWorkers = workerManager.GetWorkerCount();
        var idleWorkers = workerManager.GetIdleWorkerCount();
        var miners = workerManager.GetAssignedWorkerCount(WorkerAssignment.Mining);
        var builders = workerManager.GetAssignedWorkerCount(WorkerAssignment.Building);
        var scouts = workerManager.GetAssignedWorkerCount(WorkerAssignment.Scouting);
        
        // Display statistics on screen
        game.DrawTextScreen(10, 200, $"Total Workers: {totalWorkers}");
        game.DrawTextScreen(10, 215, $"Mining: {miners}, Building: {builders}");
        game.DrawTextScreen(10, 230, $"Scouting: {scouts}, Idle: {idleWorkers}");
        
        // Example 1: Assign a specific worker to scouting
        var availableWorker = workerManager.GetWorkersWithAssignment(WorkerAssignment.Mining).FirstOrDefault();
        if (availableWorker != null)
        {
            var scoutLocation = new Position(game.MapWidth() * 16, game.MapHeight() * 16); // Map center
            workerManager.AssignWorkerToScouting(availableWorker, scoutLocation);
        }
        
        // Example 2: Assign worker to building
        var builder = workerManager.GetWorkersWithAssignment(WorkerAssignment.Mining).FirstOrDefault();
        if (builder != null)
        {
            var buildLocation = new Position(100, 100); // Example location
            workerManager.AssignWorkerToBuilding(builder, UnitType.Terran_Supply_Depot, buildLocation);
        }
        
        // Example 3: Auto-assign scouts periodically
        if (game.GetFrameCount() % 3000 == 0 && totalWorkers > 6) // Every ~2 minutes if we have enough workers
        {
            workerManager.AssignWorkerToScouting(); // Auto-selects worker and location
        }
        
        // Example 4: Check specific worker assignments
        foreach (var worker in game.Self().GetUnits().Where(u => u.GetUnitType() == UnitType.Terran_SCV))
        {
            var assignment = workerManager.GetWorkerAssignment(worker);
            if (assignment != null)
            {
                // You can act based on what the worker is assigned to do
                switch (assignment.Assignment)
                {
                    case WorkerAssignment.Scouting:
                        // Worker is scouting - maybe check if it found enemies
                        break;
                    case WorkerAssignment.Building:
                        // Worker is building - maybe check if construction is complete
                        break;
                    case WorkerAssignment.Mining:
                        // Worker is mining - maybe check if mineral is depleted
                        break;
                }
            }
        }
    }
}