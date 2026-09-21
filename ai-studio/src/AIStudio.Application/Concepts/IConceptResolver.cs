namespace AIStudio.Application.Concepts;

/// <summary>
/// Resolves a valid concept into a deterministic production plan by looking up its
/// referenced recipe and delegating to the recipe resolver. It never executes a
/// provider and never loads code from concept data.
/// </summary>
public interface IConceptResolver
{
    ConceptResolution Resolve(ConceptManifest concept);
}
