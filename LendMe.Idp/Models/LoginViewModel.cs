using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace LendMe.Idp.Models;

public class LoginViewModel
{
    [Required]
    [KazakhPhoneNumber]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [Display(Name = "Remember me?")] public bool RememberMe { get; set; }

    public string ReturnUrl { get; set; }
}

public class KazakhPhoneNumberAttribute : ValidationAttribute
{
    private static readonly Regex KazakhPhoneRegex = new Regex(
        @"^(\+7|7|8)(70[0-9]|71[0-9]|72[0-9]|73[0-9]|74[0-9]|75[0-9]|76[0-9]|77[0-9]|78[0-9])[0-9]{7}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public override bool IsValid(object value)
    {
        if (value == null)
            return true; // Пусть Required валидатор обрабатывает null значения

        string phoneNumber = value.ToString().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");

        return KazakhPhoneRegex.IsMatch(phoneNumber);
    }

    public override string FormatErrorMessage(string name)
    {
        return $"Номер телефона должен быть в казахском формате (например: +77001234567, 87001234567)";
    }
}