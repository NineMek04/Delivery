using System.ComponentModel.DataAnnotations;

namespace BackendApi.Features.FleetTracking.Models;

public sealed class ClientEventRequest
{
    [Required]
    [MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    [RegularExpression("^(ADMIN|RIDER|CUSTOMER)$", ErrorMessage = "ClientType must be ADMIN, RIDER, or CUSTOMER.")]
    public string ClientType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }
}
