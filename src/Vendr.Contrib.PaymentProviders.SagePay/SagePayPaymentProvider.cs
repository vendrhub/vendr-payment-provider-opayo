using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
    [PaymentProvider("sagepay", "SagePay", "SagePay payment provider", Icon = "icon-credit-card")]
    public class SagePayPaymentProvider : PaymentProviderBase<SagePaySettings>
    {
        private readonly ILogger logger;
        private readonly IPaymentProviderUriResolver paymentProviderUriResolver;
        private readonly IHashProvider hashProvider;

        public SagePayPaymentProvider(VendrContext vendr, ILogger logger, IPaymentProviderUriResolver paymentProviderUriResolver, IHashProvider hashProvider)
            : base(vendr)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.paymentProviderUriResolver = paymentProviderUriResolver ?? throw new ArgumentNullException(nameof(paymentProviderUriResolver));
            this.hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        }

        public override bool FinalizeAtContinueUrl => true;

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SagePaySettings settings)
        {
            var form = new PaymentForm(cancelUrl, FormMethod.Post);
            var inputFields = LoadInputFields(order, settings, callbackUrl);
            var responseDetails = InitiateTransaction(settings.UseTestMode, inputFields);

            var status = responseDetails[SagePayConstants.Response.Status];
            Dictionary<string, string> orderMetaData = null;
            if (status == SagePayConstants.Response.StatusCodes.Ok || status == SagePayConstants.Response.StatusCodes.Repeated)
            {
                orderMetaData = GenerateOrderMeta(responseDetails, continueUrl, cancelUrl);
                if(orderMetaData != null)
                {
                    form.Action = responseDetails[SagePayConstants.Response.NextUrl];
                }
            }
            else
                logger.Warn<SagePayPaymentProvider>("Sage Pay(" + order.CartNumber + ") - Generate html form error - status: " + status + " | status details: " + responseDetails["StatusDetail"]);
            

            return new PaymentFormResult()
            {
                MetaData = orderMetaData,
                Form = form
            };
        }

        private Dictionary<string, string> GenerateOrderMeta(Dictionary<string, string> responseDetails, string continueUrl, string cancelUrl)
        {
            return new Dictionary<string, string> 
            {
                { SagePayConstants.OrderProperties.SecurityKey, new PropertyValue(responseDetails[SagePayConstants.Response.SecurityKey]) },
                { SagePayConstants.OrderProperties.ContinueUrl, new PropertyValue(continueUrl, true) },
                { SagePayConstants.OrderProperties.CancelUrl, new PropertyValue(cancelUrl, true) },
                { SagePayConstants.OrderProperties.TransactionId, new PropertyValue(responseDetails[SagePayConstants.Response.TransactionId], true) }
            };
            
        }

        private Dictionary<string,string> InitiateTransaction(bool useTestMode, Dictionary<string, string> inputFields)
        {
            var rawResponse = MakePostRequest(
                GetMethodUrl(inputFields[SagePayConstants.TransactionRequestFields.TransactionType], useTestMode),
                inputFields);

            return GetFields(rawResponse);

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
            switch(callbackRequestModel.Status)
            {
                case SagePayConstants.CallbackRequest.Status.Abort:
                    return GenerateAbortedCallbackResponse(order, callbackRequestModel, settings);
                case SagePayConstants.CallbackRequest.Status.Rejected:
                    return GenerateRejectedCallbackResponse(order, callbackRequestModel, settings);
                default:
                    return new CallbackResult();
            }

            
            //return new CallbackResult
            //{
            //    TransactionInfo = new TransactionInfo
            //    {
            //        AmountAuthorized = order.TotalPrice.Value.WithTax,
            //        TransactionFee = 0m,
            //        TransactionId = Guid.NewGuid().ToString("N"),
            //        PaymentStatus = PaymentStatus.Authorized
            //    },
            //    HttpResponse = new System.Net.Http.HttpResponseMessage()
            //};
        }

        private CallbackResult GenerateAbortedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayPaymentProvider>("Payment transaction aborted:\n\tSagePayTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateAbortCallbackResponseBody(order, settings)
                        : GenerateInvalidCallbackResponseBody(order, settings)
                }
            };
        }

        private CallbackResult GenerateRejectedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayPaymentProvider>("Payment transaction rejected:\n\tSagePayTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail );
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {                
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig 
                        ? GenerateRejectedCallbackResponseBody(order, settings)
                        : GenerateInvalidCallbackResponseBody(order, settings)
                }
            };
        }

        private HttpContent GenerateAbortCallbackResponseBody(OrderReadOnly order, SagePaySettings settings)
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={paymentProviderUriResolver.GetCancelUrl(Alias, order.GenerateOrderReference(), hashProvider)}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateRejectedCallbackResponseBody(OrderReadOnly order, SagePaySettings settings)
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={MakeUrlAbsolute(GetErrorUrl(order, settings), paymentProviderUriResolver.GetCancelUrl(Alias, order.GenerateOrderReference(), hashProvider))}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateInvalidCallbackResponseBody(OrderReadOnly order, SagePaySettings settings)
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Error}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={MakeUrlAbsolute(GetErrorUrl(order, settings), paymentProviderUriResolver.GetCancelUrl(Alias, order.GenerateOrderReference(), hashProvider))}");
            return new StringContent(responseBody.ToString());
        }

        private string GetMethodUrl(string type, bool testMode)
        {
            switch (testMode)
            {
                case false:
                    switch (type.ToUpperInvariant())
                    {
                        case "AUTHORISE":
                            return "https://live.sagepay.com/gateway/service/authorise.vsp";
                        case "PAYMENT":
                        case "DEFERRED":
                        case "AUTHENTICATE":
                            return "https://live.sagepay.com/gateway/service/vspserver-register.vsp";
                        case "CANCEL":
                            return "https://live.sagepay.com/gateway/service/cancel.vsp";
                        case "REFUND":
                            return "https://live.sagepay.com/gateway/service/refund.vsp";
                    }
                    break;
                case true:
                    switch (type.ToUpperInvariant())
                    {
                        case "AUTHORISE":
                            return "https://test.sagepay.com/gateway/service/authorise.vsp";
                        case "PAYMENT":
                        case "DEFERRED":
                        case "AUTHENTICATE":
                            return "https://test.sagepay.com/gateway/service/vspserver-register.vsp";
                        case "CANCEL":
                            return "https://test.sagepay.com/gateway/service/cancel.vsp";
                        case "REFUND":
                            return "https://test.sagepay.com/gateway/service/refund.vsp";
                    }
                    break;
            }

            return string.Empty;
        }

        private string MakePostRequest(string url, IDictionary<string, string> inputFields)
        {
            try
            {
                string requestContents = string.Empty;
                if (inputFields != null)
                {
                    requestContents = string.Join("&", (
                        from i in inputFields
                        select string.Format("{0}={1}", i.Key, HttpUtility.UrlEncode(i.Value))).ToArray<string>());
                }
                var request = new FlurlRequest(url)
                    .SetQueryParams(inputFields, Flurl.NullValueHandling.Remove);
                var response = request
                    .PostAsync(null)
                    .ReceiveString()
                    .Result; 

                return response;
            }
            catch (FlurlHttpException ex)
            {

                return string.Empty;
            }
            

        }

        private Dictionary<string, string> GetFields(string response)
        {
            return response.Split(
                Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                .ToDictionary(
                    i => i.Substring(0, i.IndexOf("=", StringComparison.Ordinal)), 
                    i => i.Substring(i.IndexOf("=", StringComparison.Ordinal) + 1, i.Length - (i.IndexOf("=", StringComparison.Ordinal) + 1)));
        }

        private bool ValidateVpsSigniture (OrderReadOnly order, CallbackRequestModel callbackRequest, SagePaySettings settings)
        {
            var md5Values = new List<string>
            {
                callbackRequest.VPSTxId,
                callbackRequest.VendorTxCode,
                callbackRequest.Status,
                callbackRequest.TxAuthNo,
                settings.VendorName.ToLowerInvariant(),
                callbackRequest.AVSCV2,
                order.Properties[SagePayConstants.OrderProperties.SecurityKey]?.Value,
                callbackRequest.AddressResult,
                callbackRequest.PostCodeResult,
                callbackRequest.CV2Result,
                callbackRequest.GiftAid,
                callbackRequest.SecureStatus,
                callbackRequest.CAVV,
                callbackRequest.AddressStatus,
                callbackRequest.PayerStatus,
                callbackRequest.CardType,
                callbackRequest.Last4Digits,
                callbackRequest.DeclineCode,
                callbackRequest.ExpiryDate,
                callbackRequest.FraudResponse,
                callbackRequest.BankAuthCode
            };

            string calcedMd5Hash = GenerateMD5Hash(string.Join("", md5Values.Where(v => string.IsNullOrEmpty(v) == false))).ToUpperInvariant();
            return callbackRequest.VPSSignature == calcedMd5Hash;
        }

        protected string GenerateMD5Hash(string input)
        {
            return (new MD5CryptoServiceProvider()).ComputeHash(Encoding.UTF8.GetBytes(input)).ToHex();
        }

        private string MakeUrlAbsolute(string url, string paymentProviderCancelUrl)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var result))
                return result.ToString();

            var paymentProviderCancelUri = new Uri(paymentProviderCancelUrl, UriKind.Absolute);

            var baseUrl = paymentProviderCancelUri.Port == 80 || paymentProviderCancelUri.Port == 443
                ? new UriBuilder(paymentProviderCancelUri.Scheme, paymentProviderCancelUri.Host).Uri
                : new UriBuilder(paymentProviderCancelUri.Scheme, paymentProviderCancelUri.Host, paymentProviderCancelUri.Port).Uri;

            return new Uri(baseUrl, url).ToString();
        }

    }
}
