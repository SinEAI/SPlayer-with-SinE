namespace SPlayer;

/// <summary>How this process was started. A media path means a player window and no shell.</summary>
internal sealed class LaunchRequest
{
    public bool PlayerOnly { get; init; }
    public string[] Media { get; init; } = [];
    public string? SmokeFile { get; init; }
    public string? SmokePng { get; init; }
    public string? SmokeLog { get; init; }
    public string? AuditFile { get; init; }
    public string? AuditLog { get; init; }
    public bool Smoke => SmokeLog is not null;
    public bool Audit => AuditLog is not null;

    public static LaunchRequest Parse(string[] args)
    {
        var rest = args.Length > 0 ? args.Skip(1).ToArray() : args;
        if (rest.Length >= 1 && rest[0] == "--smoke")
        {
            var file = rest.Length > 1 ? rest[1] : "";
            return new LaunchRequest
            {
                PlayerOnly = true,
                Media = file.Length > 0 ? [file] : [],
                SmokeFile = file,
                SmokePng = rest.Length > 2 ? rest[2] : "smoke.png",
                SmokeLog = rest.Length > 3 ? rest[3] : "smoke.log",
            };
        }
        if (rest.Length >= 1 && rest[0] == "--audit-fullscreen")
        {
            var file = rest.Length > 1 ? rest[1] : "";
            return new LaunchRequest
            {
                PlayerOnly = true,
                Media = file.Length > 0 ? [file] : [],
                AuditFile = file,
                AuditLog = rest.Length > 2 ? rest[2] : "fullscreen.log",
            };
        }
        var media = rest.Where(a => a.Length > 0 && !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        return new LaunchRequest { PlayerOnly = media.Length > 0, Media = media };
    }
}
