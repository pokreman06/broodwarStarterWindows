using System.Collections.Generic;
using System.Linq;
using BWAPI.NET;

namespace Shared;

// Step A: placement roles used to decide where buildings should go.
public enum BuildingRole
{
    MainEcon,
    NatEcon,
    RampBlock,
    ChokeDefense,
    ProductionRing,
    TechHide,
    RallyForward
}

public static class BuildingPlacementRoles
{
    // Default mapping of UnitType -> allowed roles. Each building can appear in multiple roles.
    private static readonly Dictionary<UnitType, List<BuildingRole>> _defaultMap = new Dictionary<UnitType, List<BuildingRole>>()
    {
        { UnitType.Terran_Command_Center, new List<BuildingRole> { BuildingRole.MainEcon, BuildingRole.NatEcon } },
        { UnitType.Terran_Refinery, new List<BuildingRole> { BuildingRole.MainEcon, BuildingRole.NatEcon } },
        { UnitType.Terran_Supply_Depot, new List<BuildingRole> { BuildingRole.RampBlock, BuildingRole.ProductionRing } },
        { UnitType.Terran_Barracks, new List<BuildingRole> { BuildingRole.ProductionRing, BuildingRole.RallyForward } },
        { UnitType.Terran_Factory, new List<BuildingRole> { BuildingRole.ProductionRing, BuildingRole.TechHide } },
        { UnitType.Terran_Starport, new List<BuildingRole> { BuildingRole.ProductionRing, BuildingRole.TechHide } },
        { UnitType.Terran_Engineering_Bay, new List<BuildingRole> { BuildingRole.ChokeDefense, BuildingRole.TechHide } },
        { UnitType.Terran_Missile_Turret, new List<BuildingRole> { BuildingRole.ChokeDefense } },
        { UnitType.Terran_Bunker, new List<BuildingRole> { BuildingRole.RampBlock, BuildingRole.ChokeDefense } },
        { UnitType.Terran_Academy, new List<BuildingRole> { BuildingRole.TechHide } },
        { UnitType.Terran_Armory, new List<BuildingRole> { BuildingRole.TechHide } }
    };

    // Returns the list of preferred roles for a building type. If none known, default to ProductionRing.
    public static IReadOnlyList<BuildingRole> GetRoles(UnitType type)
    {
        if (_defaultMap.TryGetValue(type, out var roles)) return roles;
        return new List<BuildingRole> { BuildingRole.ProductionRing };
    }

    // Allow runtime registration / override of roles for a UnitType.
    public static void RegisterRoles(UnitType type, IEnumerable<BuildingRole> roles)
    {
        _defaultMap[type] = roles.ToList();
    }
}
