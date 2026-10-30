using System.Text;

namespace HeyGanesh.LessonPlanner;

/// <summary>
/// Reads a VisualPlan's JSON as it streams in and hands back each complete beat the moment its closing
/// brace arrives (DEC-041). Pure and chunk-agnostic: the same text split anywhere yields the same beats.
/// </summary>
/// <remarks>
/// It does not parse JSON; it tracks just enough to cut it safely — object/array depth, whether it is
/// inside a string, and escapes — so a brace or bracket inside a "say" never ends a beat. The header is
/// everything in the top-level object before the <c>"beats"</c> key, so <see cref="PrefixJson"/> can
/// rebuild "header + the first k beats" as a complete plan for the existing parser and validator. A
/// model that writes <c>"beats"</c> before the surfaces still scans correctly; its prefixes just fail to
/// validate until the end, so nothing is released early.
/// </remarks>
public sealed class PlanStreamScanner
{
    private readonly StringBuilder _text = new();
    private readonly List<string> _beats = new();
    private int _scanned;
    private int _depth;
    private bool _inString;
    private bool _escaped;
    private int _objectStart = -1;
    private int _stringStart = -1;
    private string? _lastKey;
    private int _lastKeyStart = -1;
    private bool _awaitBeatsArray;
    private bool _inBeats;
    private int _beatStart = -1;
    private int _beatsKeyStart = -1;

    /// <summary>All text received so far.</summary>
    public string Text => _text.ToString();

    /// <summary>The complete beats found so far, as JSON object text, in order.</summary>
    public IReadOnlyList<string> Beats => _beats;

    /// <summary>The top-level object's text before the <c>"beats"</c> key, once that key has been seen.</summary>
    public string? Header { get; private set; }

    /// <summary>True once the beats array has closed.</summary>
    public bool BeatsClosed { get; private set; }

    /// <summary>Appends a chunk and returns the beats it completed (often none).</summary>
    public IReadOnlyList<string> Append(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _text.Append(chunk);
        var found = new List<string>();
        for (; _scanned < _text.Length; _scanned++)
        {
            var c = _text[_scanned];
            if (_inString)
            {
                if (_escaped)
                {
                    _escaped = false;
                }
                else if (c == '\\')
                {
                    _escaped = true;
                }
                else if (c == '"')
                {
                    _inString = false;
                    if (_depth == 1)
                    {
                        // A string directly in the top-level object: a key if a ':' follows.
                        _lastKey = _text.ToString(_stringStart + 1, _scanned - _stringStart - 1);
                        _lastKeyStart = _stringStart;
                    }
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    _inString = true;
                    _stringStart = _scanned;
                    break;
                case ':' when _depth == 1:
                    _awaitBeatsArray = _lastKey == "beats" && !_inBeats && !BeatsClosed;
                    if (_awaitBeatsArray)
                    {
                        _beatsKeyStart = _lastKeyStart;
                    }

                    _lastKey = null;
                    break;
                case '{' or '[':
                    if (_depth == 0 && c == '{' && _objectStart < 0)
                    {
                        // Anything before the first '{' (a stray fence or word) is not the plan.
                        _objectStart = _scanned;
                    }
                    else if (_depth == 1 && c == '[' && _awaitBeatsArray)
                    {
                        _inBeats = true;
                        _awaitBeatsArray = false;
                        Header = _text.ToString(_objectStart, _beatsKeyStart - _objectStart);
                    }
                    else if (_depth == 2 && c == '{' && _inBeats)
                    {
                        _beatStart = _scanned;
                    }

                    if (_objectStart >= 0)
                    {
                        _depth++;
                    }

                    break;
                case '}' or ']':
                    if (_objectStart < 0)
                    {
                        break;
                    }

                    _depth--;
                    if (_depth == 2 && c == '}' && _inBeats && _beatStart >= 0)
                    {
                        var beat = _text.ToString(_beatStart, _scanned - _beatStart + 1);
                        _beats.Add(beat);
                        found.Add(beat);
                        _beatStart = -1;
                    }
                    else if (_depth == 1 && c == ']' && _inBeats)
                    {
                        _inBeats = false;
                        BeatsClosed = true;
                    }

                    break;
                default:
                    if (_depth == 1 && !char.IsWhiteSpace(c) && c != ',')
                    {
                        // A value that is not a string (a number, true...) after a key: not "beats".
                        _awaitBeatsArray = false;
                    }

                    break;
            }
        }

        return found;
    }

    /// <summary>
    /// The header plus the first <paramref name="count"/> beats as one complete plan, for the parser
    /// and validator; null until the header is known. The header text ends just before the "beats" key,
    /// so it carries its own trailing comma when other keys came first.
    /// </summary>
    public string? PrefixJson(int count)
    {
        if (Header is null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _beats.Count);
        return Header + "\"beats\": [" + string.Join(",", _beats.Take(count)) + "]}";
    }
}
