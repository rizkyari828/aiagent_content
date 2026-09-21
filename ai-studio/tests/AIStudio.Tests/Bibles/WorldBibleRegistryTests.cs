using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class WorldBibleRegistryTests
{
    [Fact]
    public void ListsRegisteredWorldsByIdThenVersion()
    {
        var registry = new WorldBibleRegistry(
        [
            BibleTestSupport.World("office", version: 2),
            BibleTestSupport.World("alley", version: 1),
            BibleTestSupport.World("office", version: 1)
        ]);

        Assert.Equal(
            ["alley", "office", "office"],
            registry.Worlds.Select(world => world.Id.Value));
        Assert.Equal([1, 1, 2], registry.Worlds.Select(world => world.Version.Value));
    }

    [Fact]
    public void RegisterThenRetrieveByStableIdAndVersion()
    {
        var registry = new WorldBibleRegistry();
        registry.Register(BibleTestSupport.World("rio-bedroom"));

        Assert.True(registry.Contains(new WorldBibleId("rio-bedroom"), new WorldBibleVersion(1)));
        Assert.Equal(
            "rio-bedroom",
            registry.Get(new WorldBibleId("rio-bedroom"), new WorldBibleVersion(1)).Id.Value);
        Assert.Equal("rio-bedroom", registry.GetLatest(new WorldBibleId("rio-bedroom")).Id.Value);
    }

    [Fact]
    public void DuplicateIdAndVersionRegistrationIsRejected()
    {
        var registry = new WorldBibleRegistry([BibleTestSupport.World("rio-bedroom")]);

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(BibleTestSupport.World("rio-bedroom")));
    }

    [Fact]
    public void InvalidWorldRegistrationIsRejected()
    {
        var registry = new WorldBibleRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new WorldBible()));
    }

    [Fact]
    public void MissingWorldThrowsAndTryGetReturnsFalse()
    {
        var registry = new WorldBibleRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.GetLatest(new WorldBibleId("missing")));
        Assert.False(registry.TryGetLatest(new WorldBibleId("missing"), out _));
        Assert.False(registry.TryGet(new WorldBibleId("missing"), new WorldBibleVersion(1), out _));
    }
}
