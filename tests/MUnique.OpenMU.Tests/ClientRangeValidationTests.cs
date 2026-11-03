// <copyright file="ClientRangeValidationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Moq;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.ItemCrafting;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlayerActions;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tests that the distances and positions which a game client requests are validated: a modified client
/// must not hit, cast or talk from further away than the original game allows.
/// </summary>
[TestFixture]
public class ClientRangeValidationTests
{
    private static readonly Point StartPoint = new(100, 100);

    /// <summary>
    /// Tests that a hit without a skill is only performed when the target is within the melee range.
    /// </summary>
    /// <param name="distance">The distance between the player and the target.</param>
    /// <param name="isPerformed">If set to <c>true</c>, the hit is expected to be performed.</param>
    [TestCase(1, true)]
    [TestCase(HitAction.MaximumMeleeHitDistance, true)]
    [TestCase(HitAction.MaximumMeleeHitDistance + 1, false)]
    [TestCase(10, false)]
    public async ValueTask MeleeHitIsLimitedToItsRangeAsync(int distance, bool isPerformed)
    {
        var player = await this.CreatePlayerAsync().ConfigureAwait(false);
        var target = this.CreateTarget(player, new Point((byte)(StartPoint.X + distance), StartPoint.Y));

        await new HitAction().HitAsync(player, target.Object, 0, Direction.East).ConfigureAwait(false);

        target.Verify(t => t.AttackByAsync(player, null, false, It.IsAny<double>(), It.IsAny<bool?>()), isPerformed ? Times.Once() : Times.Never());
    }

    /// <summary>
    /// Tests that a hit with a ranged weapon (which consumes ammunition) reaches further than a melee hit, but not further than its range.
    /// </summary>
    /// <param name="distance">The distance between the player and the target.</param>
    /// <param name="isPerformed">If set to <c>true</c>, the hit is expected to be performed.</param>
    [TestCase(HitAction.MaximumRangedHitDistance, true)]
    [TestCase(HitAction.MaximumRangedHitDistance + 1, false)]
    public async ValueTask RangedHitIsLimitedToItsRangeAsync(int distance, bool isPerformed)
    {
        var player = await this.CreatePlayerAsync().ConfigureAwait(false);
        player.Attributes!.AddElement(new SimpleElement(1, AggregateType.AddRaw), Stats.AmmunitionConsumptionRate);
        var target = this.CreateTarget(player, new Point((byte)(StartPoint.X + distance), StartPoint.Y));

        await new HitAction().HitAsync(player, target.Object, 0, Direction.East).ConfigureAwait(false);

        target.Verify(t => t.AttackByAsync(player, null, false, It.IsAny<double>(), It.IsAny<bool?>()), isPerformed ? Times.Once() : Times.Never());
    }

    /// <summary>
    /// Tests that an area skill, which hits around the aimed point (like Flame), can't be aimed beyond its range.
    /// The targets are searched around the aimed point, so it would hit anywhere on the map otherwise.
    /// </summary>
    /// <param name="distance">The distance between the player and the aimed point.</param>
    /// <param name="isPerformed">If set to <c>true</c>, the skill is expected to be performed.</param>
    [TestCase(6, true)]
    [TestCase(8, true)]
    [TestCase(9, false)]
    [TestCase(60, false)]
    public async ValueTask AreaSkillAroundAimedPointIsLimitedToItsRangeAsync(int distance, bool isPerformed)
    {
        var player = await this.CreatePlayerAsync().ConfigureAwait(false);
        var skill = await this.LearnSkillAsync(player, 5, 6, SkillType.AreaSkillAutomaticHits).ConfigureAwait(false);
        skill.AreaSkillSettings = new AreaSkillSettings { UseTargetAreaFilter = true, TargetAreaDiameter = 2 };

        await new AreaSkillAttackAction().AttackAsync(player, 0, (ushort)skill.Number, new Point((byte)(StartPoint.X + distance), StartPoint.Y), 0).ConfigureAwait(false);

        Assert.That(player.Attributes![Stats.CurrentMana], Is.EqualTo(isPerformed ? 90 : 100));
    }

