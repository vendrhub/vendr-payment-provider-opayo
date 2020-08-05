namespace Vendr.PaymentProviders.SagePay.Api.Models
{
    public class SagePayServerClientConfig
    {
        public string ProviderAlias { get; set; }
        public string ErrorUrl { get; set; }
        public string CancelUrl { get; set; }
        public string ContinueUrl { get; set; }
    }
}
