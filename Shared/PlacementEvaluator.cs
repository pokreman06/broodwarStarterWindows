using System;
using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;

namespace Shared;

// Evaluates placement decisions holistically across the entire base/system
public class PlacementEvaluator
{
    private Game _game;
    private BuildingRoleManager _roleManager;

    public PlacementEvaluator(Game game, BuildingRoleManager roleManager)
    {
        _game = game;
        _roleManager = roleManager;
    }

    // Evaluate a placement candidate considering the entire system state.
    // Returns true if the placement passes all priority checks.
    public bool EvaluatePlacement(UnitType unitType, int x, int y)
    {
        if (!PreserveWinCondition(unitType, x, y)) return false;
        if (!BlockEnemyPaths(unitType, x, y)) return false;
        if (!ProtectWorkersOrTech(unitType, x, y)) return false;
        if (!PreserveFuturePlacements(unitType, x, y)) return false;
        return true;
    }

    // Check if placement preserves the current build's win condition by analyzing tech/production clusters
    private bool PreserveWinCondition(UnitType unitType, int cx, int cy)
    {
        var techTypes = new HashSet<UnitType> {
            UnitType.Terran_Factory,
            UnitType.Terran_Starport,
            UnitType.Terran_Armory,
            UnitType.Terran_Academy
        };

        if (!techTypes.Contains(unitType)) return true;

        // Analyze existing production cluster and ensure tech stays within reasonable distance
        var me = _game.Self();
        if (me == null) return false;
        
        var existingBuildings = me.GetUnits().Where(u => u.GetUnitType().IsBuilding()).ToList();
        var productionTypes = new HashSet<UnitType> { 
            UnitType.Terran_Barracks, UnitType.Terran_Factory, 
            UnitType.Terran_Starport, UnitType.Terran_Command_Center 
        };
        
        // Find centroid of existing production
        var productionBuildings = existingBuildings.Where(u => productionTypes.Contains(u.GetUnitType())).ToList();
        if (!productionBuildings.Any()) return true;
        
        double avgX = productionBuildings.Average(u => u.GetTilePosition().X);
        double avgY = productionBuildings.Average(u => u.GetTilePosition().Y);
        
        // Require tech buildings to be within reasonable distance of production cluster
        double distance = DistanceTiles(cx, cy, avgX, avgY);
        return distance <= 12.0;
    }

    // Analyze if placement contributes to blocking enemy movement through key areas
    private bool BlockEnemyPaths(UnitType unitType, int cx, int cy)
    {
        // Check if placement is in designated blocking zones
        var rampRanges = _roleManager.GetRanges(BuildingRole.RampBlock);
        var chokeRanges = _roleManager.GetRanges(BuildingRole.ChokeDefense);
        
        foreach (var r in rampRanges.Concat(chokeRanges))
        {
            if (r.Contains(new TilePosition(cx, cy))) return true;
        }
        
        // Analyze if placement helps create defensive formations
        var me = _game.Self();
        if (me == null) return true;
        
        var defensiveTypes = new HashSet<UnitType> { 
            UnitType.Terran_Supply_Depot, UnitType.Terran_Bunker 
        };
        
        if (defensiveTypes.Contains(unitType))
        {
            // Check if this placement helps form a wall or defensive line
            var nearbyDefensive = me.GetUnits()
                .Where(u => defensiveTypes.Contains(u.GetUnitType()))
                .Where(u => DistanceTiles(u.GetTilePosition().X, u.GetTilePosition().Y, cx, cy) <= 3.0)
                .ToList();
            
            // Prefer placements that connect to existing defensive structures
            if (nearbyDefensive.Any()) return true;
        }
        
        return true; // Don't strictly require blocking for other buildings
    }

    // Evaluate if placement protects workers, tech, or key infrastructure
    private bool ProtectWorkersOrTech(UnitType unitType, int cx, int cy)
    {
        var me = _game.Self();
        if (me == null) return false;

        var defensiveTypes = new HashSet<UnitType> { 
            UnitType.Terran_Bunker, UnitType.Terran_Missile_Turret, 
            UnitType.Terran_Engineering_Bay, UnitType.Terran_Supply_Depot 
        };
        var techTypes = new HashSet<UnitType> { 
            UnitType.Terran_Factory, UnitType.Terran_Starport, 
            UnitType.Terran_Armory, UnitType.Terran_Academy 
        };

        // Analyze defensive coverage of critical areas
        if (defensiveTypes.Contains(unitType))
        {
            // Check proximity to mineral patches (worker protection)
            var minerals = _game.GetMinerals().Where(m => m.IsVisible()).ToList();
            bool protectsWorkers = minerals.Any(m => 
                DistanceTiles(m.GetTilePosition().X, m.GetTilePosition().Y, cx, cy) <= 10.0);
            
            // Check proximity to command center
            var commandCenters = me.GetUnits().Where(u => u.GetUnitType() == UnitType.Terran_Command_Center).ToList();
            bool protectsCC = commandCenters.Any(cc => 
                DistanceTiles(cc.GetTilePosition().X, cc.GetTilePosition().Y, cx, cy) <= 12.0);
            
            return protectsWorkers || protectsCC;
        }

        // For tech buildings, ensure they're protected by existing production cluster
        if (techTypes.Contains(unitType))
        {
            var productionBuildings = me.GetUnits()
                .Where(u => u.GetUnitType().IsBuilding())
                .Where(u => !techTypes.Contains(u.GetUnitType()))
                .ToList();
            
            return productionBuildings.Any(u => 
                DistanceTiles(u.GetTilePosition().X, u.GetTilePosition().Y, cx, cy) <= 8.0);
        }

        return true;
    }

    // Analyze if placement preserves space for future expansion and building placement
    private bool PreserveFuturePlacements(UnitType unitType, int cx, int cy)
    {
        // Check that placement doesn't create bottlenecks or block future expansion
        int searchRadius = 8;
        int requiredFreeSpaces = 6;
        int freeCount = 0;
        
        // Analyze surrounding area for available building space
        for (int x = Math.Max(0, cx - searchRadius); 
             x <= Math.Min(_game.MapWidth() - 4, cx + searchRadius); x++)
        {
            for (int y = Math.Max(0, cy - searchRadius); 
                 y <= Math.Min(_game.MapHeight() - 3, cy + searchRadius); y++)
            {
                if (x == cx && y == cy) continue; // Skip the candidate position itself
                
                if (_game.CanBuildHere(new TilePosition(x, y), unitType, null))
                {
                    freeCount++;
                    if (freeCount >= requiredFreeSpaces) return true;
                }
            }
        }
        
        // Also check that we're not blocking access to expansion areas
        var expansionRanges = _roleManager.GetRanges(BuildingRole.NatEcon);
        foreach (var range in expansionRanges)
        {
            // Ensure placement doesn't block paths to natural expansion
            if (DistanceTiles(cx, cy, (range.Min.X + range.Max.X) / 2.0, 
                              (range.Min.Y + range.Max.Y) / 2.0) <= 4.0)
            {
                // Too close to expansion area - might block access
                return false;
            }
        }
        
        return freeCount >= requiredFreeSpaces;
    }

    private static double DistanceTiles(double ax, double ay, double bx, double by)
    {
        double dx = ax - bx;
        double dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}