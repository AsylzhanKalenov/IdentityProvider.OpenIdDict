using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace LendMe.Idp.Models;

public class ExternalLoginConfirmationViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }

    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
}

public class ExternalLoginListViewModel
{
    public string ReturnUrl { get; set; }
}

public class ManageExternalLoginsViewModel
{
    public IList<UserLoginInfo> CurrentLogins { get; set; }
    public IList<AuthenticationScheme> OtherLogins { get; set; }
    public bool ShowRemoveButton { get; set; }
    public string StatusMessage { get; set; }
}