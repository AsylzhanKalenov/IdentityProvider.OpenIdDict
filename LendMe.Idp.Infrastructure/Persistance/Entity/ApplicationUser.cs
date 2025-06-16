using Microsoft.AspNetCore.Identity;

namespace LendMe.Idp.Infrastructure.Persistance.Entity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? MiddleName { get; set; }
    public string? ProfilePicture { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Locale { get; set; } = "en-US";
    public string? TimeZone { get; set; } = "UTC";
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object>? CustomClaims { get; set; }
        
    // External login tracking
    public string? ExternalProvider { get; set; }
    public string? ExternalProviderId { get; set; }
}