using System;
using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;
using Shared;
public class PlacementManager
{
    private Game _game;
    private BuildingRoleManager _roleManager;
    private PlacementEvaluator _evaluator;

    public PlacementManager(Game game)
    {
        _game = game;
        _roleManager = new BuildingRoleManager();
        _evaluator = new PlacementEvaluator(game, _roleManager);
    }
    // Allow injection of a pre-configured role manager
    public PlacementManager(Game game, BuildingRoleManager roleManager)
    {
        _game = game;
        _roleManager = roleManager ?? new BuildingRoleManager();
        _evaluator = new PlacementEvaluator(game, _roleManager);
    }
    public PlacementEvaluator Evaluator => _evaluator;

    public bool IsTilePlaceable(int x, int y, UnitType unitType)
    {
        return _game.CanBuildHere(new TilePosition(x,y), unitType, null);
    }
    public TilePosition BuildQueue(UnitType unitType, Func<TileRange, int>? selector = null)
    {
        // Try role-based ranges first
        var roles = BuildingPlacementRoles.GetRoles(unitType);
        foreach (var role in roles)
        {
            // Try to find any tile in configured ranges for this role and that passes evaluators.
            var ranges = _roleManager.GetRanges(role);
            foreach (var range in ranges)
            {
                // optional ordering can be applied by caller changing ranges in the role manager
                for (int x = range.Min.X; x <= range.Max.X; x++)
                for (int y = range.Min.Y; y <= range.Max.Y; y++)
                {
                    if (!IsTilePlaceable(x, y, unitType)) continue;
                    if (!_evaluator.EvaluatePlacement(unitType, x, y)) continue;
                    return new TilePosition(x, y);
                }
            }
        }

        // If no role ranges matched, fall back to a general placement strategy.
        var fallback = FindPlacementGeneral(unitType);
        if (fallback != TilePosition.Invalid) return fallback;

        // Generic fallback: full-map scan (limited bounds)
        for (int x = 0; x <= _game.MapWidth() - 4; x++)
        {
            for (int y = 0; y <= _game.MapHeight() - 3; y++)
            {
                if (IsTilePlaceable(x, y, unitType))
                {
                    return new TilePosition(x, y);
                }
            }
        }
        return TilePosition.Invalid;
    }
    // General placement strategy: spiral outward from our start location (command center).
    // Approach:
    // 1. Find our start tile (first Terran Command Center).
    // 2. Spiral outward from start location looking for placeable spots.
    // 3. Fallback to full map scan if no start found.
    TilePosition FindPlacementGeneral(UnitType unitType)
    {
        // helper: find our first command center tile
        TilePosition GetMyStartTile()
        {
            var me = _game.Self();
            if (me == null) return TilePosition.Invalid;
            var cc = me.GetUnits().FirstOrDefault(u => u.GetUnitType() == UnitType.Terran_Command_Center);
            if (cc == null) return TilePosition.Invalid;
            return cc.GetTilePosition();
        }

        TilePosition start = GetMyStartTile();

        // If we don't have a start, fallback to full map scan
        if (start == TilePosition.Invalid)
        {
            for (int x = 0; x <= _game.MapWidth() - 4; x++)
            {
                for (int y = 0; y <= _game.MapHeight() - 3; y++)
                {
                    if (IsTilePlaceable(x, y, unitType))
                    {
                        return new TilePosition(x, y);
                    }
                }
            }
            return TilePosition.Invalid;
        }

        // Spiral outward from start location
        int sx = start.X;
        int sy = start.Y;
        int maxRadius = 20;
        
        for (int r = 0; r <= maxRadius; r++)
        {
            for (int ox = -r; ox <= r; ox++)
            {
                for (int oy = -r; oy <= r; oy++)
                {
                    if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != r) continue;
                    int nx = sx + ox;
                    int ny = sy + oy;
                    if (nx < 0 || ny < 0 || nx > _game.MapWidth() - 4 || ny > _game.MapHeight() - 3) continue;
                    if (IsTilePlaceable(nx, ny, unitType))
                    {
                        return new TilePosition(nx, ny);
                    }
                }
            }
        }

        return TilePosition.Invalid;
    }
}