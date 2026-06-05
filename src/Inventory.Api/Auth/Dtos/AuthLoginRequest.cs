using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Auth.Dtos;

public class AuthLoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
