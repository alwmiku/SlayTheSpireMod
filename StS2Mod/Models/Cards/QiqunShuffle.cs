using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using StS2Mod.Models.CardPools;

namespace StS2Mod.Models.Cards;

/// <summary>
/// 柒群原型牌：理牌。
/// 用一张主动牌暴露“洗牌”动作，方便玩家在需要时把抽牌堆和弃牌堆重新组织。
/// </summary>
public sealed class QiqunShuffle : CardModel
{
    // 暂无正式卡图，使用缺失卡图占位。
    public override string PortraitPath => MissingPortraitPath;

    public override string BetaPortraitPath => MissingPortraitPath;

    // 牌面视觉跟随柒群牌池。
    public override CardPoolModel VisualCardPool => ModelDb.CardPool<QiqunCardPool>();

    public QiqunShuffle()
        : base(0, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 先播一次施法动作，让主动洗牌不显得像无反馈的后台操作。
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        // 原版 Shuffle 会把抽牌堆和弃牌堆合并、洗乱，然后放回抽牌堆。
        await CardPileCmd.Shuffle(choiceContext, Owner);
    }
}
