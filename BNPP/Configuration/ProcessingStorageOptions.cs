namespace BNPPIntegration.BNPP.Configuration
{
    public class ProcessingStorageOptions
    {
        public const string SectionName = "ProcessingStorage";
        public string InboundRootDirectory { get; set; } = string.Empty;
        public string OutboundRootDirectory { get; set; } = string.Empty;
    }
}
