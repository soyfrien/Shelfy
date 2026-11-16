using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Common.Email;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Users.Account;

/// <summary>
/// Register, sign in, sign out, read and update the profile (ASP.NET Core Identity, email + password, cookie; ADR-012).
/// Registering creates a domain <see cref="User"/> and an account with the same Id, and emails a confirmation link. Borrowing,
/// reserving and approving a loan need a confirmed email (see <see cref="Lending.Loans.LoansHandler"/>).
/// </summary>
public class AccountHandler(
    ShelfyDbContext db,
    UserManager<ShelfyAccount> users,
    SignInManager<ShelfyAccount> signIn,
    TimeProvider clock,
    IMailer mailer,
    IMemoryCache cache,
    ILogger<AccountHandler> log)
{
    /// <summary>At most one confirmation email per account per this long, so the resend button can't be used to spam an inbox.</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(1);

    public const int MaxDisplayName = 100;
    public const int MaxCity = 100;
    public const int MaxAddress = 500;

    public async Task<HandlerResult<MeResponse>> RegisterAsync(RegisterRequest request, string baseUrl, CancellationToken ct = default)
    {
        var email = request.Email?.Trim() ?? "";
        if (email.Length is 0 or > 256 || !email.Contains('@'))
        {
            return HandlerResult<MeResponse>.Invalid("Enter a valid email address.");
        }

        var profileError = ValidateProfile(request.DisplayName, request.City, request.MailingAddress);
        if (profileError is not null)
        {
            return HandlerResult<MeResponse>.Invalid(profileError);
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return HandlerResult<MeResponse>.Conflict("An account with this email already exists. Sign in instead.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = request.DisplayName!.Trim(),
            City = request.City?.Trim() ?? "",
            MailingAddress = Clean(request.MailingAddress)
        };
        var account = new ShelfyAccount { Id = user.Id, UserName = email, Email = email, CreatedAt = now };

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var created = await users.CreateAsync(account, request.Password ?? "");
        if (!created.Succeeded)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            db.ChangeTracker.Clear();
            if (tx is null)
            {
                db.Users.Remove(new User { Id = user.Id });
                await db.SaveChangesAsync(ct);
            }

            return HandlerResult<MeResponse>.Invalid(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        await signIn.SignInAsync(account, isPersistent: true);
        await TrySendConfirmationAsync(account, user.DisplayName, baseUrl, ct);
        return HandlerResult<MeResponse>.Ok(await MeAsync(user.Id, ct) ?? throw new InvalidOperationException("New user vanished."));
    }

    public async Task<HandlerResult<MeResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var account = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (account is null)
        {
            return HandlerResult<MeResponse>.Unauthorized("Wrong email or password.");
        }

        var result = await signIn.PasswordSignInAsync(account, request.Password ?? "", request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return HandlerResult<MeResponse>.Unauthorized("Too many attempts. Try again in a few minutes.");
        }

        if (!result.Succeeded)
        {
            return HandlerResult<MeResponse>.Unauthorized("Wrong email or password.");
        }

        return HandlerResult<MeResponse>.Ok(await MeAsync(account.Id, ct) ?? throw new InvalidOperationException("Account without user."));
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    public async Task<HandlerResult<MeResponse>> GetMeAsync(Guid? userId, CancellationToken ct = default)
    {
        var me = userId is null ? null : await MeAsync(userId.Value, ct);
        return me is null ? HandlerResult<MeResponse>.Unauthorized("Not signed in.") : HandlerResult<MeResponse>.Ok(me);
    }

    public async Task<HandlerResult<MeResponse>> UpdateProfileAsync(Guid? userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = userId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return HandlerResult<MeResponse>.Unauthorized("Not signed in.");
        }

        var error = ValidateProfile(request.DisplayName, request.City, request.MailingAddress);
        if (error is not null)
        {
            return HandlerResult<MeResponse>.Invalid(error);
        }

        user.DisplayName = request.DisplayName!.Trim();
        user.City = request.City?.Trim() ?? "";
        user.MailingAddress = Clean(request.MailingAddress);
        await db.SaveChangesAsync(ct);
        return HandlerResult<MeResponse>.Ok((await MeAsync(user.Id, ct))!);
    }

    /// <summary>Confirms the email from the link in the confirmation email. Works without being signed in.</summary>
    public async Task<bool> ConfirmEmailAsync(Guid? userId, string? code)
    {
        if (userId is null || string.IsNullOrEmpty(code))
        {
            return false;
        }

        var account = await users.FindByIdAsync(userId.Value.ToString());
        if (account is null)
        {
            return false;
        }

        if (account.EmailConfirmed)
        {
            return true;
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return false;
        }

        return (await users.ConfirmEmailAsync(account, token)).Succeeded;
    }

    /// <summary>Sends the confirmation email again (signed in, not yet confirmed, at most once a minute).</summary>
    public async Task<HandlerResult<MeResponse>> ResendConfirmationAsync(Guid? userId, string baseUrl, CancellationToken ct = default)
    {
        var account = userId is null ? null : await users.FindByIdAsync(userId.Value.ToString());
        var user = userId is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (account is null || user is null)
        {
            return HandlerResult<MeResponse>.Unauthorized("Not signed in.");
        }

        if (account.EmailConfirmed)
        {
            return HandlerResult<MeResponse>.Conflict("Your email is already confirmed.");
        }

        if (cache.TryGetValue(ResendKey(account.Id), out _))
        {
            return HandlerResult<MeResponse>.Conflict("We just sent one. Check your inbox (and spam), or try again in a minute.");
        }

        if (!await TrySendConfirmationAsync(account, user.DisplayName, baseUrl, ct))
        {
            return HandlerResult<MeResponse>.Invalid("We couldn't send the email just now. Try again later.");
        }

        return HandlerResult<MeResponse>.Ok((await MeAsync(account.Id, ct))!);
    }

    private static string ResendKey(Guid id) => "confirm-email:" + id;

    private async Task<bool> TrySendConfirmationAsync(ShelfyAccount account, string name, string baseUrl, CancellationToken ct)
    {
        cache.Set(ResendKey(account.Id), true, ResendInterval);
        var token = await users.GenerateEmailConfirmationTokenAsync(account);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{baseUrl.TrimEnd('/')}/api/account/confirm-email?userId={account.Id}&code={code}";
        var body = $"Hi {name},\n\nWelcome to Shelfy! Please confirm your email so you can borrow and lend books:\n\n{link}\n\n"
                   + "If you didn't create a Shelfy account, you can ignore this email.\n\nShelfy, an internet library";
        try
        {
            await mailer.SendAsync(new MailMessage(account.Email!, "Confirm your email for Shelfy", body), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Could not send the confirmation email to account {AccountId}", account.Id);
            return false;
        }
    }

    private async Task<MeResponse?> MeAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == userId, ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (account is null || user is null)
        {
            return null;
        }

        var shelves = await db.Shelves.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsHidden)
            .Select(s => new MyShelfDto(s.Id, s.Books.Count))
            .ToListAsync(ct);
        return new MeResponse(user.Id, account.Email ?? "", account.EmailConfirmed, user.DisplayName, user.City, user.MailingAddress, shelves);
    }

    private static string? ValidateProfile(string? displayName, string? city, string? address)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Display name is required.";
        }

        if (displayName.Trim().Length > MaxDisplayName)
        {
            return $"Display name can be at most {MaxDisplayName} characters.";
        }

        if (city?.Trim().Length > MaxCity)
        {
            return $"City can be at most {MaxCity} characters.";
        }

        return address?.Trim().Length > MaxAddress ? $"Mailing address can be at most {MaxAddress} characters." : null;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Replace("\r\n", "\n");
}
