using System;
using BWAPI.NET;

namespace Shared;

// Represents a single item in the build order with placement preferences
public class BuildOrderItem
{
    public UnitType UnitType { get; set; }
    
    // Optional sorting algorithm for range selection in placement
    // If null, default range ordering will be used
    public Func<TileRange, int>? SortingAlgorithm { get; set; }
    
    // If true, this build item cannot be reordered or modified by AI decisions
    // If false, AI can adjust timing/priority based on game state
    public bool IsFixed { get; set; }
    
    // Optional metadata for AI decision making
    public string? Reasoning { get; set; }
    public int Priority { get; set; } = 0;

    public BuildOrderItem(UnitType unitType, bool isFixed = false, Func<TileRange, int>? sortingAlgorithm = null)
    {
        UnitType = unitType;
        IsFixed = isFixed;
        SortingAlgorithm = sortingAlgorithm;
    }

    public override string ToString()
    {
        var fixedStr = IsFixed ? " (Fixed)" : "";
        var priorityStr = Priority != 0 ? $" [P{Priority}]" : "";
        return $"{UnitType}{fixedStr}{priorityStr}";
    }
}