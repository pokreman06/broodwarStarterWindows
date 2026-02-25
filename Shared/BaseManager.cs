using System;
using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;

namespace Shared;

// Manages base expansion and resource patch tracking
public class BaseManager
{
    private Game? _game;
    private readonly List<BaseLocation> _knownBases;
    private readonly List<BaseLocation> _potentialExpansions;
    private readonly HashSet<Unit> _trackedMineralPatches;
    
    public BaseManager()
    {
        _knownBases = new List<BaseLocation>();
        _potentialExpansions = new List<BaseLocation>();
        _trackedMineralPatches = new HashSet<Unit>();
    }
    
    public void UpdateGameState(Game game)
    {
        _game = game;
        
        // Update existing bases
        UpdateExistingBases();
        
        // Scan for new potential expansion sites
        ScanForExpansionSites();
        
        // Clean up destroyed bases
        CleanupDestroyedBases();
    }
    
    // Find the best location for a new Nexus
    public TilePosition FindNexusLocation()
    {
        if (_game?.Self() == null) return TilePosition.Invalid;
        
        // First, check our known potential expansions
        var bestExpansion = FindBestExpansion();
        if (bestExpansion != null)
        {
            Console.WriteLine($"DEBUG: Found expansion site at ({bestExpansion.Location.X}, {bestExpansion.Location.Y}) with {bestExpansion.MineralCount} minerals");
            return bestExpansion.Location;
        }
        
        // If no known expansions, scan for new ones
        ScanForExpansionSites();
        bestExpansion = FindBestExpansion();
        
        if (bestExpansion != null)
        {
            Console.WriteLine($"DEBUG: Found new expansion site at ({bestExpansion.Location.X}, {bestExpansion.Location.Y}) with {bestExpansion.MineralCount} minerals");
            return bestExpansion.Location;
        }
        
        Console.WriteLine("DEBUG: No suitable expansion sites found");
        return TilePosition.Invalid;
    }
    
    // Get all current base locations (including main base)
    public IReadOnlyList<BaseLocation> GetAllBases()
    {
        return _knownBases.AsReadOnly();
    }
    
    // Get potential expansion sites
    public IReadOnlyList<BaseLocation> GetPotentialExpansions()
    {
        return _potentialExpansions.AsReadOnly();
    }
    
    // Check if a location is near any of our bases
    public bool IsNearExistingBase(TilePosition location, int minDistance = 15)
    {
        return _knownBases.Any(baseLocation => 
            CalculateDistance(location, baseLocation.Location) < minDistance);
    }
    
    private void UpdateExistingBases()
    {
        if (_game?.Self() == null) return;
        
        // Add main base if not already tracked
        var mainBase = _game.Self().GetStartLocation();
        if (!_knownBases.Any(b => b.Location.X == mainBase.X && b.Location.Y == mainBase.Y))
        {
            var mainBaseLocation = new BaseLocation
            {
                Location = mainBase,
                IsMainBase = true,
                HasNexus = true,
                MineralCount = CountNearbyMinerals(mainBase),
                GeyserCount = CountNearbyGeysers(mainBase)
            };
            _knownBases.Add(mainBaseLocation);
            Console.WriteLine($"DEBUG: Added main base at ({mainBase.X}, {mainBase.Y})");
        }
        
        // Update nexus presence for all bases
        foreach (var baseLocation in _knownBases)
        {
            baseLocation.HasNexus = HasNexusAt(baseLocation.Location);
        }
    }
    
    private void ScanForExpansionSites()
    {
        if (_game == null) return;
        
        // Group mineral patches by proximity to find resource clusters
        var mineralPatches = _game.GetMinerals()
            .Where(m => m.IsVisible() && m.GetResources() > 0)
            .ToList();
        
        var processedMinerals = new HashSet<Unit>();
        
        foreach (var mineral in mineralPatches)
        {
            if (processedMinerals.Contains(mineral)) continue;
            
            // Find cluster of minerals around this one
            var cluster = FindMineralCluster(mineral, mineralPatches, processedMinerals);
            
            if (cluster.Count >= 4) // Need at least 4 mineral patches for viable base
            {
                var clusterCenter = CalculateClusterCenter(cluster);
                
                // Check if this is a new potential expansion
                if (!IsKnownLocation(clusterCenter) && !IsNearExistingBase(clusterCenter))
                {
                    var expansion = new BaseLocation
                    {
                        Location = clusterCenter,
                        IsMainBase = false,
                        HasNexus = false,
                        MineralCount = cluster.Count,
                        GeyserCount = CountNearbyGeysers(clusterCenter),
                        MineralPatches = cluster.ToList()
                    };
                    
                    // Check if location is suitable for building
                    if (IsSuitableForBase(clusterCenter))
                    {
                        _potentialExpansions.Add(expansion);
                        Console.WriteLine($"DEBUG: Found potential expansion at ({clusterCenter.X}, {clusterCenter.Y}) with {cluster.Count} minerals, {expansion.GeyserCount} geysers");
                    }
                }
            }
            
            foreach (var m in cluster)
            {
                processedMinerals.Add(m);
            }
        }
    }
    
