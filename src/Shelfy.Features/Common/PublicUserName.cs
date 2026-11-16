using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Common;

/// <summary>
/// A name safe to show to other readers: the display name, or if that is missing a username that is not an email,
/// or the local-part of the email. Never the full email address.
/// </summary>
public static class PublicUserName
{
    public const string Fallback = "Someone";

    public static string Of(User? user, ShelfyAccount? account)
    {
        if (!string.IsNullOrWhiteSpace(user?.DisplayName))
        {
            return user.DisplayName.Trim();
        }

        var userName = account?.UserName?.Trim();
        if (!string.IsNullOrWhiteSpace(userName) && userName.IndexOf('@') < 0)
        {
            return userName;
        }

        var email = FirstNonEmpty(account?.Email, userName is not null && userName.Contains('@') ? userName : null);
        if (email is not null)
        {
            var at = email.IndexOf('@');
            if (at > 0)
            {
                var local = email[..at].Trim();
                if (local.Length > 0)
                {
                    return local;
                }
            }
        }

        return Fallback;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return null;
    }
}
