using System.Numerics;
using System.Text.RegularExpressions;

namespace Virgil.Core.Releases;

/// <summary>SemVer 2.0 : comparaison ordinale, sans limite numérique ; metadata sans priorité.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private const string Number = "(?:0|[1-9][0-9]*)";
    private static readonly Regex Pattern = new("^(" + Number + @")\.(" + Number + @")\.(" + Number
        + @")(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant);
    private readonly BigInteger[] _numbers;
    private readonly string[] _pre;
    public string Value { get; }
    private SemanticVersion(string value, Match match)
    {
        Value = value;
        _numbers = Enumerable.Range(1, 3).Select(i => BigInteger.Parse(match.Groups[i].Value,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        _pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
    }
    public static SemanticVersion? Parse(string? value)
    {
        if (value is null) return null;
        var match = Pattern.Match(value);
        return match.Success && match.Length == value.Length ? new SemanticVersion(value, match) : null;
    }
    // Les tags de distribution n'acceptent pas de metadata, comme Get-ReleaseVersion.ps1.
    public static SemanticVersion? ParseTag(string? tag) => tag is not null && tag.StartsWith('v')
        && !tag.Contains('+') ? Parse(tag[1..]) : null;
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 3; i++)
        {
            var result = _numbers[i].CompareTo(other._numbers[i]);
            if (result != 0) return result;
        }
        if (_pre.Length == 0 || other._pre.Length == 0)
            return (_pre.Length == 0 ? 1 : 0).CompareTo(other._pre.Length == 0 ? 1 : 0);
        for (var i = 0; i < Math.Min(_pre.Length, other._pre.Length); i++)
        {
            var aNumeric = BigInteger.TryParse(_pre[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var a);
            var bNumeric = BigInteger.TryParse(other._pre[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var b);
            var result = aNumeric && bNumeric ? a.CompareTo(b) : aNumeric ? -1 : bNumeric ? 1
                : StringComparer.Ordinal.Compare(_pre[i], other._pre[i]);
            if (result != 0) return result;
        }
        return _pre.Length.CompareTo(other._pre.Length);
    }
    public override string ToString() => Value;
}
