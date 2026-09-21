using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class CharacterBibleRegistryTests
{
    [Fact]
    public void ListsRegisteredCharactersByIdThenVersion()
    {
        var registry = new CharacterBibleRegistry(
        [
            BibleTestSupport.Character("rio", version: 2),
            BibleTestSupport.Character("alex", version: 1),
            BibleTestSupport.Character("rio", version: 1)
        ]);

        Assert.Equal(
            ["alex", "rio", "rio"],
            registry.Characters.Select(character => character.Id.Value));
        Assert.Equal([1, 1, 2], registry.Characters.Select(character => character.Version.Value));
    }

    [Fact]
    public void RegisterThenRetrieveByStableIdAndVersion()
    {
        var registry = new CharacterBibleRegistry();
        registry.Register(BibleTestSupport.Character("rio"));

        Assert.True(registry.Contains(new CharacterBibleId("rio"), new CharacterBibleVersion(1)));
        Assert.Equal("rio", registry.Get(new CharacterBibleId("rio"), new CharacterBibleVersion(1)).Id.Value);
        Assert.Equal("rio", registry.GetLatest(new CharacterBibleId("rio")).Id.Value);
    }

    [Fact]
    public void GetLatestReturnsHighestVersionForTheSameLogicalCharacter()
    {
        var registry = new CharacterBibleRegistry(
        [
            BibleTestSupport.Character("rio", version: 1),
            BibleTestSupport.Character("rio", version: 2)
        ]);

        Assert.Equal(2, registry.GetLatest(new CharacterBibleId("rio")).Version.Value);
        Assert.Equal(1, registry.Get(new CharacterBibleId("rio"), new CharacterBibleVersion(1)).Version.Value);
    }

    [Fact]
    public void DuplicateIdAndVersionRegistrationIsRejected()
    {
        var registry = new CharacterBibleRegistry([BibleTestSupport.Character("rio")]);

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(BibleTestSupport.Character("rio")));
    }

    [Fact]
    public void InvalidCharacterRegistrationIsRejected()
    {
        var registry = new CharacterBibleRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new CharacterBible()));
    }

    [Fact]
    public void MissingCharacterThrowsAndTryGetReturnsFalse()
    {
        var registry = new CharacterBibleRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.GetLatest(new CharacterBibleId("missing")));
        Assert.False(registry.TryGetLatest(new CharacterBibleId("missing"), out _));
        Assert.False(registry.TryGet(new CharacterBibleId("missing"), new CharacterBibleVersion(1), out _));
    }
}
