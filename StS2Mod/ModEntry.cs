using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.addons.mega_text;
using StS2Mod.Models.Cards;
using StS2Mod.Models.Characters;
using StS2Mod.Models.Relics;

namespace StS2Mod;

/// <summary>
/// 模组入口。游戏加载 DLL 后会调用这里，并把当前程序集里的 Harmony 补丁全部挂上。
/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    public static void Initialize()
    {
        var harmony = new Harmony("com.example.qiqun-cards");
        harmony.PatchAll(typeof(ModEntry).Assembly);
        ShiPaiDrawPileScreenOrderPatch.TryPatch(harmony);
    }
}

/// <summary>
/// 原版角色列表是固定写死的，这里把“柒群”追加进去，角色选择界面才会看到它。
/// Distinct 用来避免热重载或多次取值时重复插入同一个模型。
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
internal static class ModelDbAllCharactersPatch
{
    [HarmonyPostfix]
    static void Postfix(ref IEnumerable<CharacterModel> __result)
    {
        __result = __result.Concat(new[] { ModelDb.Character<Qiqun>() }).Distinct();
    }
}

/// <summary>
/// 给柒群的原型牌补充“机制词条”说明。
/// 基础描述仍然走 localization/cards.json，这里只追加还没有正式关键字化的临时机制说明。
/// </summary>
[HarmonyPatch(typeof(CardModel),
    nameof(CardModel.GetDescriptionForPile),
    typeof(PileType), typeof(Creature))]
internal static class QiqunCardDescriptionPatch
{
    private static readonly Dictionary<string, Dictionary<string, string>> ExtraText = new()
    {
        ["QIQUN_DRAW_ONE"] = new()
        {
            ["eng"] = "Does not take up hand size.\nAt the start of your turn, if this is in your discard pile, return it to your hand.",
            ["zhs"] = "不占用手牌数量。\n回合开始时，如果这张牌在弃牌堆，将其移回手牌。",
            ["zht"] = "不占用手牌數量。\n回合開始時，如果這張牌在棄牌堆，將其移回手牌。"
        },
        ["QIQUN_SHUFFLE"] = new()
        {
            ["eng"] = "Actively shuffles.",
            ["zhs"] = "主动洗牌。",
            ["zht"] = "主動洗牌。"
        }
    };

    [HarmonyPostfix]
    static string Postfix(string __result, CardModel __instance)
    {
        if (!ExtraText.TryGetValue(__instance.Id.Entry, out var translations))
            return __result;

        // 先按当前游戏语言取文案，未覆盖的语言回落到英文，避免空描述。
        var lang = LocManager.Instance?.Language ?? "eng";
        if (!translations.TryGetValue(lang, out var text))
            text = translations["eng"];

        return string.IsNullOrWhiteSpace(__result)
            ? text
            : __result + "\n" + text;
    }
}

/// <summary>
/// 抽丝在手牌中时不占用 10 张手牌上限。
/// 原版抽牌和加入手牌都会直接读取 hand.Cards.Count，所以这里提供“有效手牌数”：
/// 总手牌数 - 抽丝数量。需要走原版加入手牌逻辑时，则临时移出抽丝来绕过物理上限判断。
/// </summary>
internal static class QiqunHandSize
{
    public const int MaxHandSize = 10;

    private static readonly AsyncLocal<bool> WrappingPileCommand = new();

    public static bool IsWrappingPileCommand
    {
        get => WrappingPileCommand.Value;
        set => WrappingPileCommand.Value = value;
    }

    public static int EffectiveCount(CardPile? pile)
    {
        if (pile?.Type != PileType.Hand)
            return pile?.Cards.Count ?? 0;

        return pile.Cards.Count(card => card is not QiqunDrawOne);
    }

    public static bool IsFull(CardPile? pile)
    {
        return EffectiveCount(pile) >= MaxHandSize;
    }

    public static bool IsFull(Player player)
    {
        return IsFull(PileType.Hand.GetPile(player));
    }

    public static bool HasFreeHandSizeCards(Player player)
    {
        return PileType.Hand.GetPile(player).Cards.Any(card => card is QiqunDrawOne);
    }

    public static IReadOnlyList<TemporarilyRemovedHandCard> TemporarilyRemoveFreeHandSizeCards(Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        var cards = hand.Cards
            .Select((card, index) => new { card, index })
            .Where(item => item.card is QiqunDrawOne)
            .ToList();

        var removed = new List<TemporarilyRemovedHandCard>(cards.Count);
        foreach (var item in cards)
        {
            hand.RemoveInternal(item.card, silent: true);
            removed.Add(new TemporarilyRemovedHandCard(item.card, item.index));
        }

        return removed;
    }

