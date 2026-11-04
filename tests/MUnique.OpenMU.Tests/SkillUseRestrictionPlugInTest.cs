// <copyright file="SkillUseRestrictionPlugInTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.ComponentModel;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.Pathfinding;
using MagicEffectDefinition = MUnique.OpenMU.Persistence.BasicModel.MagicEffectDefinition;
using PowerUpDefinition = MUnique.OpenMU.Persistence.BasicModel.PowerUpDefinition;
using PowerUpDefinitionValue = MUnique.OpenMU.Persistence.BasicModel.PowerUpDefinitionValue;
using Skill = MUnique.OpenMU.Persistence.BasicModel.Skill;

/// <summary>
/// Tests for the <see cref="ISkillUseRestrictionPlugIn"/>, which is checked by the skill actions.
/// </summary>
[TestFixture]
public class SkillUseRestrictionPlugInTest
{
    private const short HealSkillNumber = 26;

    /// <summary>
    /// Tests that a targeted skill is used when no plugin prevents it, and not used when a plugin prevents it.
    /// </summary>
    /// <param name="prevent">If the plugin prevents the skill.</param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TargetedSkillIsPreventedByPlugInAsync(bool prevent)
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var healer = await CreatePlayerAsync(gameContext, new Point(100, 100)).ConfigureAwait(false);
        var target = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        await LearnHealAsync(healer, 50).ConfigureAwait(false);
        var healthBefore = target.Attributes![Stats.MaximumHealth] - 100;
        target.Attributes[Stats.CurrentHealth] = healthBefore;
        var plugIn = new PreventingPlugIn(prevent ? HealSkillNumber : (short)-1);
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<ISkillUseRestrictionPlugIn>(plugIn);

        await new TargetedSkillDefaultPlugin().PerformSkillAsync(healer, target, (ushort)HealSkillNumber).ConfigureAwait(false);

        Assert.That(plugIn.Checks, Is.EqualTo(new[] { (healer, (short)HealSkillNumber) }));
        Assert.That(target.Attributes[Stats.CurrentHealth], prevent ? Is.EqualTo(healthBefore) : Is.EqualTo(healthBefore + 50));
    }

    /// <summary>
    /// Tests that an area skill isn't used (no mana is consumed) when a plugin prevents it.
    /// </summary>
    [Test]
    public async Task AreaSkillIsPreventedByPlugInAsync()
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var player = await CreatePlayerAsync(gameContext, new Point(100, 100)).ConfigureAwait(false);
        var skill = new Skill { Number = 24, Name = "Triple Shot", Range = 6, SkillType = SkillType.AreaSkillAutomaticHits };
        await player.SkillList!.AddLearnedSkillAsync(skill).ConfigureAwait(false);
        var plugIn = new PreventingPlugIn(24);
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<ISkillUseRestrictionPlugIn>(plugIn);

        await new AreaSkillAttackAction().AttackAsync(player, 0, 24, new Point(102, 100), 0).ConfigureAwait(false);

        Assert.That(plugIn.Checks, Is.EqualTo(new[] { (player, (short)24) }));
        Assert.That(player.IsSkillUsePreventedByPlugIns(skill), Is.True);
    }

    private static async ValueTask LearnHealAsync(Player player, float healedHealth)
    {
        var powerUp = new PowerUpDefinition
        {
            TargetAttribute = Stats.CurrentHealth,
            Boost = new PowerUpDefinitionValue { Value = healedHealth },
        };
        var effect = new MagicEffectDefinition { Number = 1 };
        effect.PowerUpDefinitions.Add(powerUp);
        var skill = new Skill
        {
            Number = HealSkillNumber,
            Name = "Heal",
            Range = 6,
            SkillType = SkillType.Regeneration,
            Target = SkillTarget.Explicit,
            MagicEffectDef = effect,
        };

        await player.SkillList!.AddLearnedSkillAsync(skill).ConfigureAwait(false);
    }

    private static async ValueTask<Player> CreatePlayerAsync(IGameContext gameContext, Point position)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(gameContext).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false);
        player.IsAlive = true;
        player.Position = position;
        await player.CurrentMap!.AddAsync(player).ConfigureAwait(false);
        return player;
    }

    [Guid("A1F4E7C2-5B39-4D6E-8F12-7C3B9E0D4A65")]
    private sealed class PreventingPlugIn : ISkillUseRestrictionPlugIn
    {
        private readonly short _preventedSkill;

        public PreventingPlugIn(short preventedSkill)
        {
            this._preventedSkill = preventedSkill;
        }

        public List<(Player Player, short Skill)> Checks { get; } = new();

        public void CheckSkillUse(Player player, DataModel.Configuration.Skill skill, CancelEventArgs eventArgs)
        {
            this.Checks.Add((player, skill.Number));
            eventArgs.Cancel |= skill.Number == this._preventedSkill;
        }
    }
}
