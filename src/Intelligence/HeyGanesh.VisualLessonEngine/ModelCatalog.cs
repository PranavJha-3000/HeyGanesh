using System.Text.RegularExpressions;

namespace HeyGanesh.VisualLessonEngine;

/// <summary>
/// One 3D model a lesson may place: its id, the part ids a label/highlight/camera may name, and the
/// animations it can play. Names only — geometry and descriptions belong to the part library that
/// builds the catalogue (FEATURE-007 Phase 5 step 3), not to the contract.
/// </summary>
public sealed record CatalogModel(string Id, IReadOnlySet<string> Parts, IReadOnlySet<string> Animations);

/// <summary>
/// The vetted 3D vocabulary the validator checks a scene against (DEC-028). Passing one in
/// <c>ValidationContext.Scene3d</c> also says "my canvas can draw 3D"; with none, scene3d is refused.
/// </summary>
/// <remarks>
/// Built by code, never from model output, so a malformed catalogue is a programming error and the
/// constructor throws. Ids follow the same pattern as lesson ids, which is what keeps a part
/// reference ("instance.part") unambiguous.
/// </remarks>
public sealed class ModelCatalog
{
    private static readonly Regex IdPattern = new(@"^[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, CatalogModel> _models;

    public ModelCatalog(IEnumerable<CatalogModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = new Dictionary<string, CatalogModel>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            ArgumentNullException.ThrowIfNull(model);
            Require(model.Id, "model id");
            foreach (var part in model.Parts)
            {
                Require(part, $"part id in '{model.Id}'");
            }

            foreach (var animation in model.Animations)
            {
                Require(animation, $"animation id in '{model.Id}'");
            }

            if (!_models.TryAdd(model.Id, model))
            {
                throw new ArgumentException($"model id '{model.Id}' appears twice", nameof(models));
            }
        }
    }

    /// <summary>No models: a host that can draw 3D but has nothing to show. Every model op is refused.</summary>
    public static ModelCatalog Empty { get; } = new(Array.Empty<CatalogModel>());

    public IReadOnlyCollection<CatalogModel> Models => _models.Values;

    public bool TryGet(string id, out CatalogModel model) => _models.TryGetValue(id, out model!);

    private static void Require(string id, string what)
    {
        if (id is null || !IdPattern.IsMatch(id))
        {
            throw new ArgumentException($"{what} '{id}' does not match {IdPattern}");
        }
    }
}
