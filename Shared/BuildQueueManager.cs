using System;
using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;

namespace Shared;

// Manages build queue execution, resource checking, and AI-driven build order updates
public class BuildQueueManager
{
    private readonly IAIController _aiController;
    private readonly PlacementManager _placementManager;
    private readonly BaseManager _baseManager;
    private Game? _game;
    private readonly HashSet<(UnitType, int, int)> _failedLocations;
    
    // Resource reservation system
    private readonly Dictionary<Unit, (UnitType unitType, int minerals, int gas)> _reservedResources;
    private int _totalReservedMinerals;
    private int _totalReservedGas;
    
    // Track pending builds waiting for construction confirmation
    private readonly Dictionary<UnitType, DateTime> _pendingBuilds;
    
    // Pylon power field tracking
    private readonly List<(TilePosition position, bool isCompleted)> _pylons;
    private readonly List<(TilePosition position, bool isCompleted)> _nexuses;
    private readonly List<(Unit geyser, bool hasAssimilator)> _gasGeysers;
    private const int PYLON_POWER_RADIUS = 8; // Pylon power field radius in tiles
    private const int MAX_BUILDING_DISTANCE_FROM_BASE = 35; // Maximum distance from main base for buildings

    public BuildQueueManager(IAIController aiController, PlacementManager placementManager, BaseManager baseManager)
    {
        _aiController = aiController;
        _placementManager = placementManager;
        _baseManager = baseManager;
        _failedLocations = new HashSet<(UnitType, int, int)>();
        _reservedResources = new Dictionary<Unit, (UnitType, int, int)>();
        _totalReservedMinerals = 0;
        _totalReservedGas = 0;
        _pendingBuilds = new Dictionary<UnitType, DateTime>();
        _pylons = new List<(TilePosition, bool)>();
        _nexuses = new List<(TilePosition, bool)>();
        _gasGeysers = new List<(Unit, bool)>();
    }

    public void UpdateGameState(Game game)
    {
        _game = game;
        _aiController.UpdateGameState(game);
        
        // Update base manager
        _baseManager.UpdateGameState(game);
        
        // Clean up completed/failed resource reservations
        CleanupResourceReservations();
        
        // Update pylon and nexus tracking
        UpdatePowerFieldTracking();
        
        // Update gas geyser tracking
        UpdateGasGeyserTracking();
        
        // Clean up old pending builds (timeout after 10 seconds)
        CleanupPendingBuilds();
    }

