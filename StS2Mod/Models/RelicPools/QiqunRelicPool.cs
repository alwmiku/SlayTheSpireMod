using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Unlocks;
using StS2Mod.Models.Relics;

namespace StS2Mod.Models.RelicPools;

/// <summary>
/// 柒群专属遗物池。
/// 目前只有起始遗物“识牌”是柒群自有遗物；普通掉落仍借用静默猎手遗物池，方便原型阶段先跑机制。
/// </summary>
public sealed class QiqunRelicPool : RelicPoolModel
{
    // 先沿用静默猎手绿色能量主题，和柒群当前卡框/能量图标保持一致。
    public override string EnergyColorName => "silent";

    public override Color LabOutlineColor => StsColors.green;

    protected override IEnumerable<RelicModel> GenerateAllRelics()
    {
        // 把起始遗物也注册到池里，避免遗物 UI 访问 RelicModel.Pool 时找不到归属池。
        return new RelicModel[]
        {
            ModelDb.Relic<ShiPai>()
        };
    }

    public override IEnumerable<RelicModel> GetUnlockedRelics(UnlockState unlockState)
    {
        // Starter 遗物不会进入普通掉落；其他遗物暂时沿用静默猎手池。
        return ModelDb.RelicPool<SilentRelicPool>().GetUnlockedRelics(unlockState)
            .Concat(base.GetUnlockedRelics(unlockState))
            .Where(relic => relic.Rarity != RelicRarity.Starter);
    }
}
