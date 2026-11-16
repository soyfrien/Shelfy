namespace Shelfy.Domain;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Subject claim from the identity provider (ADR-006). Null until linked.</summary>
    public string? IdentitySubject { get; set; }

    public string DisplayName { get; set; } = "";
    public string City { get; set; } = "";

    /// <summary>
    /// Private postal address for mailing books (ADR-012). Never shown publicly: only the counterparty of an approved loan sees
    /// it, and only while that loan is in progress.
    /// </summary>
    public string? MailingAddress { get; set; }
}
