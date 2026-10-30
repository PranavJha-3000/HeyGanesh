using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.PartLibrary;

/// <summary>
/// The shipped models (DEC-029). v1 is three: one per kind the library must handle — mechanical and
/// computed (engine), molecular and computed (DNA), schematic (pump). None is <c>Reviewed</c> yet:
/// a person reads the PartLibrarySmoke --dump first, and the owner records that in DECISIONS.
/// </summary>
public static class ModelLibrary
{
    public static IReadOnlyList<LibraryModel> V1 { get; } = new[]
    {
        FourStrokeEngine.Build(),
        DnaHelix.Build(),
        SodiumPotassiumPump.Build(),
    };

    public static LibraryModel? Find(string id) => V1.FirstOrDefault(m => m.Id == id);

    /// <summary>The names the scene3d contract checks lessons against (DEC-028): model, part and animation ids only.</summary>
    public static ModelCatalog ToCatalog() => new(V1.Select(m => new CatalogModel(
        m.Id,
        m.Parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal),
        m.Animations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal))));
}