    private List<Unit> FindMineralCluster(Unit startMineral, List<Unit> allMinerals, HashSet<Unit> processed)
    {
        var cluster = new List<Unit> { startMineral };
        processed.Add(startMineral);
        
        const int CLUSTER_RADIUS = 10; // tiles
        
        var nearbyMinerals = allMinerals
            .Where(m => !processed.Contains(m))
            .Where(m => CalculateDistance(startMineral.GetTilePosition(), m.GetTilePosition()) <= CLUSTER_RADIUS)
            .ToList();
        
        foreach (var mineral in nearbyMinerals)
        {
            cluster.Add(mineral);
            processed.Add(mineral);
        }
        
        return cluster;
    }
    
    private TilePosition CalculateClusterCenter(List<Unit> cluster)
    {
        var avgX = (int)cluster.Average(m => m.GetTilePosition().X);
        var avgY = (int)cluster.Average(m => m.GetTilePosition().Y);
        
        // Adjust position to suitable building location nearby
        return FindNearestBuildablePosition(new TilePosition(avgX, avgY));
    }
    
    private TilePosition FindNearestBuildablePosition(TilePosition center)
    {
        if (_game?.CanBuildHere(center, UnitType.Protoss_Nexus) == true)
        {
            return center;
        }
        
        // Search in expanding radius for buildable location
        for (int radius = 1; radius <= 5; radius++)
        {
            for (int angle = 0; angle < 360; angle += 45)
            {
                var radians = angle * Math.PI / 180;
                var x = center.X + (int)(radius * Math.Cos(radians));
                var y = center.Y + (int)(radius * Math.Sin(radians));
                var candidate = new TilePosition(x, y);
                
                if (_game?.CanBuildHere(candidate, UnitType.Protoss_Nexus) == true)
                {
                    return candidate;
                }
            }
        }
        
        return center; // Fallback to original position
    }
    
    private bool IsKnownLocation(TilePosition location)
    {
        const int PROXIMITY_THRESHOLD = 8;
        
        return _knownBases.Any(b => CalculateDistance(b.Location, location) < PROXIMITY_THRESHOLD) ||
               _potentialExpansions.Any(e => CalculateDistance(e.Location, location) < PROXIMITY_THRESHOLD);
    }
    
    private bool IsSuitableForBase(TilePosition location)
    {
        if (_game == null) return false;
        
        // Check if we can build a nexus here
        if (!_game.CanBuildHere(location, UnitType.Protoss_Nexus))
        {
            return false;
        }
        
        // Check minimum distance from other bases
        if (IsNearExistingBase(location, 15))
        {
            return false;
        }
        
        return true;
    }
    
    private BaseLocation? FindBestExpansion()
    {
        if (!_potentialExpansions.Any()) return null;
        
        // Score expansions based on multiple factors
        return _potentialExpansions
            .Where(exp => IsSuitableForBase(exp.Location))
            .OrderByDescending(exp => CalculateExpansionScore(exp))
            .FirstOrDefault();
    }
    
    private double CalculateExpansionScore(BaseLocation expansion)
    {
        double score = 0;
        
        // More minerals = better
        score += expansion.MineralCount * 10;
        
        // Geysers add value
        score += expansion.GeyserCount * 15;
        
        // Closer to main base is generally better (easier to defend)
        var mainBase = _knownBases.FirstOrDefault(b => b.IsMainBase);
        if (mainBase != null)
        {
            var distance = CalculateDistance(expansion.Location, mainBase.Location);
            score += Math.Max(0, 50 - distance); // Closer is better, but cap the bonus
        }
        
        return score;
    }
    
    private int CountNearbyMinerals(TilePosition location)
    {
        if (_game == null) return 0;
        
        const int SEARCH_RADIUS = 12;
        
        return _game.GetMinerals()
            .Where(m => m.IsVisible() && m.GetResources() > 0)
            .Where(m => CalculateDistance(location, m.GetTilePosition()) <= SEARCH_RADIUS)
            .Count();
    }
    
    private int CountNearbyGeysers(TilePosition location)
    {
        if (_game == null) return 0;
        
        const int SEARCH_RADIUS = 12;
        
        return _game.GetGeysers()
            .Where(g => g.IsVisible())
            .Where(g => CalculateDistance(location, g.GetTilePosition()) <= SEARCH_RADIUS)
            .Count();
    }
    
    private bool HasNexusAt(TilePosition location)
    {
        if (_game?.Self() == null) return false;
        
        const int NEXUS_RADIUS = 5;
        
        return _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Nexus)
            .Any(nexus => CalculateDistance(location, nexus.GetTilePosition()) <= NEXUS_RADIUS);
    }
    
    private void CleanupDestroyedBases()
    {
        // Remove potential expansions that now have nexuses (they became actual bases)
        _potentialExpansions.RemoveAll(exp => HasNexusAt(exp.Location));
        
        // Move expansions with nexuses to known bases
        var newBases = _potentialExpansions
            .Where(exp => HasNexusAt(exp.Location))
            .ToList();
            
        foreach (var newBase in newBases)
        {
            newBase.HasNexus = true;
            _knownBases.Add(newBase);
        }
    }
    
    private double CalculateDistance(TilePosition pos1, TilePosition pos2)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

// Represents a base location (existing or potential)
public class BaseLocation
{
    public TilePosition Location { get; set; }
    public bool IsMainBase { get; set; }
    public bool HasNexus { get; set; }
    public int MineralCount { get; set; }
    public int GeyserCount { get; set; }
    public List<Unit> MineralPatches { get; set; } = new List<Unit>();
}