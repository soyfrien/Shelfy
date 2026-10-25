namespace Shelfy.Domain;

/// <summary>Whether a <see cref="DetectedSpine"/> was matched to a Book.</summary>
public enum SpineResolutionStatus
{
    /// <summary>No confident match; kept so the user can correct it manually.</summary>
    Unresolved,

    /// <summary>Matched to a canonical Book (<see cref="DetectedSpine.BookId"/>).</summary>
    Resolved
}
