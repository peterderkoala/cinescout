namespace cinescout.web.Client.Shared;

/// <summary>
/// §4.1's <c>variant</c> prop. Not server data (issue #77's resolution: "variant is a rendering
/// choice the calling page/section makes"), so it lives here rather than on
/// <c>FilmPerformanceCardModel</c>.
/// </summary>
public enum FilmPerformanceCardVariant
{
    Compact,
    Featured,
}