    public static IReadOnlyList<TemporarilyRemovedHandCard> TemporarilyMakeRoomForFreeCards(Player player, int count)
    {
        if (count <= 0)
            return new List<TemporarilyRemovedHandCard>();

        var hand = PileType.Hand.GetPile(player);
        var removed = new List<TemporarilyRemovedHandCard>();

        while (hand.Cards.Count >= MaxHandSize && removed.Count < count)
        {
            var card = hand.Cards.FirstOrDefault();
            if (card == null)
                break;

            removed.Add(RemoveCard(hand, card));
        }

        return removed;
    }

    public static void Restore(Player player, IReadOnlyList<TemporarilyRemovedHandCard> removed)
    {
        if (removed.Count == 0)
            return;

        var hand = PileType.Hand.GetPile(player);
        foreach (var item in removed.OrderBy(item => item.Index))
        {
            if (item.Card.Pile != null)
                continue;

            hand.AddInternal(item.Card, Math.Min(item.Index, hand.Cards.Count), silent: true);
        }
    }

    private static TemporarilyRemovedHandCard RemoveCard(CardPile hand, CardModel card)
    {
        var index = hand.Cards.ToList().IndexOf(card);
        hand.RemoveInternal(card, silent: true);
        return new TemporarilyRemovedHandCard(card, index);
    }

    public readonly record struct TemporarilyRemovedHandCard(CardModel Card, int Index);
}

/// <summary>
/// 替换原版抽牌流程，只把“手牌已满”的判断改成按有效手牌数计算。
/// 这样抽丝无论原本就在手牌，还是本次抽牌抽上来，都不会占用 10 张手牌上限。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
    typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool))]
internal static class QiqunCardPileDrawHandLimitPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw,
        ref System.Threading.Tasks.Task<IEnumerable<CardModel>> __result)
    {
        if (QiqunHandSize.IsWrappingPileCommand)
            return true;

        __result = DrawWithQiqunHandSize(choiceContext, count, player, fromHandDraw);
        return false;
    }

    private static async System.Threading.Tasks.Task<IEnumerable<CardModel>> DrawWithQiqunHandSize(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return Array.Empty<CardModel>();

        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return Array.Empty<CardModel>();

        if (fromHandDraw && ShiPaiRules.Has(player))
            return Array.Empty<CardModel>();

        if (!Hook.ShouldDraw(combatState, player, fromHandDraw, out var modifier))
        {
            if (modifier != null)
                await Hook.AfterPreventingDraw(combatState, modifier);

            return Array.Empty<CardModel>();
        }

        var result = new List<CardModel>();
        var hand = PileType.Hand.GetPile(player);
        var drawPile = PileType.Draw.GetPile(player);
        var drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
            return result;

        if (!CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player))
            return result;

        for (var i = 0; i < drawsRequested; i++)
        {
            await CardPileCmd.ShuffleIfNecessary(choiceContext, player);
            if (!CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player))
                break;

            var card = drawPile.Cards.FirstOrDefault();
            if (card == null)
                break;

            if (QiqunHandSize.IsFull(hand) && card is not QiqunDrawOne)
                break;

            result.Add(card);
            await CardPileCmd.Add(card, hand);
            CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
            await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
            card.InvokeDrawn();
            NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);
        }

        return result;
    }

    private static bool CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(Player player)
    {
        var drawPile = PileType.Draw.GetPile(player);
        var discardPile = PileType.Discard.GetPile(player);
        if (drawPile.Cards.Count + discardPile.Cards.Count == 0)
        {
            ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
            return false;
        }

        if (!QiqunHandSize.IsFull(player))
            return true;

        if (drawPile.Cards.FirstOrDefault() is QiqunDrawOne)
            return true;

        // 抽牌堆为空时，下一步会先洗牌；弃牌堆里有抽丝就允许继续进入洗牌检查。
        if (!drawPile.Cards.Any() && discardPile.Cards.Any(card => card is QiqunDrawOne))
            return true;

        ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
        return false;
    }
}

/// <summary>
/// 其他效果直接把牌加入手牌时，也需要忽略已经在手里的抽丝。
/// 如果加入的全是抽丝，则即使普通手牌已经满 10 张，也临时腾出物理位置让抽丝进手。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
    typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool))]
internal static class QiqunCardPileAddHandLimitPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        IEnumerable<CardModel> cards,
        CardPile newPile,
        CardPilePosition position,
        AbstractModel? source,
        bool skipVisuals,
        ref System.Threading.Tasks.Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        if (QiqunHandSize.IsWrappingPileCommand || newPile.Type != PileType.Hand)
            return true;

        var cardList = cards.ToList();
        var owner = cardList.FirstOrDefault()?.Owner;
        if (owner == null)
            return true;

        var hasFreeCardsInHand = QiqunHandSize.HasFreeHandSizeCards(owner);
        var addingOnlyFreeCards = cardList.Count > 0 && cardList.All(card => card is QiqunDrawOne);
        if (!hasFreeCardsInHand && !addingOnlyFreeCards)
            return true;

        __result = AddWithFreeHandSizeCards(cardList, newPile, position, source, skipVisuals, owner, addingOnlyFreeCards);
        return false;
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<CardPileAddResult>> AddWithFreeHandSizeCards(
        IReadOnlyList<CardModel> cards,
        CardPile newPile,
        CardPilePosition position,
        AbstractModel? source,
        bool skipVisuals,
        Player owner,
        bool addingOnlyFreeCards)
    {
        QiqunHandSize.IsWrappingPileCommand = true;
        var removed = QiqunHandSize.TemporarilyRemoveFreeHandSizeCards(owner).ToList();
        if (addingOnlyFreeCards)
            removed.AddRange(QiqunHandSize.TemporarilyMakeRoomForFreeCards(owner, cards.Count));

        try
        {
            return await CardPileCmd.Add(cards, newPile, position, source, skipVisuals);
        }
        finally
        {
            QiqunHandSize.Restore(owner, removed);
            QiqunHandSize.IsWrappingPileCommand = false;
        }
    }
}

/// <summary>
/// 识牌的共享判定。遗物自身能处理“每回合抽几张”，但自动洗牌和抽牌堆界面都在外部系统里，
/// 所以把“玩家是否持有可用识牌”的判断集中到这里，避免多个补丁各写一套。
/// </summary>
internal static class ShiPaiRules
{
    public static bool Has(Player player)
    {
        var relic = player.GetRelic<ShiPai>();
        return relic != null && !relic.IsMelted;
    }

    public static bool IsRevealedDrawPile(CardPile? pile)
    {
        if (pile == null || pile.Type != PileType.Draw)
            return false;

        var combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState?.Players.Any(player => Has(player) && player.PlayerCombatState?.DrawPile == pile) ?? false;
    }
}

/// <summary>
/// 识牌禁止原版自动洗牌：抽牌堆空了也不会把弃牌堆自动洗回去。
/// 主动打出“理牌”仍然直接调用 CardPileCmd.Shuffle，因此不会被这个补丁拦住。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.ShuffleIfNecessary))]
internal static class ShiPaiNoAutoShufflePatch
{
    [HarmonyPrefix]
    static bool Prefix(Player player, ref System.Threading.Tasks.Task __result)
    {
        if (!ShiPaiRules.Has(player))
            return true;

        // ShuffleIfNecessary 返回 Task。Prefix 跳过原方法时必须补一个已完成任务；
        // 否则抽丝里的 await 会拿到 null，打出卡牌后流程会直接卡住。
        __result = System.Threading.Tasks.Task.CompletedTask;
        return false;
    }
}

/// <summary>
/// 原版抽牌堆查看界面会按稀有度和 ID 排序，这是为了隐藏真实抽牌顺序。
/// 识牌的核心信息就是“识别牌序”，所以持有识牌时直接把抽牌堆当前顺序交给卡牌网格。
/// 这个补丁目标是私有 UI 方法，游戏更新时最容易变；因此手动注册，找不到目标时只跳过本功能。
/// </summary>
internal static class ShiPaiDrawPileScreenOrderPatch
{
    private static readonly System.Reflection.FieldInfo GridField =
        AccessTools.Field(typeof(NCardPileScreen), "_grid");

    private static readonly System.Reflection.FieldInfo BottomLabelField =
        AccessTools.Field(typeof(NCardPileScreen), "_bottomLabel");

