namespace Shelfy.Features.Users.Account;

public record RegisterRequest(string? Email, string? Password, string? DisplayName, string? City = null, string? MailingAddress = null);

public record LoginRequest(string? Email, string? Password, bool RememberMe = true);

public record UpdateProfileRequest(string? DisplayName, string? City, string? MailingAddress);

public record MyShelfDto(Guid Id, int BookCount);

/// <summary>The signed-in user's own view. This is the only response that contains their own mailing address.</summary>
public record MeResponse(Guid Id, string Email, string DisplayName, string City, string? MailingAddress, IReadOnlyList<MyShelfDto> Shelves);
