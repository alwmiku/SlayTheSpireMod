using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using StS2Mod.Models.Cards;

namespace StS2Mod.Models.Relics;

/// <summary>
/// 柒群初始遗物：识牌。
/// 它把原版“每回合自动抽 5 张、抽牌堆空时自动洗牌”的节奏改成手动节奏：
/// 战斗开始先给 5 张不占手牌数的抽丝，之后玩家要靠抽丝和理牌自己管理抽牌顺序。
/// </summary>
public sealed class ShiPai : RelicModel
{
    // 识牌目前生成 5 张抽丝；用动态变量承接，方便后续直接改本地化里的数值展示。
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new CardsVar(5) };

    public override RelicRarity Rarity => RelicRarity.Starter;

    // 临时复用壶铃遗物图标，轮廓上更接近“秤砣/重物”。正式秤砣图标后续替换这里即可。
    protected override string IconBaseName => "girya";

    // 复用现有大图路径，避免新遗物在大预览里掉到缺失图。
    protected override string BigIconPath => ImageHelper.GetImagePath("relics/girya.png");

    public override async Task BeforeCombatStart()
    {
        var combatState = Owner.Creature.CombatState;
        if (combatState == null)
            return;

        Flash();

        // 在战斗开始时生成抽丝。随后 ModifyHandDraw 会把自动抽牌数压成 0，
        // 所以开局手牌只由这些抽丝承担，避免原版额外抽 5 张。
        var threads = Enumerable
            .Range(0, DynamicVars.Cards.IntValue)
            .Select(_ => combatState.CreateCard<QiqunDrawOne>(Owner))
            .ToList();

        await CardPileCmd.AddGeneratedCardsToCombat(threads, PileType.Hand, addedByPlayer: true);
    }

    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        // 识牌持有者不再走原版每回合自动抽牌。主动打出抽丝仍然调用普通抽牌流程。
        return player == Owner ? 0m : count;
    }
}
