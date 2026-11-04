// <copyright file="ExitGateExtensionsTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tests the placement-specific landing-point contract of exit gates.
/// </summary>
[TestFixture]
public class ExitGateExtensionsTest
{
    /// <summary>
    /// The possible points must preserve the existing random-placement bounds without defining how other
    /// consumers interpret the gate rectangle.
    /// </summary>
    [Test]
    public void PossibleLandingPointsMatchRandomPlacementBounds()
    {
        var gate = CreateGate(10, 20, 12, 22);
        var expected = new[]
        {
            new Point(10, 20),
            new Point(10, 21),
            new Point(11, 20),
            new Point(11, 21),
        };

        Assert.That(gate.GetPossibleLandingPoints(), Is.EquivalentTo(expected));
    }

    /// <summary>
    /// Equal bounds describe one landing coordinate, including at the edge of the byte coordinate range.
    /// </summary>
    [Test]
    public void PossibleLandingPointsSupportSinglePointGateAtMapEdge()
    {
        var gate = CreateGate(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        Assert.That(gate.GetPossibleLandingPoints(), Is.EqualTo(new[] { new Point(byte.MaxValue, byte.MaxValue) }));
        Assert.That(gate.GetRandomPoint(), Is.EqualTo(new Point(byte.MaxValue, byte.MaxValue)));
    }

    /// <summary>
    /// Invalid reversed bounds must not be silently normalized into a different landing area.
    /// </summary>
    [Test]
    public void PossibleLandingPointsRejectReversedBounds()
    {
        var gate = CreateGate(12, 22, 10, 20);

        Assert.That(() => gate.GetPossibleLandingPoints().ToList(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Random gate placement and possible-point enumeration must use the same coordinate domain.
    /// </summary>
    [Test]
    public void RandomPointAlwaysBelongsToPossibleLandingPoints()
    {
        var gate = CreateGate(10, 20, 13, 23);
        var possiblePoints = gate.GetPossibleLandingPoints().ToHashSet();

        Assert.That(Enumerable.Range(0, 100).Select(_ => gate.GetRandomPoint()), Is.All.Matches<Point>(possiblePoints.Contains));
    }

    /// <summary>
    /// With a terrain, the random point is always a walkable tile of the gate, even if most of the gate is blocked
    /// (like the fountain in the middle of the Lorencia safezone).
    /// </summary>
    [Test]
    public void RandomPointWithTerrainIsAlwaysWalkable()
    {
        var gate = CreateGate(10, 20, 14, 24);
        var terrain = CreateOpenTerrain();
        foreach (var point in gate.GetPossibleLandingPoints())
        {
            terrain.ApplyTerrainAttribute(point.X, point.Y, TerrainAttributeType.Blocked, true);
        }

        var walkable = new[] { new Point(11, 21), new Point(13, 23) };
        foreach (var point in walkable)
        {
            terrain.ApplyTerrainAttribute(point.X, point.Y, TerrainAttributeType.Blocked, false);
        }

        var points = Enumerable.Range(0, 200).Select(_ => gate.GetRandomPoint(terrain)).ToList();

        Assert.That(points, Is.All.Matches<Point>(walkable.Contains));
        Assert.That(points.Distinct(), Is.EquivalentTo(walkable));
    }

    /// <summary>
    /// A gate without any walkable tile keeps the old placement instead of failing.
    /// </summary>
    [Test]
    public void RandomPointWithTerrainFallsBackWhenNothingIsWalkable()
    {
        var gate = CreateGate(10, 20, 12, 22);
        var terrain = CreateOpenTerrain();
        foreach (var point in gate.GetPossibleLandingPoints())
        {
            terrain.ApplyTerrainAttribute(point.X, point.Y, TerrainAttributeType.Blocked, true);
        }

        Assert.That(gate.GetRandomPoint(terrain), Is.AnyOf(gate.GetPossibleLandingPoints().Cast<object>().ToArray()));
    }

    /// <summary>
    /// The real safezone gate of Lorencia contains the blocked fountain tile (140, 127), where respawned players got stuck.
    /// </summary>
    [Test]
    public void LorenciaSafezoneRespawnAvoidsTheFountain()
    {
        var gate = CreateGate(133, 118, 151, 135);
        var terrain = CreateOpenTerrain();
        terrain.ApplyTerrainAttribute(140, 127, TerrainAttributeType.Blocked, true);

        Assert.That(gate.GetPossibleLandingPoints(), Does.Contain(new Point(140, 127)));
        Assert.That(Enumerable.Range(0, 2000).Select(_ => gate.GetRandomPoint(terrain)), Has.None.EqualTo(new Point(140, 127)));
    }

    private static GameMapTerrain CreateOpenTerrain()
    {
        // A complete *.att file (3 bytes header, 256 x 256 tiles) without any attribute: everything is walkable.
        return new GameMapTerrain(new byte[3 + (256 * 256)]);
    }

    private static DataModel.Configuration.ExitGate CreateGate(byte x1, byte y1, byte x2, byte y2)
    {
        return new MUnique.OpenMU.Persistence.BasicModel.ExitGate
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
        };
    }
}
