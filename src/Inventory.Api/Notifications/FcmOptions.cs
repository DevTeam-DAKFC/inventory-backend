namespace Inventory.Api.Notifications;

public class FcmOptions
{
    public const string SectionName = "Firebase";

    public bool Enabled { get; set; }
    public string? ProjectId { get; set; }
    public string? CredentialsPath { get; set; }
}
