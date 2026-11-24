using NUnit.Framework;
using RingingLoot.Core;

public class GameMapTests
{
    [Test]
    public void FixedMap_HasExpectedSize_AndObjects()
    {
        var m = new GameMap(GameMap.FixedMapV1);
        Assert.AreEqual(25, m.Cols);
        Assert.AreEqual(18, m.Rows);
        Assert.AreEqual((15, 3), m.Start);
        Assert.AreEqual(3, m.Relics.Count);
        Assert.AreEqual(5, m.Oils.Count);
        Assert.AreEqual(7, m.Traps.Count);
    }

    [Test]
    public void Round1_HasNoTraps()
    {
        var m = new GameMap(GameMap.FixedMapV1, trapsEnabled: false);
        Assert.AreEqual(0, m.Traps.Count);
    }

    [Test]
    public void IsWall_OutOfBounds_IsWall()
    {
        var m = new GameMap(GameMap.FixedMapV1);
        Assert.IsTrue(m.IsWall(-1, 0));
        Assert.IsTrue(m.IsWall(0, 0));
        Assert.IsFalse(m.IsWall(3, 15));
    }

    [Test]
    public void CellCenter_FlipsY()
    {
        Assert.AreEqual((3.5f, -15.5f), GameMap.CellCenter(15, 3));
    }
}
