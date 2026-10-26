namespace Shelfy.Features.Recognition.FixSpine;

/// <summary>One book offered for an unmatched spine, or found by the search box.</summary>
/// <param name="Isbn13">Always a valid ISBN-13: this is what is sent back to resolve.</param>
/// <param name="Score">0..1 how well it fits the spine text (0 for typed searches).</param>
/// <param name="Source">OpenLibrary, GoogleBooks or Shelfy (already known here).</param>
/// <param name="Strategy">How it was found: full, parsed, longest, swapped, line or search.</param>
public record BookSuggestionDto(string Isbn13, string Title, string Author, string? CoverUrl, double Score, string Source, string Strategy, Guid? BookId = null);

/// <param name="Unavailable">True when the book catalogs could not be reached and nothing was found because of it.</param>
public record SpineSuggestionsResponse(Guid SpineId, string Text, IReadOnlyList<BookSuggestionDto> Suggestions, bool Unavailable);

/// <summary>
/// What to add: a known book (<see cref="BookId"/>, from a search result that is already in Shelfy), or an ISBN-13 with the
/// details shown in the suggestion (<see cref="Title"/> and <see cref="Author"/>, <see cref="CoverUrl"/>). Details are only used when the book is new here.
/// </summary>
public record ResolveSpineRequest(string? Isbn13, Guid? BookId, string? Title, string? Author, string? CoverUrl);

public record ResolvedSpineBookDto(Guid BookId, string Title, string Author, string? Isbn, string? CoverUrl);

/// <param name="AddedToShelf">False when the book was already on the shelf (nothing was added twice).</param>
/// <param name="SpinesResolved">How many unmatched reads of this same text were marked resolved.</param>
public record ResolveSpineResponse(Guid SpineId, ResolvedSpineBookDto Book, bool AddedToShelf, int SpinesResolved, bool Remembered);

/// <param name="SpinesDismissed">How many reads of this same text were hidden from the list.</param>
public record DismissSpineResponse(Guid SpineId, bool Dismissed, int SpinesDismissed);
