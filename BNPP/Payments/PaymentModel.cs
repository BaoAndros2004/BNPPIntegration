using System.Text.Json;

namespace BNPPIntegration.BNPP.Payments;

public sealed class PaymentModel
{
    public string PaymentType { get; init; } = "Domestic";
    public JsonElement Payment { get; init; }
}
