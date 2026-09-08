namespace EffortlessInsight.Api.Services.Collaboration;

/// <summary>
/// Raised when a write is based on a version of a record that has since been
/// superseded. Surfaced as HTTP 409 so a client can tell "your change did not
/// apply because someone else edited this" apart from a transient failure it
/// should retry (TC-MOB-059).
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message) { }
}
