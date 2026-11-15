using Cosmoteer.Game.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Debt;

/// <summary>Spec 1419: the money display's tooltip lists the local player's debts under the vanilla text. UI only.</summary>
[HarmonyPatch(typeof(PlayerMoneyLabel), MethodType.Constructor, typeof(Cosmoteer.Game.GameRoot))]
static class MoneyTip
{
    static void Postfix(PlayerMoneyLabel __instance) => Errors.Guard("money tooltip", () =>
    {
        var vanilla = __instance.ToolTips.TextProvider;
        var game = __instance.Game;
        __instance.ToolTips.TextProvider = new Ux.LiveText(() =>
            (vanilla?.Text ?? "") + (Run.Current is { } run && run.Game == game ? DebtArea.MoneyTipLines(run) : ""));
    });
}
