// <copyright file="NpcTargetSelectionPlugInTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.Pathfinding;
using MonsterAttribute = MUnique.OpenMU.Persistence.BasicModel.MonsterAttribute;
using MonsterDefinition = MUnique.OpenMU.Persistence.BasicModel.MonsterDefinition;

/// <summary>
/// Tests for the <see cref="INpcTargetSelectionPlugIn"/>, which is called by the <see cref="BasicMonsterIntelligence"/>.
/// </summary>
[TestFixture]
public class NpcTargetSelectionPlugInTest
{
    private static readonly Point MonsterPosition = new(100, 100);

    /// <summary>
    /// Tests that the monster selects the nearest player without a plugin.
    /// </summary>
    [Test]
    public async Task SelectsNearestTargetWithoutPlugInAsync()
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var intelligence = await CreateMonsterAsync(gameContext).ConfigureAwait(false);
        var near = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        _ = await CreatePlayerAsync(gameContext, new Point(106, 100)).ConfigureAwait(false);

        Assert.That(await intelligence.ResolveTargetAsync().ConfigureAwait(false), Is.SameAs(near));
    }

    /// <summary>
    /// Tests that a plugin can select another valid target than the nearest, and that it gets the default target.
    /// </summary>
    [Test]
    public async Task PlugInSelectsValidTargetAsync()
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var intelligence = await CreateMonsterAsync(gameContext).ConfigureAwait(false);
        var near = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        var far = await CreatePlayerAsync(gameContext, new Point(106, 100)).ConfigureAwait(false);
        var plugIn = new SelectingPlugIn(far);
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<INpcTargetSelectionPlugIn>(plugIn);

        var target = await intelligence.ResolveTargetAsync().ConfigureAwait(false);

        Assert.That(target, Is.SameAs(far));
        Assert.That(plugIn.LastArgs?.DefaultTarget, Is.SameAs(near));
        Assert.That(plugIn.LastArgs?.IsValidTarget(far), Is.True);
    }

    /// <summary>
    /// Tests that the monster keeps the default target when the plugin selects one outside of its view range.
    /// </summary>
    [Test]
    public async Task InvalidTargetOfPlugInIsIgnoredAsync()
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var intelligence = await CreateMonsterAsync(gameContext).ConfigureAwait(false);
        var near = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        var outOfRange = await CreatePlayerAsync(gameContext, new Point(130, 100)).ConfigureAwait(false);
        var plugIn = new SelectingPlugIn(outOfRange);
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<INpcTargetSelectionPlugIn>(plugIn);

        var target = await intelligence.ResolveTargetAsync().ConfigureAwait(false);

        Assert.That(target, Is.SameAs(near));
        Assert.That(plugIn.LastArgs?.IsValidTarget(outOfRange), Is.False);
    }

    /// <summary>
    /// Tests that a dead player is not a valid target.
    /// </summary>
    [Test]
    public async Task DeadPlayerIsNotValidAsync()
    {
        var gameContext = GameContextTestHelper.CreateGameContext();
        var intelligence = await CreateMonsterAsync(gameContext).ConfigureAwait(false);
        var near = await CreatePlayerAsync(gameContext, new Point(102, 100)).ConfigureAwait(false);
        var dead = await CreatePlayerAsync(gameContext, new Point(104, 100)).ConfigureAwait(false);
        dead.IsAlive = false;
        var plugIn = new SelectingPlugIn(dead);
        gameContext.PlugInManager.RegisterPlugInAtPlugInPoint<INpcTargetSelectionPlugIn>(plugIn);

        Assert.That(await intelligence.ResolveTargetAsync().ConfigureAwait(false), Is.SameAs(near));
    }

    private static async ValueTask<BasicMonsterIntelligence> CreateMonsterAsync(IGameContext gameContext)
    {
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);
        var monsterDefinition = new MonsterDefinition { Id = Guid.NewGuid(), Number = 3, ObjectKind = NpcObjectKind.Monster, AttackRange = 1, ViewRange = 10, AttackDelay = TimeSpan.FromHours(1) };
        monsterDefinition.Attributes.Add(new MonsterAttribute { AttributeDefinition = Stats.MaximumHealth, Value = 100 });
        var spawnArea = new MonsterSpawnArea
        {
            MonsterDefinition = monsterDefinition,
            GameMap = map!.Definition,
            X1 = MonsterPosition.X,
            Y1 = MonsterPosition.Y,
            X2 = MonsterPosition.X,
            Y2 = MonsterPosition.Y,
            Quantity = 1,
        };

        var intelligence = new BasicMonsterIntelligence();
        var monster = new Monster(spawnArea, monsterDefinition, map, NullDropGenerator.Instance, intelligence, gameContext.PlugInManager, gameContext.PathFinderPool);
        monster.Initialize();
        await map.AddAsync(monster).ConfigureAwait(false);
        return intelligence;
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

    [Guid("9A41F3D2-5E68-4C1B-B7A0-3D82E6F1C495")]
    private sealed class SelectingPlugIn : INpcTargetSelectionPlugIn
    {
        private readonly IAttackable _target;

        public SelectingPlugIn(IAttackable target)
        {
            this._target = target;
        }

        public NpcTargetSelectionArgs? LastArgs { get; private set; }

        public ValueTask SelectTargetAsync(Monster monster, NpcTargetSelectionArgs args)
        {
            this.LastArgs = args;
            args.Target = this._target;
            return ValueTask.CompletedTask;
        }
    }
}
