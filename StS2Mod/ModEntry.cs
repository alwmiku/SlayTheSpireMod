using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Modding;

namespace StS2Mod;

/// <summary>
/// 给「仆从俯冲」(MinionDiveBomb) 添加抽 1 张牌效果。
/// MinionDiveBomb 是储君「冲锋！！」生成的 Token 随从牌。
/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    public static void Initialize()
    {
        var harmony = new Harmony("com.example.minion-divebomb-draw");
        harmony.PatchAll(typeof(ModEntry).Assembly);
    }
}

// ── 补丁 1：MinionDiveBomb.OnPlay 后抽 1 张牌 ───────────────
[HarmonyPatch(typeof(MinionDiveBomb), "OnPlay")]
internal static class MinionDiveBombOnPlayPatch
{
    [HarmonyPostfix]
    static async void Postfix(
        System.Threading.Tasks.Task __result,
        MinionDiveBomb __instance,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay)
    {
        try
        {
            if (__result != null)
                await __result;
            if (choiceContext == null || __instance?.Owner == null) return;
            if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsOverOrEnding != false) return;
            await CardPileCmd.Draw(choiceContext, 1m, __instance.Owner);
        }
        catch
        {
            // Avoid surfacing exceptions from async void patch callbacks.
        }
    }
}

// ── 补丁 2：MinionDiveBomb 描述追加本地化"抽 1 张牌" ───────────
[HarmonyPatch(typeof(CardModel),
    nameof(CardModel.GetDescriptionForPile),
    typeof(PileType), typeof(Creature))]
internal static class MinionDiveBombDescriptionPatch
{
    private static readonly Dictionary<string, string> DrawText = new()
    {
        ["eng"] = "Draw 1 card.",
        ["zhs"] = "抽 1 张牌。",
        ["zht"] = "抽 1 張牌。",
        ["jpn"] = "カードを1枚引く。",
        ["kor"] = "카드를 1장 뽑습니다.",
        ["fre"] = "Piochez 1 carte.",
        ["deu"] = "Ziehe 1 Karte.",
        ["spa"] = "Roba 1 carta.",
        ["ptb"] = "Compre 1 carta.",
        ["rus"] = "Возьмите 1 карту.",
    };

    [HarmonyPostfix]
    static string Postfix(string __result, CardModel __instance)
    {
        if (__instance is not MinionDiveBomb) return __result;

        string lang = LocManager.Instance?.Language ?? "eng";
        if (!DrawText.TryGetValue(lang, out var text))
            text = DrawText["eng"];

        return __result + "\n" + text;
    }
}
