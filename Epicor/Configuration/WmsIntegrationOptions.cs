namespace BNPPIntegration.BNPP.Configuration
{
    public sealed class WmsIntegrationOptions
    {
        public const string SectionName = "Wms";

        public string BaseUrl { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
