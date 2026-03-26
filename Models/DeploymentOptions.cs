namespace Kartist.Models
{
    public class DeploymentOptions
    {
        public string Secret { get; set; } = string.Empty;
        public int SignatureToleranceSeconds { get; set; } = 300;
    }
}
