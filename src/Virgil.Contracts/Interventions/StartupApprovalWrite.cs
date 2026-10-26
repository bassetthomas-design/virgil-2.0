namespace Virgil.Contracts;

public enum StartupApprovalKind { Run, Run32, StartupFolder }
public sealed record StartupApprovalWrite(StartupApprovalKind Kind, string Name, string ExpectedHex, string NewHex)
{
    public bool IsValid() => Enum.IsDefined(Kind) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 255 &&
        Name.All(c => !char.IsControl(c) && c is not '\\' and not '/') && ValidBytes(ExpectedHex) && ValidBytes(NewHex);
    private static bool ValidBytes(string? value)
    {
        if (value is null || value.Length != 24 || !value.All(Uri.IsHexDigit)) return false;
        var bytes = Convert.FromHexString(value);
        return bytes[0] is 2 or 3 or 6 or 7 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0;
    }
}
