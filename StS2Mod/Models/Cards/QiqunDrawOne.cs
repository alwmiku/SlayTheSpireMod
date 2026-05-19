using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using StS2Mod.Models.CardPools;

namespace StS2Mod.Models.Cards;

/// <summary>
/// 柒群原型牌：抽丝。
/// 0 费抽 1 张牌；在手牌中时不占用手牌数量上限。
/// 如果它在弃牌堆，会在自己的回合抽牌前回到手牌。
/// </summary>
public sealed class QiqunDrawOne : CardModel
{
    // 使用动态变量是为了让描述里的“抽 1 张牌”和实际数值共用同一个来源，后续升级也更好改。
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new CardsVar(1) };

    // 还没有正式卡图，先使用游戏内置的缺失卡图，保证资源加载不会失败。
    public override string PortraitPath => MissingPortraitPath;

    public override string BetaPortraitPath => MissingPortraitPath;

    // 决定卡牌外观所属牌池，这里绑定到柒群牌池，使用绿色牌框和能量图标。
    public override CardPoolModel VisualCardPool => ModelDb.CardPool<QiqunCardPool>();

    public QiqunDrawOne()
        : base(0, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // “默认抽卡逻辑”的实例化：消耗 0，打出后抽 1 张牌。
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, CombatState combatState)
    {
        if (player != Owner || Pile?.Type != PileType.Discard)
            return;

        // 不再打出后立刻回手，而是在下一次我方回合开始抽牌前，从弃牌堆回到手牌。
        await CardPileCmd.Add(this, PileType.Hand);
    }
}
