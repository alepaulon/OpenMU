// <copyright file="SupportiveSkillAppliedPlugInTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

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
/// Tests for the <see cref="ISupportiveSkillAppliedPlugIn"/>, which is called by the <see cref="TargetedSkillDefaultPlugin"/>.
/// </summary>
[TestFixture]
public class SupportiveSkillAppliedPlugInTest
{
    private const short HealSkillNumber = 26;

    /// <summary>
    /// Tests that the plugin gets the restored health of a regeneration skill, without the overhealing.
    /// </summary>
    /// <param name="missingHealth">The health which the target misses before the heal.</param>
    /// <param name="expectedRestoredHealth">The expected restored health.</param>
    [TestCase(100, 50)]
    [TestCase(20, 20)]
    [TestCase(0, 0)]
    public async Task RegenerationReportsRestoredHealthAsync(int missingHealth, int expectedRestoredHealth)
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var healer = await CreatePlayerAsync(gameContext, new Point(100, 100)).ConfigureAwait(false);
        var target = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        await LearnHealAsync(healer, 50).ConfigureAwait(false);
        target.Attributes![Stats.CurrentHealth] = target.Attributes[Stats.MaximumHealth] - missingHealth;
        var plugIn = new RecordingPlugIn();
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<ISupportiveSkillAppliedPlugIn>(plugIn);

        await new TargetedSkillDefaultPlugin().PerformSkillAsync(healer, target, (ushort)HealSkillNumber).ConfigureAwait(false);

        Assert.That(plugIn.Calls, Has.Count.EqualTo(1));
        Assert.That(plugIn.Calls[0].Player, Is.SameAs(healer));
        Assert.That(plugIn.Calls[0].Target, Is.SameAs(target));
        Assert.That(plugIn.Calls[0].Skill.Number, Is.EqualTo(HealSkillNumber));
        Assert.That(plugIn.Calls[0].RestoredHealth, Is.EqualTo(expectedRestoredHealth));
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

    [Guid("2D5C8E19-7B46-4A3F-8E21-C0F9D6A4B357")]
    private sealed class RecordingPlugIn : ISupportiveSkillAppliedPlugIn
    {
        public List<(Player Player, IAttackable Target, DataModel.Configuration.Skill Skill, float RestoredHealth)> Calls { get; } = new();

        public void SupportiveSkillApplied(Player player, IAttackable target, DataModel.Configuration.Skill skill, float restoredHealth)
        {
            this.Calls.Add((player, target, skill, restoredHealth));
        }
    }
}
