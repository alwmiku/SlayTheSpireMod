using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using StS2Mod.Models.Cards;

namespace StS2Mod.Models.CardPools;

/// <summary>
/// 柒群专属牌池。
/// 目前只注册两张原型牌，后续新增柒群牌都应放进 GenerateAllCards。
/// </summary>
public sealed class QiqunCardPool : CardPoolModel
{
    // 牌池内部名。注意：展示文本不从这里来，而是走 localization。
    public override string Title => "qiqun";

    // 先复用静默猎手的绿色能量图标和牌框材质。
    public override string EnergyColorName => "silent";

    public override string CardFrameMaterialPath => "card_frame_green";

    // 牌库列表中卡牌条目的颜色。
    public override Color DeckEntryCardColor => new("3CB371");

    public override Color EnergyOutlineColor => new("205C3A");

    // 柒群牌不是无色牌，应当作为角色色牌出现。
    public override bool IsColorless => false;

    protected override CardModel[] GenerateAllCards()
    {
        // 新增柒群卡牌时，需要在这里显式注册，否则奖励池里不会出现。
        return new CardModel[]
        {
            ModelDb.Card<QiqunDrawOne>(),
            ModelDb.Card<QiqunShuffle>()
        };
    }
}
