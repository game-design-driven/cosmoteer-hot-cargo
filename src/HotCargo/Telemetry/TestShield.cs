using Cosmoteer.Crew;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace HotCargo.Telemetry;

/// <summary>
/// Test only: the autotest command "shieldplayers on" keeps human ship parts from losing health and human crew from dying,
/// so a scripted run (telemetry-run) does not depend on the starter ship surviving. Repairs still work. It changes the sim, so
/// in co-op it runs only with "mp" (every machine at the same tick). Turn it off around test commands that damage on purpose (vault damage).
/// "shieldhunters on" does the same for the parts of corporate hunters (tag hc_hunter), so a step that checks where hunters are
/// does not depend on who wins a fight. Their crew can still die.
/// </summary>
static class TestShield
{
    internal static bool On;
    internal static bool Hunters;

    static bool Shields(Part part) => part.Ship is { } ship
        && (On && ship.IsHumanPlayerShip || Hunters && HotCargo.Ai.AiGate.IsCorporateHunter(ship));

    /// <summary>The Health setter; a lower value keeps the current health.</summary>
    [HarmonyPatch(typeof(Part), nameof(Part.Health), MethodType.Setter)]
    static class HealthSetter
    {
        static void Prefix(Part __instance, ref int value)
        {
            if (Shields(__instance) && value < __instance.Health) value = __instance.Health;
        }
    }

    /// <summary>Damage with salvage (AddDamageAndSalvage goes through here).</summary>
    [HarmonyPatch(typeof(Part), nameof(Part.SetHealthAndSalvageDamage))]
    static class HealthAndSalvage
    {
        static void Prefix(Part __instance, ref int health)
        {
            if (Shields(__instance) && health < __instance.Health) health = __instance.Health;
        }
    }

    /// <summary>Crew that hit effects or fires would kill stay alive.</summary>
    [HarmonyPatch(typeof(CrewSoul), nameof(CrewSoul.Kill))]
    static class CrewKill
    {
        static bool Prefix(CrewSoul __instance) =>
            !(On && __instance.Sim?.Game is { } game && game.HumanPlayersIndexes.Contains(__instance.PlayerIndex));
    }
}
