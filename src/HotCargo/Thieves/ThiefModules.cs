using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Thieves;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcThief")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class ThiefStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new ThiefStrategy();
}

/// <summary>
/// Spec 1779. A thief that collects or flees takes over from every other module; one that fights, and every other ship, falls through to the
/// pirate AI. The phase lives in the run state, so this module keeps no state of its own.
/// </summary>
internal sealed class ThiefStrategy : AIStrategyModule
{
    public override bool OnAITick(Ship ship, ShipAI ai) => Thieves.Step(ship);

    public override AIStrategyModule Clone(AIParameters? parameters) => new ThiefStrategy();
}

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcRob")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class RobTargetingRules : AITargetingModuleRules
{
    public override AITargetingModule CreateModule(AIParameters? parameters) => new RobTargeting();
}

/// <summary>
/// Spec 1776. A fighting thief aims all its weapons at a cargo part of its attack target (ThievesModel.Rank and Pick): storage that holds
/// resources first, then empty storage, then other parts, never the command part or a reactor. Other ships, and thieves without an attack
/// target, assign nothing, so the modules below decide (Disable, the vanilla value targeter).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class RobTargeting : AITargetingModule
{
    [Serialize(Alias = "PartTarget", Optional = true)] Part? _partTarget;

    public override void GetIntegrityHash(IntegrityHash iHash) =>
        (_partTarget?.Location ?? new IntVector2(int.MinValue, int.MinValue)).GetIntegrityHash(iHash);

    public override void OnEnteringStasis(Ship ship) => _partTarget = null;

    public override void TargetWeapons(Ship ship, ShipAI ai, List<Weapon> weapons)
    {
        var target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target;
        if (Thieves.Of(ship) is not { Phase: ThiefPhase.Fight } || Run.Current is not { } run || target?.Physics.Body is null)
        {
            _partTarget = null;
            return;
        }
        var parts = target.Parts.Where(p => !p.IsDestroyed).OrderBy(p => p.Location.X).ThenBy(p => p.Location.Y).ToList();
        var from = ship.DetWorldCenter;
        var ranked = parts.Select(p => (Rank(p), from.DistanceTo(p.DetWorldCenter))).ToList();
        int pick = ThievesModel.Pick(ranked, _partTarget is null ? -1 : parts.IndexOf(_partTarget), run.T.Thieves.SwitchFactor);
        if (pick < 0)
        {
            _partTarget = null;
            return;
        }
        _partTarget = parts[pick];
        // Same assignment as AIValueTargeterModule.TargetWeapons. The funnel is not a gun: it keeps its own targets.
        for (int i = weapons.Count - 1; i >= 0; i--)
        {
            var weapon = weapons[i];
            if (!weapon.Part.Rules.TypeCategories.Contains(new("weapon")) || weapon.Rules.IsShipWideExplicitTargetingSuppressed(ship, _partTarget)) continue;
            if (weapon.ExplicitTargets.CurConfirmedTarget?.Target != _partTarget)
            {
                var wt = new WeaponTarget(weapon, _partTarget, ship.Weapons.AllocGlobalTargetID()) { PlayerTimeGiven = App.Clock.Time };
                weapon.ExplicitTargets.EnqueueTarget(wt, append: false, confirmed: true);
            }
            weapons.RemoveAt(i);
        }
    }

    static RobRank Rank(Part part)
    {
        var c = part.Rules.TypeCategories;
        // A Tech Vault is storage to the game, but the thieves want cargo: the tech is the hunters' business, and they never collect it.
        bool storage = c.Contains(new("storage")) && part.Rules.ID != Tech.Vaults.VaultId;
        bool holds = storage && part.Components.OfType<BaseResourceStorage>().Any(s => s.Resources > 0);
        return ThievesModel.Rank(storage, holds, command: c.Contains(new("command")), reactor: c.Contains(new("reactor")));
    }

    public override AITargetingModule Clone(AIParameters? parameters) => new RobTargeting { _partTarget = _partTarget };
}
