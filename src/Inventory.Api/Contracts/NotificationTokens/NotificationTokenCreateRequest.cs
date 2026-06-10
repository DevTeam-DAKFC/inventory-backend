using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Contracts.NotificationTokens;

public class NotificationTokenCreateRequest
{
    [Required]
    [StringLength(500)]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string Platform { get; set; } = string.Empty;
}
