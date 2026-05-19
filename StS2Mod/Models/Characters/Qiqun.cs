using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using StS2Mod.Models.CardPools;
using StS2Mod.Models.Cards;
using StS2Mod.Models.RelicPools;
using StS2Mod.Models.Relics;

namespace StS2Mod.Models.Characters;

/// <summary>
/// 柒群角色原型。
/// 当前目标是先验证“抽牌、回手、主动洗牌”的核心机制，因此数值、美术、遗物池都先复用静默猎手。
/// </summary>
public sealed class Qiqun : CharacterModel
{
    // 先使用中性代词，具体性别和本地化称呼后续可以按角色设定再调整。
    public override CharacterGender Gender => CharacterGender.Neutral;

    // 原型阶段默认解锁，方便直接在角色选择界面测试。
    protected override CharacterModel? UnlocksAfterRunAs => null;

    public override Color NameColor => new("3CB371");

    public override int StartingHp => 70;

    public override int StartingGold => 99;

    public override CardPoolModel CardPool => ModelDb.CardPool<QiqunCardPool>();

    // 遗物掉落暂时借用静默猎手，但通过柒群遗物池给“识牌”一个正式归属，避免遗物 UI 找不到 Pool。
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<QiqunRelicPool>();

    public override PotionPoolModel PotionPool => ModelDb.PotionPool<SilentPotionPool>();

    // 初始牌组：基础攻防复用静默猎手，额外塞入两张柒群机制牌用于开局验证。
    public override IEnumerable<CardModel> StartingDeck => new CardModel[]
    {
        ModelDb.Card<StrikeSilent>(),
        ModelDb.Card<StrikeSilent>(),
        ModelDb.Card<StrikeSilent>(),
        ModelDb.Card<StrikeSilent>(),
        ModelDb.Card<DefendSilent>(),
        ModelDb.Card<DefendSilent>(),
        ModelDb.Card<DefendSilent>(),
        ModelDb.Card<DefendSilent>(),
        ModelDb.Card<QiqunDrawOne>(),
        ModelDb.Card<QiqunShuffle>()
    };

    // 起始遗物改为柒群专属的“识牌”，让角色从第一场战斗起就进入手动抽牌/洗牌节奏。
    public override IReadOnlyList<RelicModel> StartingRelics => new[] { ModelDb.Relic<ShiPai>() };

    // 当前战斗模型复用静默猎手，所以攻击/施法延迟也沿用它的手感。
    public override float AttackAnimDelay => 0.15f;

    public override float CastAnimDelay => 0.25f;

    public override Color EnergyLabelOutlineColor => new("004F04FF");

    public override Color DialogueColor => new("284719");

    public override VfxColor SpeechBubbleColor => VfxColor.Swamp;

    public override Color MapDrawingColor => new("2F6729");

    public override Color RemoteTargetingLineColor => new("2EBD5EFF");

    public override Color RemoteTargetingLineOutline => new("004F04FF");

    // 角色选择和顶栏图标先指向静默猎手资源。更多资源路径兜底在 ModEntry 的 Harmony 补丁里。
    protected override string CharacterSelectIconPath => ImageHelper.GetImagePath("packed/character_select/char_select_silent.png");

    protected override string CharacterSelectLockedIconPath => ImageHelper.GetImagePath("packed/character_select/char_select_silent_locked.png");

    protected override string IconPath => SceneHelper.GetScenePath("ui/character_icons/silent_icon");

    // 建筑师事件需要角色提供一组攻击特效候选；这里沿用静默猎手风格。
    public override List<string> GetArchitectAttackVfx()
    {
        return new List<string>
        {
            "vfx/vfx_dagger_spray",
            "vfx/vfx_flying_slash",
            "vfx/vfx_dramatic_stab",
            "vfx/vfx_dagger_throw"
        };
    }
}
