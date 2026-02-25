using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BWAPI.NET;

namespace Shared;

// Represents an axis-aligned inclusive tile rectangle for role ranges.
public class TileRange
{
    public TilePosition Min { get; }
    public TilePosition Max { get; }

    public TileRange(TilePosition a, TilePosition b)
    {
        int minX = Math.Min(a.X, b.X);
        int minY = Math.Min(a.Y, b.Y);
        int maxX = Math.Max(a.X, b.X);
        int maxY = Math.Max(a.Y, b.Y);
        Min = new TilePosition(minX, minY);
        Max = new TilePosition(maxX, maxY);
    }

    public bool Contains(TilePosition t)
    {
        return t.X >= Min.X && t.Y >= Min.Y && t.X <= Max.X && t.Y <= Max.Y;
    }

    public IEnumerable<TilePosition> EnumerateTiles()
    {
        for (int x = Min.X; x <= Max.X; x++)
            for (int y = Min.Y; y <= Max.Y; y++)
                yield return new TilePosition(x, y);
    }
}

// Manages multiple TileRange entries per BuildingRole.
public class BuildingRoleManager
{
    private readonly Dictionary<BuildingRole, List<TileRange>> _map = new();

    // Add a rectangular range for a role. The range is inclusive.
    public void AddRange(BuildingRole role, TilePosition a, TilePosition b)
    {
        if (!_map.TryGetValue(role, out var list))
        {
            list = new List<TileRange>();
            _map[role] = list;
        }
        list.Add(new TileRange(a, b));
    }

    // Return a copy of ranges for a role.
    public IReadOnlyList<TileRange> GetRanges(BuildingRole role)
    {
        if (_map.TryGetValue(role, out var list)) return list.AsReadOnly();
        return Array.Empty<TileRange>();
    }

    // Remove a specific range instance (exact match) for a role.
    public bool RemoveRange(BuildingRole role, TileRange range)
    {
        if (_map.TryGetValue(role, out var list)) return list.Remove(range);
        return false;
    }

    // Clear all ranges for a role.
    public void ClearRanges(BuildingRole role)
    {
        _map.Remove(role);
    }

    // Clear all stored ranges for all roles.
    public void ClearAll()
    {
        _map.Clear();
    }

    // Return all roles whose ranges contain the given tile.
    public IEnumerable<BuildingRole> RolesContaining(TilePosition t)
    {
        foreach (var kv in _map)
        {
            if (kv.Value.Any(r => r.Contains(t))) yield return kv.Key;
        }
    }

    // Try to find the first tile inside any range for the role that satisfies `predicate`.
    // The predicate receives x,y tile coords and should return true for an acceptable tile.
    public TilePosition FindTile(BuildingRole role, Func<int,int,bool> predicate)
    {
        if (!_map.TryGetValue(role, out var list)) return TilePosition.Invalid;
        foreach (var range in list)
        {
            for (int x = range.Min.X; x <= range.Max.X; x++)
            for (int y = range.Min.Y; y <= range.Max.Y; y++)
            {
                if (predicate(x, y)) return new TilePosition(x, y);
            }
        }
        return TilePosition.Invalid;
    }

    // Find a tile inside any range for the role, but allow sorting/ranking of ranges.
    // If `rangeRankSelector` is provided, ranges are ordered by its returned key (ascending).
    public TilePosition FindTileInRanges(BuildingRole role, Func<TileRange, int>? rangeRankSelector, Func<int,int,bool> predicate)
    {
        if (!_map.TryGetValue(role, out var list)) return TilePosition.Invalid;
        IEnumerable<TileRange> ranges = list;
        if (rangeRankSelector != null)
        {
            ranges = list.OrderBy(rangeRankSelector);
        }

        foreach (var range in ranges)
        {
            for (int x = range.Min.X; x <= range.Max.X; x++)
            for (int y = range.Min.Y; y <= range.Max.Y; y++)
            {
                if (predicate(x, y)) return new TilePosition(x, y);
            }
        }
        return TilePosition.Invalid;
    }

    // Load role ranges from a JSON file. Existing ranges for parsed roles are cleared.
    // JSON format example:
    // {
    //   "MainEcon": [ { "min": { "x": 10, "y": 10 }, "max": { "x": 20, "y": 20 } ]
    // }
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Role init file not found", path);
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dict = JsonSerializer.Deserialize<Dictionary<string, List<RangeDto>>>(json, options);
        if (dict == null) return;

        foreach (var kv in dict)
        {
            if (!Enum.TryParse<BuildingRole>(kv.Key, true, out var role)) continue;
            // replace existing ranges for this role
            ClearRanges(role);
            if (kv.Value == null) continue;
            foreach (var rd in kv.Value)
            {
                if (rd?.Min == null || rd?.Max == null) continue;
                AddRange(role, new TilePosition(rd.Min.X, rd.Min.Y), new TilePosition(rd.Max.X, rd.Max.Y));
            }
        }
    }

    // Save current ranges to a JSON file for editing.
    public void SaveToFile(string path)
    {
        var dict = new Dictionary<string, List<RangeDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _map)
        {
            var list = new List<RangeDto>();
            foreach (var r in kv.Value)
            {
                list.Add(new RangeDto { Min = new PosDto { X = r.Min.X, Y = r.Min.Y }, Max = new PosDto { X = r.Max.X, Y = r.Max.Y } });
            }
            dict[kv.Key.ToString()] = list;
        }
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(dict, options);
        File.WriteAllText(path, json);
    }

    private class PosDto { public int X { get; set; } public int Y { get; set; } }
    private class RangeDto { public PosDto Min { get; set; } public PosDto Max { get; set; } }
}
