namespace Shelfy.Domain;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Subject claim from the identity provider (ADR-006). Null until linked.</summary>
    public string? IdentitySubject { get; set; }

    public string DisplayName { get; set; } = "";
    public string City { get; set; } = "";
}
