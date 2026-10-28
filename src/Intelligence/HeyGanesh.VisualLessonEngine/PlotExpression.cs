using System.Globalization;

namespace HeyGanesh.VisualLessonEngine;

/// <summary>One sampled point of a plot, in the function's own units.</summary>
public sealed record PlotPoint(double X, double Y);

/// <summary>
/// A plot's y = f(x), parsed from the model's expression by a closed grammar and evaluated here.
/// This is FEATURE-007's "compute, don't recall": the model names the function, the app draws the
/// curve, so a plotted curve cannot be a hallucinated one — and because the only names are x, pi, e
/// and eight functions, an expression can never be code.
/// </summary>
/// <remarks>
/// Grammar: numbers, x, pi, e, + − * / and right-associative ^, unary minus (looser than ^, so
/// −2^2 = −4), parentheses, sin cos tan exp ln log(base 10) sqrt abs, and implicit multiplication
/// (6x, 3x^2, 2(x+1), 2sin(x)) because models write maths the way people do. Nesting is capped so
/// adversarial input cannot exhaust the stack.
/// </remarks>
public sealed class PlotExpression
{
    public const int MaxLength = 280;
    public const int MaxDepth = 64;

    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.Ordinal)
    {
        ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan, ["exp"] = Math.Exp,
        ["ln"] = Math.Log, ["log"] = Math.Log10, ["sqrt"] = Math.Sqrt, ["abs"] = Math.Abs,
    };

    private readonly Func<double, double> _f;

    private PlotExpression(Func<double, double> f) => _f = f;

    public double Evaluate(double x) => _f(x);

    public static bool TryParse(string text, out PlotExpression? expression, out string? error)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty expression";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = $"expression is {text.Length} chars (max {MaxLength})";
            return false;
        }

        var parser = new Parser(text);
        if (!parser.TryParse(out var f, out error))
        {
            return false;
        }

        expression = new PlotExpression(f!);
        return true;
    }

    /// <summary>
    /// <paramref name="count"/> evenly spaced samples over [XMin, XMax], ends exact. A non-finite y
    /// (an asymptote, ln of a negative) is a null — a gap the renderer must not join across.
    /// Empty when the expression does not parse (the validator has already refused such a plot).
    /// </summary>
    public static IReadOnlyList<PlotPoint?> Sample(PlotOp op, int count = 121)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        if (!TryParse(op.Expr, out var e, out _))
        {
            return Array.Empty<PlotPoint?>();
        }

        var points = new PlotPoint?[count];
        for (var i = 0; i < count; i++)
        {
            // (span * i) / (count - 1), not i * step: keeps both ends and the midpoint exact.
            var x = op.XMin + (op.XMax - op.XMin) * i / (count - 1);
            var y = e!.Evaluate(x);
            points[i] = double.IsFinite(y) ? new PlotPoint(x, y) : null;
        }

        return points;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _pos;
        private int _depth;

        public Parser(string s) => _s = s;

        public bool TryParse(out Func<double, double>? f, out string? error)
        {
            try
            {
                f = Expr();
                SkipSpace();
                if (_pos < _s.Length)
                {
                    throw new FormatException($"unexpected '{_s[_pos]}' at {_pos}");
                }

                error = null;
                return true;
            }
            catch (FormatException ex)
            {
                f = null;
                error = ex.Message;
                return false;
            }
        }

        // expr := term (('+' | '-') term)*
        private Func<double, double> Expr()
        {
            var left = Term();
            while (true)
            {
                SkipSpace();
                if (Eat('+')) { var l = left; var r = Term(); left = x => l(x) + r(x); }
                else if (Eat('-')) { var l = left; var r = Term(); left = x => l(x) - r(x); }
                else return left;
            }
        }

        // term := unary (('*' | '/') unary | <implicit> unary)*
        private Func<double, double> Term()
        {
            var left = Unary();
            while (true)
            {
                SkipSpace();
                if (Eat('*')) { var l = left; var r = Unary(); left = x => l(x) * r(x); }
                else if (Eat('/')) { var l = left; var r = Unary(); left = x => l(x) / r(x); }
                else if (StartsOperand()) { var l = left; var r = Unary(); left = x => l(x) * r(x); }
                else return left;
            }
        }

        // unary := '-' unary | power
        private Func<double, double> Unary()
        {
            Enter();
            try
            {
                SkipSpace();
                if (Eat('-'))
                {
                    var inner = Unary();
                    return x => -inner(x);
                }

                return Power();
            }
            finally
            {
                _depth--;
            }
        }

        // power := primary ('^' unary)?   — right-associative through unary
        private Func<double, double> Power()
        {
            var b = Primary();
            SkipSpace();
            if (!Eat('^'))
            {
                return b;
            }

            var p = Unary();
            return x => Math.Pow(b(x), p(x));
        }

        private Func<double, double> Primary()
        {
            Enter();
            try
            {
                SkipSpace();
                if (_pos >= _s.Length)
                {
                    throw new FormatException("expression ends early");
                }

                var c = _s[_pos];
                if (Eat('('))
                {
                    var inner = Expr();
                    SkipSpace();
                    if (!Eat(')'))
                    {
                        throw new FormatException($"missing ')' at {_pos}");
                    }

                    return inner;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    var start = _pos;
                    while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
                    if (!double.TryParse(_s.AsSpan(start, _pos - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
                    {
                        throw new FormatException($"bad number at {start}");
                    }

                    return _ => n;
                }

                if (char.IsAsciiLetter(c))
                {
                    var start = _pos;
                    // Names are read greedily but only the known ones exist; 'x' and the constants
                    // stop at one token so "xx" or "pix" are refused rather than guessed at.
                    while (_pos < _s.Length && char.IsAsciiLetter(_s[_pos])) _pos++;
                    var name = _s[start.._pos];
                    switch (name)
                    {
                        case "x": return x => x;
                        case "pi": return _ => Math.PI;
                        case "e": return _ => Math.E;
                    }

                    if (Functions.TryGetValue(name, out var fn))
                    {
                        SkipSpace();
                        if (!Eat('('))
                        {
                            throw new FormatException($"'{name}' needs parentheses");
                        }

                        var arg = Expr();
                        SkipSpace();
                        if (!Eat(')'))
                        {
                            throw new FormatException($"missing ')' after {name}(");
                        }

                        return x => fn(arg(x));
                    }

                    throw new FormatException($"unknown name '{name}'");
                }

                throw new FormatException($"unexpected '{c}' at {_pos}");
            }
            finally
            {
                _depth--;
            }
        }

        private bool StartsOperand()
        {
            if (_pos >= _s.Length) return false;
            var c = _s[_pos];
            return c == '(' || char.IsDigit(c) || c == '.' || char.IsAsciiLetter(c);
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
            {
                throw new FormatException($"nested deeper than {MaxDepth}");
            }
        }

        private bool Eat(char c)
        {
            if (_pos < _s.Length && _s[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private void SkipSpace()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }
    }
}
