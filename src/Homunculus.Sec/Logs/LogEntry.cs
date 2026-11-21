namespace Homunculus.Sec.Logs;

public enum LogSource { NginxAccess, NginxError, Auth }

public enum EntryKind
{
    /// <summary>An HTTP request served by nginx.</summary>
    Request,

    /// <summary>nginx refused a request because a limit_req / limit_conn zone was exceeded.</summary>
    RateLimited,

    /// <summary>Any other nginx error-log line.</summary>
    ServerError,

    /// <summary>A failed password or public-key login.</summary>
    FailedLogin,

    /// <summary>A login attempt for a user that does not exist.</summary>
    InvalidUser,

    /// <summary>
    /// A connection that ended before it logged in ("Connection closed by invalid user …", "Disconnected from authenticating user …").
    /// One per attempt, so this is how password guessing shows up on a server that only accepts keys and so never logs "Failed password".
    /// </summary>
    PreauthDrop,

    /// <summary>A successful login.</summary>
    LoginSuccess,
}

/// <summary>One parsed log line. Only the fields that apply to the source are set.</summary>
public sealed record LogEntry(
    LogSource Source,
    EntryKind Kind,
    DateTimeOffset Time,
    string? Ip,
    string Raw,
    string? User = null,
    string? Method = null,
    string? Path = null,
    int? Status = null,
    string? UserAgent = null);
