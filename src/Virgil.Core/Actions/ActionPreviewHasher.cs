using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public static class ActionPreviewHasher
{
    // Length-prefixed fields avoid collisions caused by separators inside user text.
    // Enumeration values use invariant numbers; lists retain their supplied order.
    public static string Hash(ActionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var canonical = new StringBuilder();
        void Field(string value) => canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(value);
        void Number(int value) => Field(value.ToString(CultureInfo.InvariantCulture));
        void List(IReadOnlyList<string> values)
        {
            Number(values.Count);
            foreach (var value in values) Field(value);
        }

        Field(preview.ActionId);
        Field(preview.Title);
        Number((int)preview.RiskLevel);
        List(preview.Targets);
        Number(preview.Parameters.Count);
        foreach (var parameter in preview.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Field(parameter.Key);
            Field(parameter.Value);
        }
        List(preview.Effects);
        Number(preview.RequiresAdmin ? 1 : 0);
        Number(preview.RebootRequired ? 1 : 0);
        Number((int)preview.Reversibility);
        List(preview.RequiredBackups);
        // UTF-16 code units preserve even unpaired surrogates rather than replacing them.
        var bytes = new byte[canonical.Length * 2];
        for (var i = 0; i < canonical.Length; i++)
        {
            bytes[2 * i] = (byte)canonical[i];
            bytes[2 * i + 1] = (byte)(canonical[i] >> 8);
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