    public static void TryPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(NCardPileScreen), "OnPileContentsChanged");
        var prefix = AccessTools.Method(typeof(ShiPaiDrawPileScreenOrderPatch), nameof(Prefix));

        if (target == null || prefix == null || GridField == null || BottomLabelField == null)
            return;

        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    static bool Prefix(NCardPileScreen __instance)
    {
        var pile = __instance.Pile;
        if (!ShiPaiRules.IsRevealedDrawPile(pile))
            return true;

        if (GridField.GetValue(__instance) is not NCardGrid grid)
            return true;

        grid.SetCards(
            pile.Cards.ToList(),
            pile.Type,
            new List<SortingOrders> { SortingOrders.Ascending });

        if (BottomLabelField.GetValue(__instance) is MegaRichTextLabel bottomLabel)
            bottomLabel.Text = "[center]识牌：当前按真实抽牌顺序显示。";

        return false;
    }
}

/// <summary>
/// 柒群目前是机制原型，还没有自己的角色美术、地图图标、音效和战斗模型。
/// 这里集中保存所有“临时复用静默猎手资源”的路径，后续替换专属资源时只需要改这一处。
/// </summary>
internal static class QiqunSilentAssetPaths
{
    public const string Visuals = "res://scenes/creature_visuals/silent.tscn";
    public const string IconTexture = "res://images/ui/top_panel/character_icon_silent.png";
    public const string IconOutlineTexture = "res://images/ui/top_panel/character_icon_silent_outline.png";
    public const string Icon = "res://scenes/ui/character_icons/silent_icon.tscn";
    public const string EnergyCounter = "res://scenes/combat/energy_counters/silent_energy_counter.tscn";
    public const string RestSiteAnim = "res://scenes/rest_site/characters/silent_rest_site.tscn";
    public const string MerchantAnim = "res://scenes/merchant/characters/silent_merchant.tscn";
    public const string CharacterSelectBg = "res://scenes/screens/char_select/char_select_bg_silent.tscn";
    public const string CharacterSelectIcon = "res://images/packed/character_select/char_select_silent.png";
    public const string CharacterSelectLockedIcon = "res://images/packed/character_select/char_select_silent_locked.png";
    public const string CharacterSelectTransition = "res://materials/transitions/silent_transition_mat.tres";
    public const string MapMarker = "res://images/packed/map/icons/map_marker_silent.png";
    public const string Trail = "res://scenes/vfx/card_trail_silent.tscn";
    public const string ArmPointing = "res://images/ui/hands/multiplayer_hand_silent_point.png";
    public const string ArmRock = "res://images/ui/hands/multiplayer_hand_silent_rock.png";
    public const string ArmPaper = "res://images/ui/hands/multiplayer_hand_silent_paper.png";
    public const string ArmScissors = "res://images/ui/hands/multiplayer_hand_silent_scissors.png";

    public static readonly string[] CharacterSelectAssets =
    {
        CharacterSelectBg,
        CharacterSelectIcon,
        IconTexture,
        CharacterSelectLockedIcon,
        CharacterSelectTransition
    };

    public static readonly string[] RunAssets =
    {
        Visuals,
        IconTexture,
        Icon,
        EnergyCounter,
        RestSiteAnim,
        MerchantAnim,
        CharacterSelectTransition,
        MapMarker,
        Trail
    };
}

/// <summary>
/// 角色选择界面的预加载资源。
/// CharacterModel 会默认按角色 ID 拼 qiqun_* 路径，但这些资源现在不存在，
/// 所以这里直接返回静默猎手资源，避免进入角色选择时加载失败。
/// </summary>
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.AssetPathsCharacterSelect), MethodType.Getter)]
internal static class QiqunAssetPathsCharacterSelectPatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref IEnumerable<string> __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = QiqunSilentAssetPaths.CharacterSelectAssets;
        return false;
    }
}

/// <summary>
/// 跑图/战斗中的角色预加载资源。
/// 这是最关键的资源兜底：预加载器会批量读取 AssetPaths，
/// 如果不改这里，即使单个 getter 被 patch，批量加载仍可能去找不存在的 qiqun 资源。
/// </summary>
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.AssetPaths), MethodType.Getter)]
internal static class QiqunAssetPathsPatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref IEnumerable<string> __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = QiqunSilentAssetPaths.RunAssets;
        return false;
    }
}

