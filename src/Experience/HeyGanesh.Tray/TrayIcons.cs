namespace HeyGanesh.Tray;

/// <summary>
/// The Hey Ganesh mark (DEC-056): one ink stroke from ear to trunk, the bindu and a crown arc on kraft paper. The
/// same <c>heyganesh.ico</c> is the exe's icon (ApplicationIcon, which the installer's shortcut and Apps &amp; features
/// entry show) and an embedded resource, read here for the tray and the key screen. The source drawing is
/// <c>assets/brand/heyganesh.svg</c>.
/// </summary>
internal static class TrayIcons
{
    private const string Resource = "HeyGanesh.Tray.heyganesh.ico";

    /// <summary>The icon at the size Windows asks for (the ICO holds 16–256 px, so no size is scaled from far away).</summary>
    public static Icon Load(Size size)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the embedded {Resource} is missing from the build");
        return new Icon(stream, size);
    }
}
