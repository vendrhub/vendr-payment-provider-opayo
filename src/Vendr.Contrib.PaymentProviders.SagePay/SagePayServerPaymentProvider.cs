using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Mvc;
using Vendr.Contrib.PaymentProviders.SagePay.Models;
using Vendr.Core;
using Vendr.Core.Logging;
using Vendr.Core.Models;
using Vendr.Core.Security;
using Vendr.Core.Web;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    [PaymentProvider("sagepayServer", "SagePay Server", "SagePay Server payment provider", Icon = "icon-credit-card")]
    public class SagePayServerPaymentProvider : PaymentProviderBase<SagePaySettings>
    {
        private readonly ILogger logger;
        private readonly IPaymentProviderUriResolver paymentProviderUriResolver;
        private readonly IHashProvider hashProvider;

        public SagePayServerPaymentProvider(VendrContext vendr, ILogger logger, IPaymentProviderUriResolver paymentProviderUriResolver, IHashProvider hashProvider)
            : base(vendr)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.paymentProviderUriResolver = paymentProviderUriResolver ?? throw new ArgumentNullException(nameof(paymentProviderUriResolver));
            this.hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        }

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SagePaySettings settings)
        {
            var form = new PaymentForm(cancelUrl, FormMethod.Post);
            var client = new SagePayServerClient(logger, new SagePayServerClientConfig
            {
                ContinueUrl = continueUrl,
                CancelUrl = cancelUrl,
                ErrorUrl = GetErrorUrl(order, settings),
                ProviderAlias = Alias
            });

            var inputFields = LoadInputFields(order, settings, callbackUrl);
            var responseDetails = client.InitiateTransaction(settings.TestMode, inputFields);

            var status = responseDetails[SagePayConstants.Response.Status];
            Dictionary<string, string> orderMetaData = null;
            if (status == SagePayConstants.Response.StatusCodes.Ok || status == SagePayConstants.Response.StatusCodes.Repeated)
            {
                orderMetaData = GenerateOrderMeta(responseDetails);
                if(orderMetaData != null)
                {
                    form.Action = responseDetails[SagePayConstants.Response.NextUrl];
                }
            }
            else
                logger.Warn<SagePayServerPaymentProvider>("Sage Pay(" + order.CartNumber + ") - Generate html form error - status: " + status + " | status details: " + responseDetails["StatusDetail"]);
            

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
                { SagePayConstants.OrderProperties.SecurityKey, responseDetails[SagePayConstants.Response.SecurityKey] },
                { SagePayConstants.OrderProperties.TransactionId, responseDetails[SagePayConstants.Response.TransactionId]}
            };
            
        }

        private Dictionary<string,string> LoadInputFields(OrderReadOnly order, SagePaySettings settings, string vendrCallbackUrl)
        {
            settings.MustNotBeNull(nameof(settings));
            return SagePayInputLoader.LoadInputs(order, settings, Vendr, vendrCallbackUrl);
        }
        
        public override string GetCancelUrl(OrderReadOnly order, SagePaySettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.CancelUrl.MustNotBeNullOrWhiteSpace(nameof(settings.CancelUrl));
            return settings.CancelUrl.ReplacePlaceHolders(order);
        }

        public override string GetErrorUrl(OrderReadOnly order, SagePaySettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.ErrorUrl.MustNotBeNullOrWhiteSpace(nameof(settings.ErrorUrl));
            return settings.ErrorUrl.ReplacePlaceHolders(order);
        }

        public override string GetContinueUrl(OrderReadOnly order, SagePaySettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            settings.ContinueUrl.MustNotBeNullOrWhiteSpace(nameof(settings.ContinueUrl));
            return settings.ContinueUrl.ReplacePlaceHolders(order);
        }

        public override CallbackResult ProcessCallback(OrderReadOnly order, HttpRequestBase request, SagePaySettings settings)
        {
            var callbackRequestModel = CallbackRequestModel.FromRequest(request);
            var client = new SagePayServerClient(
                logger, 
                new SagePayServerClientConfig {
                    ProviderAlias = Alias,
                    ContinueUrl = paymentProviderUriResolver.GetContinueUrl(Alias, order.GenerateOrderReference(), hashProvider),
                    CancelUrl = paymentProviderUriResolver.GetCancelUrl(Alias, order.GenerateOrderReference(), hashProvider),
                    ErrorUrl = GetErrorUrl(order, settings)
                });

            return client.HandleCallback(order, callbackRequestModel, settings);

        }

        

        
    }
}
