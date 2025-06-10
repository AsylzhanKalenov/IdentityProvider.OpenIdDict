using Microsoft.AspNetCore.Identity;

namespace LendMe.Idp.Infrastructure.Persistance.Entity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Picture { get; set; }
    public DateTime CreatedAt { get; set; }
}