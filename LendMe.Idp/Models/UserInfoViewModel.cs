namespace LendMe.Idp.Models;

public class UserInfoViewModel
{
    public string Sub { get; set; }
    public string Name { get; set; }
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Email { get; set; }
    public bool EmailVerified { get; set; }
    public List<string> Roles { get; set; }
}