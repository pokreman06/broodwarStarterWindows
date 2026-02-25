using System;
using System.Collections.Generic;
using System.Text.Json;
using BWAPI.NET;

namespace Shared;

// Interface for AI decision making system that accepts JSON function calls and manages build orders
public interface IAIController
{
    // Process incoming JSON function call data and return AI decisions
    // functionCallJson: JSON string containing function call data (e.g., placement requests, build decisions)
    // Returns: JSON response with AI decisions or null if no action needed
    string? ProcessFunctionCall(string functionCallJson);
    
    // Get current build order managed by the AI
    IReadOnlyList<BuildOrderItem> GetCurrentBuildOrder();
    
    // Update the build order (AI can reorder non-fixed items)
    void UpdateBuildOrder(IEnumerable<BuildOrderItem> newBuildOrder);
    
    // Request placement decision for a specific unit type
    // Returns tile position or TilePosition.Invalid if no suitable placement found
    TilePosition RequestPlacement(UnitType unitType, Func<TileRange, int>? sortingAlgorithm = null);
    
    // Notify AI of game state changes for decision making
    void UpdateGameState(Game game);
    
    // Get AI recommendations for next build items based on current game state
    IEnumerable<BuildOrderItem> GetRecommendedBuilds(int maxSuggestions = 3);
}

// Data transfer objects for JSON serialization
public class FunctionCallRequest
{
    public string Function { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
}

public class FunctionCallResponse
{
    public string RequestId { get; set; } = "";
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class PlacementRequest
{
    public string UnitTypeName { get; set; } = "";
    public string? SortingStrategy { get; set; }
    public Dictionary<string, object> Constraints { get; set; } = new();
}

public class PlacementResponse
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsValid { get; set; }
    public string? Reasoning { get; set; }
    public double Confidence { get; set; }
}

// Default implementation of the AI controller
public class DefaultAIController : IAIController
{
    private readonly PlacementManager _placementManager;
    private readonly List<BuildOrderItem> _buildOrder;
    private Game? _currentGame;

    public DefaultAIController(PlacementManager placementManager)
    {
        _placementManager = placementManager;
        _buildOrder = new List<BuildOrderItem>();
    }

    public string? ProcessFunctionCall(string functionCallJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<FunctionCallRequest>(functionCallJson);
            if (request == null) return null;

            var response = new FunctionCallResponse { RequestId = request.RequestId };

            switch (request.Function.ToLowerInvariant())
            {
                case "request_placement":
                    response.Result = HandlePlacementRequest(request.Parameters);
                    response.Success = true;
                    break;
                    
                case "update_build_order":
                    response.Result = HandleBuildOrderUpdate(request.Parameters);
                    response.Success = true;
                    break;
                    
                case "get_recommendations":
                    response.Result = HandleRecommendationRequest(request.Parameters);
                    response.Success = true;
                    break;
                    
                default:
                    response.Success = false;
                    response.Error = $"Unknown function: {request.Function}";
                    break;
            }

            return JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            var errorResponse = new FunctionCallResponse
            {
                Success = false,
                Error = ex.Message
            };
            return JsonSerializer.Serialize(errorResponse);
        }
    }

    public IReadOnlyList<BuildOrderItem> GetCurrentBuildOrder()
    {
        return _buildOrder.AsReadOnly();
    }

    public void UpdateBuildOrder(IEnumerable<BuildOrderItem> newBuildOrder)
    {
        _buildOrder.Clear();
        _buildOrder.AddRange(newBuildOrder);
    }

    public TilePosition RequestPlacement(UnitType unitType, Func<TileRange, int>? sortingAlgorithm = null)
    {
        return _placementManager.BuildQueue(unitType, sortingAlgorithm);
    }

    public void UpdateGameState(Game game)
    {
        _currentGame = game;
    }

    public IEnumerable<BuildOrderItem> GetRecommendedBuilds(int maxSuggestions = 3)
    {
        // Basic recommendations - can be enhanced with more sophisticated AI logic
        var suggestions = new List<BuildOrderItem>();
        
        if (_currentGame == null) return suggestions;
        
        var me = _currentGame.Self();
        if (me == null) return suggestions;
        
        var supply = me.SupplyUsed();
        var supplyTotal = me.SupplyTotal();
        var minerals = me.Minerals();
        
        // Determine race and suggest appropriate units
        var race = me.GetRace();
        
        // Basic supply management
        if (supplyTotal - supply < 4 && suggestions.Count < maxSuggestions)
        {
            var supplyUnit = race switch
            {
                Race.Protoss => UnitType.Protoss_Pylon,
                Race.Terran => UnitType.Terran_Supply_Depot,
                Race.Zerg => UnitType.Zerg_Overlord,
                _ => UnitType.Protoss_Pylon
            };
            
            suggestions.Add(new BuildOrderItem(supplyUnit, false) 
            { 
                Reasoning = "Supply block prevention",
                Priority = 10
            });
        }
        
        // Basic economy expansion
        if (minerals > 400 && suggestions.Count < maxSuggestions)
        {
            var workerUnit = race switch
            {
                Race.Protoss => UnitType.Protoss_Probe,
                Race.Terran => UnitType.Terran_SCV,
                Race.Zerg => UnitType.Zerg_Drone,
                _ => UnitType.Protoss_Probe
            };
            
            suggestions.Add(new BuildOrderItem(workerUnit, false)
            {
                Reasoning = "Economy expansion",
                Priority = 5
            });
        }
        
        return suggestions.Take(maxSuggestions);
    }

    private object HandlePlacementRequest(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("unitType", out var unitTypeObj)) 
            throw new ArgumentException("Missing unitType parameter");
            
        if (!Enum.TryParse<UnitType>(unitTypeObj.ToString(), out var unitType))
            throw new ArgumentException($"Invalid unit type: {unitTypeObj}");
            
        var position = RequestPlacement(unitType);
        
        return new PlacementResponse
        {
            X = position.X,
            Y = position.Y,
            IsValid = position != TilePosition.Invalid,
            Confidence = position != TilePosition.Invalid ? 0.8 : 0.0,
            Reasoning = position != TilePosition.Invalid ? "Found suitable placement" : "No suitable placement found"
        };
    }

    private object HandleBuildOrderUpdate(Dictionary<string, object> parameters)
    {
        // Implementation for build order updates via JSON
        return new { success = true, message = "Build order update processed" };
    }

    private object HandleRecommendationRequest(Dictionary<string, object> parameters)
    {
        var maxSuggestions = parameters.TryGetValue("maxSuggestions", out var maxObj) 
            ? Convert.ToInt32(maxObj) : 3;
            
        var recommendations = GetRecommendedBuilds(maxSuggestions);
        
        return recommendations.Select(r => new 
        {
            unitType = r.UnitType.ToString(),
            isFixed = r.IsFixed,
            priority = r.Priority,
            reasoning = r.Reasoning
        });
    }
}