    // Process the build queue each frame - check resources and attempt builds
    public void ProcessBuildQueue()
    {
        if (_game?.Self() == null) return;

        var currentBuildOrder = _aiController.GetCurrentBuildOrder().ToList();
        if (!currentBuildOrder.Any()) 
        {
            Console.WriteLine("DEBUG: No items in build order");
            return;
        }

        Console.WriteLine($"DEBUG: Build order has {currentBuildOrder.Count} items, next: {currentBuildOrder.First().UnitType}");

        // Try to execute the next build item
        var nextBuild = currentBuildOrder.FirstOrDefault();
        if (nextBuild != null)
        {
            Console.WriteLine($"DEBUG: Checking if can afford {nextBuild.UnitType}");
            var canAfford = CanAfford(nextBuild.UnitType);
            
            Console.WriteLine($"DEBUG: Can afford: {canAfford}");
            
            if (canAfford)
            {
                // Check if this is a unit (needs training) or building (needs construction)
                if (IsUnit(nextBuild.UnitType))
                {
                    Console.WriteLine($"DEBUG: {nextBuild.UnitType} is a unit, finding production building");
                    if (AttemptUnitTraining(nextBuild.UnitType))
                    {
                        // Successfully issued training command - mark as pending
                        _pendingBuilds[nextBuild.UnitType] = DateTime.Now;
                        Console.WriteLine($"DEBUG: Training command issued for {nextBuild.UnitType}, awaiting construction start");
                        return; // Don't remove from queue yet
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG: {nextBuild.UnitType} is a building, requesting placement");
                    
                    // Try multiple placement locations until we find one that works
                    TilePosition placement = TilePosition.Invalid;
                    int attempts = 0;
                    const int maxAttempts = 50; // Increased attempts for better success rate
                    
                    while (placement == TilePosition.Invalid && attempts < maxAttempts)
                    {
                        var candidateLocation = attempts == 0 
                            ? _placementManager.BuildQueue(nextBuild.UnitType, nextBuild.SortingAlgorithm)
                            : FindAlternativeLocation(nextBuild.UnitType);
                            
                        // Check if we've already failed at this location
                        if (!_failedLocations.Contains((nextBuild.UnitType, candidateLocation.X, candidateLocation.Y)))
                        {
                            // Special handling for Assimilators - must be on gas geysers
                            if (nextBuild.UnitType == UnitType.Protoss_Assimilator)
                            {
                                var geyserLocation = FindAvailableGeyser();
                                if (geyserLocation != TilePosition.Invalid)
                                {
                                    placement = geyserLocation;
                                    Console.WriteLine($"DEBUG: Found gas geyser for Assimilator at ({geyserLocation.X}, {geyserLocation.Y})");
                                }
                                else
                                {
                                    Console.WriteLine($"DEBUG: No available gas geysers for Assimilator");
                                }
                            }
                            // Special handling for Nexus - use BaseManager for expansion sites
                            else if (nextBuild.UnitType == UnitType.Protoss_Nexus)
                            {
                                var nexusLocation = _baseManager.FindNexusLocation();
                                if (nexusLocation != TilePosition.Invalid)
                                {
                                    placement = nexusLocation;
                                    Console.WriteLine($"DEBUG: Found expansion site for Nexus at ({nexusLocation.X}, {nexusLocation.Y})");
                                }
                                else
                                {
                                    Console.WriteLine($"DEBUG: No suitable expansion sites found for Nexus");
                                }
                            }
                            // Check if location is appropriate (not near resources unless allowed)
                            else if (IsNearResourcesUnlessAppropriate(candidateLocation, nextBuild.UnitType))
                            {
                                Console.WriteLine($"DEBUG: Location ({candidateLocation.X}, {candidateLocation.Y}) too close to resources for {nextBuild.UnitType}");
                            }
                            // Check if location is within acceptable distance from base
                            else if (!IsWithinBaseDistance(candidateLocation, nextBuild.UnitType))
                            {
                                Console.WriteLine($"DEBUG: Location ({candidateLocation.X}, {candidateLocation.Y}) too far from base for {nextBuild.UnitType}");
                            }
                            // For Protoss buildings that require power, check if location has power
                            else if (RequiresPower(nextBuild.UnitType))
                            {
                                if (IsInPowerField(candidateLocation))
                                {
                                    placement = candidateLocation;
                                }
                                else
                                {
                                    Console.WriteLine($"DEBUG: Location ({candidateLocation.X}, {candidateLocation.Y}) not in power field for {nextBuild.UnitType}");
                                }
                            }
                            else
                            {
                                placement = candidateLocation;
                            }
                        }
                        
                        attempts++;
                        Console.WriteLine($"DEBUG: Placement attempt {attempts}: ({candidateLocation.X}, {candidateLocation.Y})");
                    }
                
                    Console.WriteLine($"DEBUG: Placement result: {placement.X}, {placement.Y}");
                    
                    if (placement != TilePosition.Invalid)
                    {
                        Console.WriteLine($"DEBUG: Finding builder for {nextBuild.UnitType}");
                        var builder = FindAvailableBuilder(nextBuild.UnitType);
                        
                        if (builder != null)
                        {
                            Console.WriteLine($"DEBUG: Found builder, attempting to build {nextBuild.UnitType}");
                            if (AttemptBuild(builder, nextBuild.UnitType, placement))
                            {
                                // Successfully issued build command - mark as pending
                                _pendingBuilds[nextBuild.UnitType] = DateTime.Now;
                                Console.WriteLine($"DEBUG: Build command issued for {nextBuild.UnitType}, awaiting construction start");
                                return; // Don't remove from queue yet
                            }
                            else
                            {
                                // Mark this location as failed
                                _failedLocations.Add((nextBuild.UnitType, placement.X, placement.Y));
                                Console.WriteLine($"DEBUG: Marked location ({placement.X}, {placement.Y}) as failed for {nextBuild.UnitType}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"DEBUG: No builder available for {nextBuild.UnitType}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"DEBUG: No valid placement found for {nextBuild.UnitType}");
                    }
                }
            }
        }

        // Periodically update AI suggestions and modify non-fixed items
        if (ShouldUpdateAISuggestions())
        {
            UpdateAISuggestions();
        }
    }

    // Check if we have sufficient resources for the unit type (accounting for reservations)
    private bool CanAfford(UnitType unitType)
    {
        if (_game?.Self() == null) return false;
        
        var player = _game.Self();
        var minerals = player.Minerals();
        var gas = player.Gas();
        var supply = player.SupplyUsed();
        var supplyTotal = player.SupplyTotal();

        // Get unit costs
        var unitCosts = GetUnitCosts(unitType);
        
        // Account for reserved resources
        var availableMinerals = minerals - _totalReservedMinerals;
        var availableGas = gas - _totalReservedGas;
        
        Console.WriteLine($"DEBUG: Resources: {minerals}M ({availableMinerals} available), {gas}G ({availableGas} available), {supply}/{supplyTotal} supply");
        Console.WriteLine($"DEBUG: Reserved: {_totalReservedMinerals}M, {_totalReservedGas}G from {_reservedResources.Count} pending builds");
        Console.WriteLine($"DEBUG: Need: {unitCosts.minerals}M, {unitCosts.gas}G, {unitCosts.supply} supply");
        
        var canAfford = availableMinerals >= unitCosts.minerals && 
                       availableGas >= unitCosts.gas && 
                       supply + unitCosts.supply <= supplyTotal;
        
        Console.WriteLine($"DEBUG: Can afford {unitType}: {canAfford}");
        return canAfford;
    }

    // Find an available builder (Probe for Protoss)
    private Unit? FindAvailableBuilder(UnitType unitType)
    {
        if (_game?.Self() == null) return null;

        // For Protoss, Probes can build everything
        var allProbes = _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == UnitType.Protoss_Probe)
            .ToList();
            
        var availableProbes = allProbes
            .Where(u => u.IsIdle() || u.IsGatheringMinerals())
            .OrderBy(u => u.IsIdle() ? 0 : 1) // Prefer idle workers
            .ToList();
        
        Console.WriteLine($"DEBUG: Found {allProbes.Count} total probes, {availableProbes.Count} available for building");
        
        return availableProbes.FirstOrDefault();
    }

    // Attempt to build the unit at the specified location
    private bool AttemptBuild(Unit builder, UnitType unitType, TilePosition location)
    {
        if (_game == null) return false;

        Console.WriteLine($"DEBUG: Checking if can build {unitType} at ({location.X}, {location.Y})");
        
        // Check if location is still valid
        if (!_game.CanBuildHere(location, unitType, builder))
        {
            Console.WriteLine($"DEBUG: Cannot build {unitType} at ({location.X}, {location.Y}) - location invalid");
            return false;
        }

        Console.WriteLine($"DEBUG: Location valid, issuing build command for {unitType}");
        
        // Issue build command
        var success = builder.Build(unitType, location);
        
        if (success)
        {
            // Reserve resources for this build
            var costs = GetUnitCosts(unitType);
            ReserveResources(builder, unitType, costs.minerals, costs.gas);
            Console.WriteLine($"SUCCESS: Build command issued for {unitType} at ({location.X}, {location.Y})");
            Console.WriteLine($"DEBUG: Reserved {costs.minerals}M, {costs.gas}G for {unitType} build");
        }
        else
        {
            Console.WriteLine($"ERROR: Build command failed for {unitType} at ({location.X}, {location.Y})");
        }
        
        return success;
    }

    // Find an alternative location when the placement manager fails
    private TilePosition FindAlternativeLocation(UnitType unitType)
    {
        if (_game?.Self() == null) return TilePosition.Invalid;

        // Special handling for Nexus - use BaseManager for expansion sites
        if (unitType == UnitType.Protoss_Nexus)
        {
            var nexusLocation = _baseManager.FindNexusLocation();
            if (nexusLocation != TilePosition.Invalid)
            {
                Console.WriteLine($"DEBUG: BaseManager found Nexus location at ({nexusLocation.X}, {nexusLocation.Y})");
                return nexusLocation;
            }
            Console.WriteLine($"DEBUG: BaseManager found no suitable Nexus locations, falling back to standard placement");
        }

        // Get our main base location
        var startLocation = _game.Self().GetStartLocation();
        
        // Try locations in expanding circles around our base
        var baseX = startLocation.X;
        var baseY = startLocation.Y;
        
        // Expanded search radius for better coverage
        var maxRadius = Math.Min(30, MAX_BUILDING_DISTANCE_FROM_BASE);
        
        for (int radius = 3; radius <= maxRadius; radius += 2)
        {
            // More angle increments for better coverage
            for (int angle = 0; angle < 360; angle += 30)
            {
                var radians = angle * Math.PI / 180;
                var x = baseX + (int)(radius * Math.Cos(radians));
                var y = baseY + (int)(radius * Math.Sin(radians));
                
                var candidate = new TilePosition(x, y);
                
                // Check if this location hasn't failed before
                if (!_failedLocations.Contains((unitType, x, y)) && 
                    _game.CanBuildHere(candidate, unitType) &&
                    !IsNearResourcesUnlessAppropriate(candidate, unitType) &&
                    IsWithinBaseDistance(candidate, unitType))
                {
                    // Special handling for Assimilators
                    if (unitType == UnitType.Protoss_Assimilator)
                    {
                        var geyserLocation = FindAvailableGeyser();
                        if (geyserLocation != TilePosition.Invalid)
                        {
                            Console.WriteLine($"DEBUG: Found gas geyser for Assimilator at ({geyserLocation.X}, {geyserLocation.Y})");
                            return geyserLocation;
                        }
                    }
                    // For Protoss buildings that require power, prefer locations with power
                    else if (RequiresPower(unitType))
                    {
                        if (IsInPowerField(candidate))
                        {
                            Console.WriteLine($"DEBUG: Found alternative location ({x}, {y}) at radius {radius} with power");
                            return candidate;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"DEBUG: Found alternative location ({x}, {y}) at radius {radius}");
                        return candidate;
                    }
                }
            }
        }
        
        Console.WriteLine($"DEBUG: No alternative location found for {unitType}");
        return TilePosition.Invalid;
    }
    
    // Power field management for Protoss
    private void UpdatePowerFieldTracking()
    {
        if (_game?.Self() == null) return;
        
        // Clear existing tracking
        _pylons.Clear();
        _nexuses.Clear();
        
        // Find all pylons and nexuses
        foreach (var unit in _game.Self().GetUnits())
        {
            if (unit.GetUnitType() == UnitType.Protoss_Pylon)
            {
                _pylons.Add((unit.GetTilePosition(), unit.IsCompleted()));
            }
            else if (unit.GetUnitType() == UnitType.Protoss_Nexus)
            {
                _nexuses.Add((unit.GetTilePosition(), unit.IsCompleted()));
            }
        }
        
        Console.WriteLine($"DEBUG: Tracking {_pylons.Count(p => p.isCompleted)} completed pylons, {_nexuses.Count(n => n.isCompleted)} nexuses");
    }
    
    private bool RequiresPower(UnitType unitType)
    {
        return unitType switch
        {
            // Buildings that DON'T require power
            UnitType.Protoss_Pylon => false,
            UnitType.Protoss_Nexus => false,
            UnitType.Protoss_Assimilator => false, // Built on geysers
            
            // All other Protoss buildings require power
            UnitType.Protoss_Gateway => true,
            UnitType.Protoss_Forge => true,
            UnitType.Protoss_Cybernetics_Core => true,
            UnitType.Protoss_Photon_Cannon => true,
            UnitType.Protoss_Robotics_Facility => true,
            UnitType.Protoss_Stargate => true,
            UnitType.Protoss_Citadel_of_Adun => true,
            UnitType.Protoss_Robotics_Support_Bay => true,
            UnitType.Protoss_Fleet_Beacon => true,
            UnitType.Protoss_Templar_Archives => true,
            UnitType.Protoss_Observatory => true,
            UnitType.Protoss_Shield_Battery => true,
            
            // Non-Protoss buildings don't require power
            _ => false
        };
    }
    
    private bool IsInPowerField(TilePosition location)
    {
        // Check if location is within power range of any completed pylon or nexus
        foreach (var (pylonPos, isCompleted) in _pylons)
        {
            if (isCompleted && IsWithinRange(location, pylonPos, PYLON_POWER_RADIUS))
            {
                return true;
            }
        }
        
        foreach (var (nexusPos, isCompleted) in _nexuses)
        {
            if (isCompleted && IsWithinRange(location, nexusPos, PYLON_POWER_RADIUS))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private bool IsWithinRange(TilePosition pos1, TilePosition pos2, int range)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        return distance <= range;
    }
    
    // Gas geyser management for Assimilators
    private void UpdateGasGeyserTracking()
    {
        if (_game == null) return;
        
        // Clear existing tracking
        _gasGeysers.Clear();
        
        // Find all gas geysers on the map
        foreach (var geyser in _game.GetGeysers())
        {
            if (geyser.IsVisible())
            {
                // Check if there's already an assimilator on this geyser
                bool hasAssimilator = _game.Self().GetUnits()
                    .Any(u => u.GetUnitType() == UnitType.Protoss_Assimilator && 
                              u.GetTilePosition().X == geyser.GetTilePosition().X &&
                              u.GetTilePosition().Y == geyser.GetTilePosition().Y);
                              
                _gasGeysers.Add((geyser, hasAssimilator));
            }
        }
        
        var availableGeysers = _gasGeysers.Count(g => !g.hasAssimilator);
        Console.WriteLine($"DEBUG: Found {_gasGeysers.Count} gas geysers, {availableGeysers} available for Assimilators");
    }
    
    private TilePosition FindAvailableGeyser()
    {
        if (_game?.Self() == null) return TilePosition.Invalid;
        
        // Find the closest available gas geyser to our main base
        var startLocation = _game.Self().GetStartLocation();
        Unit? closestGeyser = null;
        double closestDistance = double.MaxValue;
        
        foreach (var (geyser, hasAssimilator) in _gasGeysers)
        {
            if (!hasAssimilator)
            {
                var geyserPos = geyser.GetTilePosition();
                var dx = startLocation.X - geyserPos.X;
                var dy = startLocation.Y - geyserPos.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                
                // Only consider geysers within reasonable distance
                const int MAX_GEYSER_DISTANCE = 35;
                if (distance <= MAX_GEYSER_DISTANCE && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestGeyser = geyser;
                }
                else if (distance > MAX_GEYSER_DISTANCE)
                {
                    Console.WriteLine($"DEBUG: Geyser at ({geyserPos.X}, {geyserPos.Y}) too far (distance: {distance:F1})");
                }
            }
        }
        
        if (closestGeyser != null)
        {
            var geyserTilePos = closestGeyser.GetTilePosition();
            Console.WriteLine($"DEBUG: Found available geyser at ({geyserTilePos.X}, {geyserTilePos.Y}), distance: {closestDistance:F1}");
            return geyserTilePos;
        }
        
        Console.WriteLine($"DEBUG: No available gas geysers found within acceptable distance");
        return TilePosition.Invalid;
    }
    
    // Check if a building location is too close to resources (unless it's appropriate)
    private bool IsNearResourcesUnlessAppropriate(TilePosition location, UnitType unitType)
    {
        if (_game == null) return false;
        
        const int MIN_DISTANCE_FROM_RESOURCES = 3; // Minimum tiles away from resources
        
        // Buildings that are allowed near specific resources
        bool canBeNearMinerals = unitType == UnitType.Protoss_Nexus || 
                                unitType == UnitType.Terran_Command_Center ||
                                unitType == UnitType.Zerg_Hatchery;
                                
        bool canBeNearGas = unitType == UnitType.Protoss_Assimilator ||
                           unitType == UnitType.Terran_Refinery ||
                           unitType == UnitType.Zerg_Extractor;
        
        // Check distance to minerals
        if (!canBeNearMinerals)
        {
            foreach (var mineral in _game.GetMinerals())
            {
                if (mineral.IsVisible() && IsWithinRange(location, mineral.GetTilePosition(), MIN_DISTANCE_FROM_RESOURCES))
                {
                    Console.WriteLine($"DEBUG: {unitType} too close to minerals at ({mineral.GetTilePosition().X}, {mineral.GetTilePosition().Y})");
                    return true; // Too close to minerals
                }
            }
        }
        
        // Check distance to gas geysers
        if (!canBeNearGas)
        {
            foreach (var geyser in _game.GetGeysers())
            {
                if (geyser.IsVisible() && IsWithinRange(location, geyser.GetTilePosition(), MIN_DISTANCE_FROM_RESOURCES))
                {
                    Console.WriteLine($"DEBUG: {unitType} too close to gas geyser at ({geyser.GetTilePosition().X}, {geyser.GetTilePosition().Y})");
                    return true; // Too close to gas
                }
            }
        }
        
        return false; // Location is fine
    }
    
    // Check if a building location is within acceptable distance from main base
    private bool IsWithinBaseDistance(TilePosition location, UnitType unitType)
    {
        if (_game?.Self() == null) return true; // Allow if we can't determine base location
        
        var startLocation = _game.Self().GetStartLocation();
        
        // Calculate actual distance directly
        var dx = location.X - startLocation.X;
        var dy = location.Y - startLocation.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        
        // Different distance limits for different building types
        var maxDistance = unitType switch
        {
            // Resource buildings can be farther away (expansions, etc)
            UnitType.Protoss_Nexus => 50,
            UnitType.Protoss_Assimilator => 35, // Gas geysers might be a bit farther
            UnitType.Terran_Command_Center => 50,
            UnitType.Terran_Refinery => 35,
            
            // Defensive buildings can be at medium distance
            UnitType.Protoss_Photon_Cannon => 30,
            UnitType.Terran_Bunker => 30,
            UnitType.Terran_Missile_Turret => 30,
            
            // Most production buildings should stay close
            _ => MAX_BUILDING_DISTANCE_FROM_BASE
        };
        
        var withinDistance = distance <= maxDistance;
        
        if (!withinDistance)
        {
            Console.WriteLine($"DEBUG: {unitType} at distance {distance:F1} exceeds max {maxDistance} from base");
        }
        
        return withinDistance;
    }

    // Resource reservation management
    private void ReserveResources(Unit unit, UnitType unitType, int minerals, int gas)
    {
        // Release any existing reservation for this unit
        ReleaseReservation(unit);
        
        // Add new reservation
        _reservedResources[unit] = (unitType, minerals, gas);
        _totalReservedMinerals += minerals;
        _totalReservedGas += gas;
        
        Console.WriteLine($"DEBUG: Total reserved resources now: {_totalReservedMinerals}M, {_totalReservedGas}G");
    }
    
    private void ReleaseReservation(Unit unit)
    {
        if (_reservedResources.TryGetValue(unit, out var reservation))
        {
            _totalReservedMinerals -= reservation.minerals;
            _totalReservedGas -= reservation.gas;
            _reservedResources.Remove(unit);
            
            Console.WriteLine($"DEBUG: Released reservation for {reservation.unitType}: {reservation.minerals}M, {reservation.gas}G");
            Console.WriteLine($"DEBUG: Total reserved resources now: {_totalReservedMinerals}M, {_totalReservedGas}G");
        }
    }
    
    private void CleanupResourceReservations()
    {
        if (_game?.Self() == null) return;
        
        var unitsToRelease = new List<Unit>();
        
        foreach (var kvp in _reservedResources)
        {
            var unit = kvp.Key;
            var reservation = kvp.Value;
            
            // Check if unit still exists and what it's doing
            if (!_game.Self().GetUnits().Contains(unit))
            {
                // Unit no longer exists, release reservation
                unitsToRelease.Add(unit);
                Console.WriteLine($"DEBUG: Unit no longer exists, releasing {reservation.unitType} reservation");
            }
            else if (unit.IsConstructing())
            {
                // Worker started construction - resources already deducted, release reservation
                unitsToRelease.Add(unit);
                Console.WriteLine($"DEBUG: Construction started, releasing {reservation.unitType} reservation");
            }
            else if (unit.IsTraining())
            {
                // Building started training - resources already deducted, release reservation
                unitsToRelease.Add(unit);
                Console.WriteLine($"DEBUG: Training started, releasing {reservation.unitType} reservation");
            }
            else if (unit.IsIdle() && reservation.unitType != UnitType.None)
            {
                // Unit went idle without starting the reserved task - release reservation
                unitsToRelease.Add(unit);
                Console.WriteLine($"DEBUG: Unit went idle without starting {reservation.unitType}, releasing reservation");
            }
        }
        
        foreach (var unit in unitsToRelease)
        {
            ReleaseReservation(unit);
        }
    }

    // Check if a UnitType is a trainable unit (vs a constructible building)
    private bool IsUnit(UnitType unitType)
    {
        return unitType switch
        {
            // Protoss Units
            UnitType.Protoss_Probe => true,
            UnitType.Protoss_Zealot => true,
            UnitType.Protoss_Dragoon => true,
            UnitType.Protoss_High_Templar => true,
            UnitType.Protoss_Dark_Templar => true,
            UnitType.Protoss_Reaver => true,
            UnitType.Protoss_Observer => true,
            UnitType.Protoss_Shuttle => true,
            UnitType.Protoss_Scout => true,
            UnitType.Protoss_Corsair => true,
            UnitType.Protoss_Carrier => true,
            UnitType.Protoss_Arbiter => true,
            UnitType.Protoss_Interceptor => true,
            UnitType.Protoss_Scarab => true,
            // Terran Units
            UnitType.Terran_SCV => true,
            UnitType.Terran_Marine => true,
            UnitType.Terran_Firebat => true,
            UnitType.Terran_Medic => true,
            UnitType.Terran_Ghost => true,
            UnitType.Terran_Vulture => true,
            UnitType.Terran_Siege_Tank_Tank_Mode => true,
            UnitType.Terran_Goliath => true,
            UnitType.Terran_Wraith => true,
            UnitType.Terran_Dropship => true,
            UnitType.Terran_Science_Vessel => true,
            UnitType.Terran_Battlecruiser => true,
            UnitType.Terran_Valkyrie => true,
            // Everything else is a building
            _ => false
        };
    }

    // Attempt to train a unit from the appropriate building
    private bool AttemptUnitTraining(UnitType unitType)
    {
        if (_game?.Self() == null) return false;

        // Find the appropriate production building
        var productionBuilding = FindProductionBuilding(unitType);
        if (productionBuilding == null)
        {
            Console.WriteLine($"DEBUG: No production building available for {unitType}");
            return false;
        }

        Console.WriteLine($"DEBUG: Found production building {productionBuilding.GetUnitType()}, issuing train command");
        
        // Issue train command
        var success = productionBuilding.Train(unitType);
        
        if (success)
        {
            // Reserve resources for this training
            var costs = GetUnitCosts(unitType);
            ReserveResources(productionBuilding, unitType, costs.minerals, costs.gas);
            Console.WriteLine($"SUCCESS: Training command issued for {unitType}");
            Console.WriteLine($"DEBUG: Reserved {costs.minerals}M, {costs.gas}G for {unitType} training");
        }
        else
        {
            Console.WriteLine($"ERROR: Training command failed for {unitType}");
        }
        
        return success;
    }

    // Find an appropriate building to train the specified unit type
    private Unit? FindProductionBuilding(UnitType unitType)
    {
        if (_game?.Self() == null) return null;

        var requiredBuilding = GetRequiredProductionBuilding(unitType);
        if (requiredBuilding == UnitType.None) return null;

        return _game.Self().GetUnits()
            .Where(u => u.GetUnitType() == requiredBuilding)
            .Where(u => u.IsCompleted() && u.IsIdle())
            .FirstOrDefault();
    }

    // Get the building type required to produce a specific unit
    private UnitType GetRequiredProductionBuilding(UnitType unitType)
    {
        return unitType switch
        {
            // Protoss
            UnitType.Protoss_Probe => UnitType.Protoss_Nexus,
            UnitType.Protoss_Zealot => UnitType.Protoss_Gateway,
            UnitType.Protoss_Dragoon => UnitType.Protoss_Gateway, // Requires Cybernetics Core
            UnitType.Protoss_High_Templar => UnitType.Protoss_Gateway, // Requires Templar Archives
            UnitType.Protoss_Dark_Templar => UnitType.Protoss_Gateway, // Requires Templar Archives
            UnitType.Protoss_Reaver => UnitType.Protoss_Robotics_Facility,
            UnitType.Protoss_Observer => UnitType.Protoss_Robotics_Facility,
            UnitType.Protoss_Shuttle => UnitType.Protoss_Robotics_Facility,
            UnitType.Protoss_Scout => UnitType.Protoss_Stargate,
            UnitType.Protoss_Corsair => UnitType.Protoss_Stargate,
            UnitType.Protoss_Carrier => UnitType.Protoss_Stargate,
            UnitType.Protoss_Arbiter => UnitType.Protoss_Stargate,
            // Terran
            UnitType.Terran_SCV => UnitType.Terran_Command_Center,
            UnitType.Terran_Marine => UnitType.Terran_Barracks,
            UnitType.Terran_Firebat => UnitType.Terran_Barracks,
            UnitType.Terran_Medic => UnitType.Terran_Barracks,
            UnitType.Terran_Ghost => UnitType.Terran_Barracks,
            UnitType.Terran_Vulture => UnitType.Terran_Factory,
            UnitType.Terran_Siege_Tank_Tank_Mode => UnitType.Terran_Factory,
            UnitType.Terran_Goliath => UnitType.Terran_Factory,
            UnitType.Terran_Wraith => UnitType.Terran_Starport,
            UnitType.Terran_Dropship => UnitType.Terran_Starport,
            UnitType.Terran_Science_Vessel => UnitType.Terran_Starport,
            UnitType.Terran_Battlecruiser => UnitType.Terran_Starport,
            UnitType.Terran_Valkyrie => UnitType.Terran_Starport,
            _ => UnitType.None
        };
    }

    // Check if we should update AI suggestions (every few seconds)
    private bool ShouldUpdateAISuggestions()
    {
        // Simple time-based check - could be more sophisticated
        return _game?.GetFrameCount() % 240 == 0; // Every ~10 seconds at normal speed
    }

    // Update AI suggestions and modify non-fixed build order items
    private void UpdateAISuggestions()
    {
        var currentOrder = _aiController.GetCurrentBuildOrder().ToList();
        var recommendations = _aiController.GetRecommendedBuilds(5).ToList();
        
        if (!recommendations.Any()) return;

        var modifiedOrder = new List<BuildOrderItem>();
        var addedRecommendations = new HashSet<UnitType>();

        // Keep fixed items in order, but insert high-priority recommendations
        foreach (var item in currentOrder)
        {
            if (item.IsFixed)
            {
                modifiedOrder.Add(item);
                continue;
            }

            // Check if any high-priority recommendations should come first
            var urgentRec = recommendations
                .Where(r => r.Priority > item.Priority)
                .Where(r => !addedRecommendations.Contains(r.UnitType))
                .OrderByDescending(r => r.Priority)
                .FirstOrDefault();

            if (urgentRec != null)
            {
                modifiedOrder.Add(urgentRec);
                addedRecommendations.Add(urgentRec.UnitType);
                Console.WriteLine($"AI inserted urgent build: {urgentRec.UnitType} (Priority: {urgentRec.Priority})");
            }

            modifiedOrder.Add(item);
        }

        // Add remaining recommendations at the end
        foreach (var rec in recommendations.Where(r => !addedRecommendations.Contains(r.UnitType)))
        {
            modifiedOrder.Add(rec);
        }

        // Update the build order if changes were made
        if (!BuildOrdersEqual(currentOrder, modifiedOrder))
        {
            _aiController.UpdateBuildOrder(modifiedOrder);
            Console.WriteLine("AI updated build order with new suggestions");
        }
    }

    // Compare two build orders for equality
    private bool BuildOrdersEqual(List<BuildOrderItem> order1, List<BuildOrderItem> order2)
    {
        if (order1.Count != order2.Count) return false;
        
        for (int i = 0; i < order1.Count; i++)
        {
            if (order1[i].UnitType != order2[i].UnitType || 
                order1[i].IsFixed != order2[i].IsFixed)
                return false;
        }
        
        return true;
    }

    // Called when a build is successfully started
    private void OnBuildStarted(BuildOrderItem item)
    {
        Console.WriteLine($"Build started: {item.UnitType} (Fixed: {item.IsFixed})");
        
        // Trigger immediate AI suggestion update after successful build
        UpdateAISuggestions();
    }

    // Get unit costs - simplified version (should use actual game data)
    private (int minerals, int gas, int supply) GetUnitCosts(UnitType unitType)
    {
        return unitType switch
        {
            // Protoss Units
            UnitType.Protoss_Probe => (50, 0, 1),
            UnitType.Protoss_Pylon => (100, 0, 0),
            UnitType.Protoss_Gateway => (150, 0, 0),
            UnitType.Protoss_Assimilator => (100, 0, 0),
            UnitType.Protoss_Cybernetics_Core => (200, 0, 0),
            UnitType.Protoss_Forge => (150, 0, 0),
            UnitType.Protoss_Photon_Cannon => (150, 0, 0),
            UnitType.Protoss_Robotics_Facility => (200, 200, 0),
            UnitType.Protoss_Stargate => (150, 150, 0),
            UnitType.Protoss_Citadel_of_Adun => (150, 100, 0),
            UnitType.Protoss_Robotics_Support_Bay => (200, 200, 0),
            UnitType.Protoss_Fleet_Beacon => (300, 200, 0),
            UnitType.Protoss_Templar_Archives => (150, 200, 0),
            UnitType.Protoss_Observatory => (50, 100, 0),
            UnitType.Protoss_Shield_Battery => (100, 0, 0),
            UnitType.Protoss_Nexus => (400, 0, 0),
            // Protoss Units that require supply
            UnitType.Protoss_Zealot => (100, 0, 2),
            UnitType.Protoss_Dragoon => (125, 50, 2),
            UnitType.Protoss_High_Templar => (50, 150, 2),
            UnitType.Protoss_Dark_Templar => (125, 100, 2),
            UnitType.Protoss_Reaver => (200, 100, 4),
            UnitType.Protoss_Observer => (25, 75, 1),
            UnitType.Protoss_Shuttle => (200, 0, 2),
            UnitType.Protoss_Scout => (275, 125, 3),
            UnitType.Protoss_Corsair => (150, 100, 2),
            UnitType.Protoss_Carrier => (350, 250, 6),
            UnitType.Protoss_Arbiter => (100, 350, 4),
            // Terran Units (keeping for compatibility)
            UnitType.Terran_SCV => (50, 0, 1),
            UnitType.Terran_Supply_Depot => (100, 0, 0),
            UnitType.Terran_Barracks => (150, 0, 0),
            UnitType.Terran_Factory => (200, 100, 0),
            UnitType.Terran_Starport => (150, 100, 0),
            UnitType.Terran_Command_Center => (400, 0, 0),
            UnitType.Terran_Refinery => (100, 0, 0),
            UnitType.Terran_Engineering_Bay => (125, 0, 0),
            UnitType.Terran_Academy => (150, 0, 0),
            UnitType.Terran_Armory => (100, 50, 0),
            UnitType.Terran_Bunker => (100, 0, 0),
            UnitType.Terran_Missile_Turret => (75, 0, 0),
            _ => (0, 0, 0)
        };
    }

    // Public interface for external control
    public void AddToBuildQueue(BuildOrderItem item)
    {
        var currentOrder = _aiController.GetCurrentBuildOrder().ToList();
        currentOrder.Add(item);
        _aiController.UpdateBuildOrder(currentOrder);
        UpdateAISuggestions(); // Trigger AI update
    }

    public void ClearBuildQueue()
    {
        _aiController.UpdateBuildOrder(new List<BuildOrderItem>());
    }

    public IReadOnlyList<BuildOrderItem> GetCurrentBuildOrder()
    {
        return _aiController.GetCurrentBuildOrder();
    }

    // Force an immediate AI suggestion update
    public void ForceAIUpdate()
    {
        UpdateAISuggestions();
    }
    
    // Public methods for resource reservation management
    public void ReleaseResourceReservation(Unit unit)
    {
        ReleaseReservation(unit);
    }
    
    public (int minerals, int gas) GetReservedResources()
    {
        return (_totalReservedMinerals, _totalReservedGas);
    }
    
    public int GetActiveReservationCount()
    {
        return _reservedResources.Count;
    }
    
    public (int availableMinerals, int availableGas, int totalMinerals, int totalGas) GetAvailableResources()
    {
        if (_game?.Self() == null) return (0, 0, 0, 0);
        
        var totalMinerals = _game.Self().Minerals();
        var totalGas = _game.Self().Gas();
        var availableMinerals = totalMinerals - _totalReservedMinerals;
        var availableGas = totalGas - _totalReservedGas;
        
        return (availableMinerals, availableGas, totalMinerals, totalGas);
    }
    
    // Called when OnUnitCreate fires to confirm construction started
    public void OnUnitStartedConstruction(Unit unit)
    {
        var unitType = unit.GetUnitType();
        
        // Check if this unit was in our pending builds
        if (_pendingBuilds.ContainsKey(unitType))
        {
            // Remove from pending and remove from build queue
            _pendingBuilds.Remove(unitType);
            
            var currentOrder = _aiController.GetCurrentBuildOrder().ToList();
            var itemToRemove = currentOrder.FirstOrDefault(item => item.UnitType == unitType);
            
            if (itemToRemove != null)
            {
                var updatedOrder = currentOrder.Where(item => item != itemToRemove).ToList();
                _aiController.UpdateBuildOrder(updatedOrder);
                OnBuildStarted(itemToRemove);
                Console.WriteLine($"SUCCESS: {unitType} construction confirmed and removed from build queue");
            }
        }
        else
        {
            // Unit created that wasn't in our pending list (might be manually built or from AI suggestions)
            Console.WriteLine($"INFO: {unitType} created but wasn't in pending builds");
        }
    }
    
    // Clean up pending builds that have timed out (build command failed)
    private void CleanupPendingBuilds()
    {
        var timeoutThreshold = TimeSpan.FromSeconds(10);
        var timedOutBuilds = _pendingBuilds
            .Where(kvp => DateTime.Now - kvp.Value > timeoutThreshold)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var unitType in timedOutBuilds)
        {
            _pendingBuilds.Remove(unitType);
            Console.WriteLine($"WARNING: {unitType} build command timed out, will retry");
            
            // Also clean up any stale resource reservations for units that might have failed to build
            var staleBuildReservations = _reservedResources
                .Where(kvp => kvp.Value.unitType == unitType && 
                             !kvp.Key.IsConstructing() && 
                             !kvp.Key.IsTraining() &&
                             kvp.Key.IsIdle())
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var unit in staleBuildReservations)
            {
                ReleaseReservation(unit);
                Console.WriteLine($"DEBUG: Released stale reservation for {unitType}");
            }
        }
        
        if (_pendingBuilds.Any())
        {
            var pendingInfo = string.Join(", ", _pendingBuilds.Keys.Select(ut => ut.ToString()));
            Console.WriteLine($"DEBUG: Pending construction confirmations: {pendingInfo}");
        }
    }
}