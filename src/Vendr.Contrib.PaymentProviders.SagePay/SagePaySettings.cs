using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    public class SagePaySettings
    {
        public SagePaySettings()
        {
            VPSProtocol = Defaults.VPSProtocol;
        }

        public static class Defaults
        {
            public static string VPSProtocol = "3.00";
            public static string TxType = "PAYMENT";
        }

        [PaymentProviderSetting(Name = "Continue URL", Description = "The URL to continue to after this provider has done processing. eg: /continue/", SortOrder = 20)]
        public string ContinueUrl { get; set; }

        [PaymentProviderSetting(Name = "Vendor Name", Description = "Your unique identifier, assigned to you by Sage Pay during sign up", SortOrder = 1)]
        public string VendorName { get; set; }

        [PaymentProviderSetting(Name = "VPS Protocol", IsAdvanced = true, Description = "Messaging version of the API (defaults to 3.00)", SortOrder = 900)]
        public string VPSProtocol { get; set; }


        [PaymentProviderSetting(Name ="Transaction Type", IsAdvanced =true, Description ="Transaction Type: PAYMENT, DEFERRED, AUTHENTICATE")]
        public string TxType { get; set; }

        [PaymentProviderSetting(Name = "Notification Url", Description = "This is the Url for SagePays Notification Posts" )]
        public string CallbackUrl { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing Surname", Description = "Order Property containing the billing surname")]
        public string OrderPropertyBillingSurname { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing First Name", Description = "Order Property containing the billing first name")]
        public string OrderPropertyBillingFirstName { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing Address 1", Description = "Order Property containing the billing address 1")]
        public string OrderPropertyBillingAddress1 { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing Address 2", Description = "Order Property containing the billing address 2")]
        public string OrderPropertyBillingAddress2 { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing City", Description = "Order Property containing the billing city")]
        public string OrderPropertyBillingCity { get; set; }
        [PaymentProviderSetting(Name = "Order Property: Billing County/State", Description = "Order Property containing the billing county/state")]
        public string OrderPropertyBillingCounty { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Billing Postcode", Description = "Order Property containing the billing postcode")]
        public string OrderPropertyBillingPostcode { get; set; }



        [PaymentProviderSetting(Name = "Order Property: Delivery Surname", Description = "Order Property containing the delivery surname")]
        public string OrderPropertyDeliverySurname { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery First Name", Description = "Order Property containing the delivery first name")]
        public string OrderPropertyDeliveryFirstName { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery Address 1", Description = "Order Property containing the delivery address 1")]
        public string OrderPropertyDeliveryAddress1 { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery Address 2", Description = "Order Property containing the delivery address 2")]
        public string OrderPropertyDeliveryAddress2 { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery City", Description = "Order Property containing the delivery city")]
        public string OrderPropertyDeliveryCity { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery County/State", Description = "Order Property containing the delivery county/state")]
        public string OrderPropertyDeliveryCounty { get; set; }

        [PaymentProviderSetting(Name = "Order Property: Delivery Postcode", Description = "Order Property containing the delivery postcode")]
        public string OrderPropertyDeliveryPostcode { get; set; }

        [PaymentProviderSetting(Name = "Send order line details", Description ="Send the order line details to be shown on the payment providers final stage")]
        public bool IncludeDisplayOrderLines { get; set; }

        [PaymentProviderSetting(Name = "Order Description Property", Description = "Order Property containing the description to send to SagePay")]
        public string OrderPropertyDescription { get; set; }


        [PaymentProviderSetting(Name = "Use test mode",
            Description = "Set whether to process payments in test mode.",
            SortOrder = 1000000)]
        public bool UseTestMode { get; set; }


    }
}
