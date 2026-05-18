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

namespace ChargeDrawMod;

/// <summary>
/// 缁欍€屼粏浠庝刊鍐层€?MinionDiveBomb) 娣诲姞鎶?1 寮犵墝鏁堟灉銆?/// MinionDiveBomb 鏄偍鍚涖€屽啿閿嬶紒锛併€嶇敓鎴愮殑 Token 闅忎粠鐗屻€?/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    public static void Initialize()
    {
        var harmony = new Harmony("com.example.minion-divebomb-draw");
        harmony.PatchAll(typeof(ModEntry).Assembly);
    }
}

// 鈹€鈹€ 琛ヤ竵 1锛歁inionDiveBomb.OnPlay 鍚庢娊 1 寮犵墝 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
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
            // Swallow exceptions to avoid breaking game flow from async void patch callbacks.
        }
    }
}

// 鈹€鈹€ 琛ヤ竵 2锛歁inionDiveBomb 鎻忚堪杩藉姞鏈湴鍖?鎶?1 寮犵墝" 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
[HarmonyPatch(typeof(CardModel),
    nameof(CardModel.GetDescriptionForPile),
    typeof(PileType), typeof(Creature))]
internal static class MinionDiveBombDescriptionPatch
{
    private static readonly Dictionary<string, string> DrawText = new()
    {
        ["eng"] = "Draw 1 card.",
        ["zhs"] = "鎶?1 寮犵墝銆?,
        ["zht"] = "鎶?1 寮电墝銆?,
        ["jpn"] = "銈兗銉夈倰1鏋氬紩銇忋€?,
        ["kor"] = "旃措摐毳?1鞛?虢戩姷雼堧嫟.",
        ["fre"] = "Piochez 1 carte.",
        ["deu"] = "Ziehe 1 Karte.",
        ["spa"] = "Roba 1 carta.",
        ["ptb"] = "Compre 1 carta.",
        ["rus"] = "袙芯蟹褜屑懈褌械 1 泻邪褉褌褍.",
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

