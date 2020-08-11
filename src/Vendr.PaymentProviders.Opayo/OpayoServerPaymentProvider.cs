using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Mvc;
using Vendr.Core;
using Vendr.Core.Logging;
using Vendr.Core.Models;
using Vendr.Core.Security;
using Vendr.Core.Web;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;
using Vendr.PaymentProviders.Opayo.Api;
using Vendr.PaymentProviders.Opayo.Api.Models;

namespace Vendr.PaymentProviders.Opayo
{
    // NOTE: This payment provider was written just as SagePay was rebranded to Opayo so we
    // have decided to upate the payment provider name to Opayo to make it future proof however
    // much of the API endpoints are still referencing SagePay 
    [PaymentProvider("opayo-server", "Opayo Server", "Opayo Server payment provider", Icon = "icon-credit-card")]
    public class OpayoServerPaymentProvider : PaymentProviderBase<OpayoSettings>
    {
        private readonly ILogger logger;
        private readonly IPaymentProviderUriResolver paymentProviderUriResolver;
        private readonly IHashProvider hashProvider;

        public OpayoServerPaymentProvider(VendrContext vendr, ILogger logger, IPaymentProviderUriResolver paymentProviderUriResolver, IHashProvider hashProvider)
            : base(vendr)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.paymentProviderUriResolver = paymentProviderUriResolver ?? throw new ArgumentNullException(nameof(paymentProviderUriResolver));
            this.hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        }

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, OpayoSettings settings)
        {
            var form = new PaymentForm(cancelUrl, FormMethod.Post);
            var client = new OpayoServerClient(logger, new OpayoServerClientConfig
            {
                ContinueUrl = continueUrl,
                CancelUrl = cancelUrl,
                ErrorUrl = GetErrorUrl(order, settings),
                ProviderAlias = Alias
            });

            var inputFields = LoadInputFields(order, settings, callbackUrl);
            var responseDetails = client.InitiateTransaction(settings.TestMode, inputFields);

            var status = responseDetails[OpayoConstants.Response.Status];

            Dictionary<string, string> orderMetaData = null;

            if (status == OpayoConstants.Response.StatusCodes.Ok || status == OpayoConstants.Response.StatusCodes.Repeated)
            {
                orderMetaData = GenerateOrderMeta(responseDetails);
                if (orderMetaData != null)
                {
                    form.Action = responseDetails[OpayoConstants.Response.NextUrl];
                }
            }
            else
            {
                logger.Warn<OpayoServerPaymentProvider>("Opayo (" + order.OrderNumber + ") - Generate html form error - status: " + status + " | status details: " + responseDetails["StatusDetail"]);
            }

            return new PaymentFormResult()
            {
                MetaData = orderMetaData,
                Form = form
            };
        }

        private Dictionary<string, string> GenerateOrderMeta(Dictionary<string, string> responseDetails)
        {
            return new Dictionary<string, string> 
            {
                { OpayoConstants.OrderProperties.SecurityKey, responseDetails[OpayoConstants.Response.SecurityKey] },
                { OpayoConstants.OrderProperties.TransactionId, responseDetails[OpayoConstants.Response.TransactionId]}
            };
            
        }

        private Dictionary<string,string> LoadInputFields(OrderReadOnly order, OpayoSettings settings, string vendrCallbackUrl)
        {
            settings.MustNotBeNull(nameof(settings));
            return OpayoInputLoader.LoadInputs(order, settings, Vendr, vendrCallbackUrl);
        }
        
        public override string GetCancelUrl(OrderReadOnly order, OpayoSettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.CancelUrl.MustNotBeNullOrWhiteSpace(nameof(settings.CancelUrl));
            return settings.CancelUrl.ReplacePlaceHolders(order);
        }

        public override string GetErrorUrl(OrderReadOnly order, OpayoSettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.ErrorUrl.MustNotBeNullOrWhiteSpace(nameof(settings.ErrorUrl));
            return settings.ErrorUrl.ReplacePlaceHolders(order);
        }

        public override string GetContinueUrl(OrderReadOnly order, OpayoSettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.ContinueUrl.MustNotBeNullOrWhiteSpace(nameof(settings.ContinueUrl));
            return settings.ContinueUrl.ReplacePlaceHolders(order);
        }

        public override CallbackResult ProcessCallback(OrderReadOnly order, HttpRequestBase request, OpayoSettings settings)
        {
            var callbackRequestModel = CallbackRequestModel.FromRequest(request);
            var client = new OpayoServerClient(
                logger, 
                new OpayoServerClientConfig {
                    ProviderAlias = Alias,
                    ContinueUrl = paymentProviderUriResolver.GetContinueUrl(Alias, order.GenerateOrderReference(), hashProvider),
                    CancelUrl = paymentProviderUriResolver.GetCancelUrl(Alias, order.GenerateOrderReference(), hashProvider),
                    ErrorUrl = GetErrorUrl(order, settings)
                });

            return client.HandleCallback(order, callbackRequestModel, settings);

        }        
    }


    // Maintain a wrapper sage pay provider that just proxies the Opayo provider so that
    // we don't break anyone using the alpha. Thankfully non of the settings were prefixed
    // with "SagePay" so it shouldn't be a problem reusing Opayo settings object
    [Obsolete("Use OpayoServerPaymentProvider instead")]
    [PaymentProvider("sagepay-server", "Opayo Server", "Opayo Server payment provider", Icon = "icon-credit-card")]
    public class SagePayServerPaymentProvider : OpayoServerPaymentProvider
    {
        public SagePayServerPaymentProvider(VendrContext vendr, ILogger logger, IPaymentProviderUriResolver paymentProviderUriResolver, IHashProvider hashProvider)
            : base(vendr, logger, paymentProviderUriResolver, hashProvider)
        { }
    }
}