// 以下这些 getter 补丁用于兜住运行时的直接访问场景。
// 预加载走 AssetPaths；某些 UI 节点或战斗节点会单独读某个属性，因此也要同步重定向。
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.TrailPath), MethodType.Getter)]
internal static class QiqunTrailPathPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.Trail;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.EnergyCounterPath), MethodType.Getter)]
internal static class QiqunEnergyCounterPathPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.EnergyCounter;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.MerchantAnimPath), MethodType.Getter)]
internal static class QiqunMerchantAnimPathPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.MerchantAnim;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.RestSiteAnimPath), MethodType.Getter)]
internal static class QiqunRestSiteAnimPathPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.RestSiteAnim;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectBg), MethodType.Getter)]
internal static class QiqunCharacterSelectBgPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.CharacterSelectBg;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectTransitionPath), MethodType.Getter)]
internal static class QiqunCharacterSelectTransitionPathPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = QiqunSilentAssetPaths.CharacterSelectTransition;
    }
}

/// <summary>
/// 地图节点显示角色头像时会直接读取 MapMarker。
/// 因为 MapMarkerPath 是 protected，不能直接覆写实例属性访问，所以在 getter 上拦截。
/// </summary>
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.MapMarker), MethodType.Getter)]
internal static class QiqunMapMarkerPatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref CompressedTexture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetCompressedTexture2D(QiqunSilentAssetPaths.MapMarker);
        return false;
    }
}

// 角色音效同样先复用静默猎手。以后有柒群专属 FMOD 事件时替换这里。
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectSfx), MethodType.Getter)]
internal static class QiqunCharacterSelectSfxPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = "event:/sfx/characters/silent/silent_select";
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterTransitionSfx), MethodType.Getter)]
internal static class QiqunCharacterTransitionSfxPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = "event:/sfx/ui/wipe_silent";
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.AttackSfx), MethodType.Getter)]
internal static class QiqunAttackSfxPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = "event:/sfx/characters/silent/silent_attack";
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CastSfx), MethodType.Getter)]
internal static class QiqunCastSfxPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = "event:/sfx/characters/silent/silent_cast";
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.DeathSfx), MethodType.Getter)]
internal static class QiqunDeathSfxPatch
{
    [HarmonyPostfix]
    static void Postfix(CharacterModel __instance, ref string __result)
    {
        if (__instance is Qiqun)
            __result = "event:/sfx/characters/silent/silent_die";
    }
}

// 多人模式手势贴图也会按角色 ID 拼路径；这里统一回落到静默猎手贴图。
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconTexture), MethodType.Getter)]
internal static class QiqunIconTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.IconTexture);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconOutlineTexture), MethodType.Getter)]
internal static class QiqunIconOutlineTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.IconOutlineTexture);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmPointingTexture), MethodType.Getter)]
internal static class QiqunArmPointingTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.ArmPointing);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmRockTexture), MethodType.Getter)]
internal static class QiqunArmRockTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.ArmRock);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmPaperTexture), MethodType.Getter)]
internal static class QiqunArmPaperTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.ArmPaper);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmScissorsTexture), MethodType.Getter)]
internal static class QiqunArmScissorsTexturePatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetTexture2D(QiqunSilentAssetPaths.ArmScissors);
        return false;
    }
}

/// <summary>
/// 顶栏角色图标使用 PackedScene，不是普通贴图，所以需要单独拦截 Icon getter。
/// </summary>
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.Icon), MethodType.Getter)]
internal static class QiqunIconPatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref Control __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetScene(QiqunSilentAssetPaths.Icon)
            .Instantiate<Control>();
        return false;
    }
}

/// <summary>
/// 战斗中的角色模型。柒群暂时使用静默猎手模型，保证可以进入战斗流程。
/// </summary>
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CreateVisuals))]
internal static class QiqunCreateVisualsPatch
{
    [HarmonyPrefix]
    static bool Prefix(CharacterModel __instance, ref NCreatureVisuals __result)
    {
        if (__instance is not Qiqun)
            return true;

        __result = PreloadManager.Cache.GetScene(QiqunSilentAssetPaths.Visuals)
            .Instantiate<NCreatureVisuals>();
        return false;
    }
}

/// <summary>
/// 旧功能保留：让原模组已经改过的“仆从俯冲”在原效果结算后额外抽 1 张牌。
/// 这里暂时不和柒群逻辑混在一起，后续如果这个功能不再需要，可以单独删除。
/// </summary>
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
            // Harmony 的 async void 后置补丁不能被调用方 await；这里吞掉异常，避免异步回调把游戏流程打断。
        }
    }
}

/// <summary>
/// 旧功能的描述补丁：给“仆从俯冲”追加“抽 1 张牌”的说明。
/// </summary>
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