    /// <summary>
    /// Tests that an area skill around the caster still works with any aimed point, because the point doesn't extend its reach.
    /// </summary>
    [Test]
    public async ValueTask AreaSkillAroundCasterIgnoresTheAimedPointAsync()
    {
        var player = await this.CreatePlayerAsync().ConfigureAwait(false);
        var skill = await this.LearnSkillAsync(player, 9, 3, SkillType.AreaSkillAutomaticHits).ConfigureAwait(false);

        await new AreaSkillAttackAction().AttackAsync(player, 0, (ushort)skill.Number, new Point((byte)(StartPoint.X + 60), StartPoint.Y), 0).ConfigureAwait(false);

        Assert.That(player.Attributes![Stats.CurrentMana], Is.EqualTo(90));
    }

    /// <summary>
    /// Tests that the game client can only talk to an NPC which is next to the player.
    /// </summary>
    /// <param name="distance">The distance between the player and the NPC.</param>
    /// <param name="isOpened">If set to <c>true</c>, the NPC dialog is expected to be opened.</param>
    [TestCase(1, true)]
    [TestCase(TalkNpcAction.MaximumTalkDistance, true)]
    [TestCase(TalkNpcAction.MaximumTalkDistance + 1, false)]
    [TestCase(50, false)]
    public async ValueTask TalkingToNpcRequiresToStandNextToItAsync(int distance, bool isOpened)
    {
        var player = await this.CreatePlayerAsync().ConfigureAwait(false);
        var definition = new Mock<MonsterDefinition>();
        definition.SetupAllProperties();
        definition.Setup(d => d.ItemCraftings).Returns(new List<ItemCrafting>());
        definition.Object.NpcWindow = NpcWindow.ChaosMachine;
        var npc = new NonPlayerCharacter(null!, definition.Object, player.CurrentMap!)
        {
            Position = new Point(StartPoint.X, (byte)(StartPoint.Y + distance)),
        };

        await new TalkNpcAction().TalkToNearbyNpcAsync(player, npc).ConfigureAwait(false);

        Assert.That(player.OpenedNpc, isOpened ? Is.SameAs(npc) : Is.Null);
        Assert.That(player.PlayerState.CurrentState, Is.EqualTo(isOpened ? PlayerState.NpcDialogOpened : PlayerState.EnteredWorld));
    }

    private async ValueTask<Player> CreatePlayerAsync()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        player.Position = StartPoint;
        player.Attributes![Stats.CurrentMana] = 100;
        return player;
    }

    private Mock<IAttackable> CreateTarget(Player player, Point position)
    {
        var target = new Mock<IAttackable>();
        target.SetupGet(t => t.IsAlive).Returns(true);
        target.SetupGet(t => t.CurrentMap).Returns(player.CurrentMap);
        target.SetupGet(t => t.Position).Returns(position);
        return target;
    }

    private async ValueTask<Skill> LearnSkillAsync(Player player, short number, short range, SkillType skillType)
    {
        var skillMock = new Mock<Skill>();
        skillMock.SetupAllProperties();
        skillMock.Setup(s => s.Requirements).Returns(new List<AttributeRequirement>());
        skillMock.Setup(s => s.AttributeRelationships).Returns(new List<AttributeRelationship>());
        skillMock.Setup(s => s.ConsumeRequirements).Returns(new List<AttributeRequirement>
        {
            new() { Attribute = Stats.CurrentMana, MinimumValue = 10 },
        });
        var skill = skillMock.Object;
        skill.Number = number;
        skill.Range = range;
        skill.SkillType = skillType;
        await player.SkillList!.AddLearnedSkillAsync(skill).ConfigureAwait(false);
        return skill;
    }
}
