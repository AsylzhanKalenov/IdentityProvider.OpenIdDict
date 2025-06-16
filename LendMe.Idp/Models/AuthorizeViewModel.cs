using System.ComponentModel.DataAnnotations;

namespace LendMe.Idp.Models;

public class AuthorizeViewModel
{
    [Display(Name = "Application")]
    public string ApplicationName { get; set; }

    [Display(Name = "Scope")]
    public string Scope { get; set; }

    public string ClientId { get; set; }
    public string RedirectUri { get; set; }
    public string ResponseType { get; set; }
    public string State { get; set; }
    public string CodeChallenge { get; set; }
    public string CodeChallengeMethod { get; set; }
}