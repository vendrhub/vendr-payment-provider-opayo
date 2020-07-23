using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;
using Vendr.Contrib.PaymentProviders.SagePay.Models;
using Vendr.Core;
using Vendr.Core.Logging;
using Vendr.Core.Models;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    public class SagePayServerClient
    {
        private readonly ILogger logger;
        private readonly SagePayServerClientConfig config;

        public SagePayServerClient(ILogger logger, SagePayServerClientConfig config)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public Dictionary<string, string> InitiateTransaction(bool useTestMode, Dictionary<string, string> inputFields)
        {
            var rawResponse = MakePostRequest(
                GetMethodUrl(inputFields[SagePayConstants.TransactionRequestFields.TransactionType], useTestMode),
                inputFields);

            return GetFields(rawResponse);

        }

        public CallbackResult HandleCallback(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            switch (request.Status)
            {
                case SagePayConstants.CallbackRequest.Status.Abort:
                    return GenerateAbortedCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.Rejected:
                    return GenerateRejectedCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.Registered:
                case SagePayConstants.CallbackRequest.Status.Error:
                    return GenerateErrorCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.Pending:
                    return GeneratePendingCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.Ok:
                    return GenerateOkCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.NotAuthorised:
                    return GenerateNotAuthorisedCallbackResponse(order, request, settings);
                case SagePayConstants.CallbackRequest.Status.Authenticated:
                    return GenerateAuthenticatedCallbackResponse(order, request, settings);
                default:
                    return CallbackResult.Empty;
            }
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

        private CallbackResult GenerateOkCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction okay:\n\tSagePayTx: {VPSTxId}", request.VPSTxId);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                TransactionInfo = validSig
                    ? new TransactionInfo
                    {
                        TransactionId = request.VPSTxId,
                        AmountAuthorized = order.TotalPrice.Value.WithTax,
                        TransactionFee = request.Surcharge,
                        PaymentStatus = request.TxType == "PAYMENT" ? PaymentStatus.Captured : PaymentStatus.Authorized
                    }
                    : null,
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateOkCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                },
                MetaData = validSig
                    ? new Dictionary<string, string>
                        {
                            { SagePayConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { SagePayConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GenerateAuthenticatedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction Authenticated:\n\tSagePayTx: {VPSTxId}", request.VPSTxId);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {                
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateOkCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                },
                MetaData = validSig
                    ? new Dictionary<string, string>
                        {
                            { SagePayConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { SagePayConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GenerateNotAuthorisedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction not authorised:\n\tSagePayTx: {VPSTxId}", request.VPSTxId);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                TransactionInfo = validSig
                    ? new TransactionInfo
                    {
                        TransactionId = request.VPSTxId,
                        AmountAuthorized = 0,
                        TransactionFee = request.Surcharge,
                        PaymentStatus = PaymentStatus.Error
                    }
                    : null,
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateRejectedCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                },
                MetaData = validSig
                    ? new Dictionary<string, string>
                        {
                            { SagePayConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { SagePayConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GeneratePendingCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction pending:\n\tSagePayTx: {VPSTxId}", request.VPSTxId);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                TransactionInfo = validSig
                    ? new TransactionInfo
                    {
                        TransactionId = request.VPSTxId,
                        AmountAuthorized = order.TotalPrice.Value.WithTax,
                        TransactionFee = request.Surcharge,
                        PaymentStatus = PaymentStatus.PendingExternalSystem
                    }
                    : null,
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateOkCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                }
            };
        }

        private CallbackResult GenerateAbortedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction aborted:\n\tSagePayTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateAbortCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                }
            };
        }

        private CallbackResult GenerateRejectedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction rejected:\n\tSagePayTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateRejectedCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                }
            };
        }

        private CallbackResult GenerateErrorCallbackResponse(OrderReadOnly order, CallbackRequestModel request, SagePaySettings settings)
        {
            logger.Warn<SagePayServerClient>("Payment transaction error:\n\tSagePayTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                HttpResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = validSig
                        ? GenerateRejectedCallbackResponseBody()
                        : GenerateInvalidCallbackResponseBody()
                }
            };
        }

        private bool ValidateVpsSigniture(OrderReadOnly order, CallbackRequestModel callbackRequest, SagePaySettings settings)
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

            string calcedMd5Hash = string.Join("", md5Values.Where(v => string.IsNullOrEmpty(v) == false)).ToMD5Hash().ToUpperInvariant();
            return callbackRequest.VPSSignature == calcedMd5Hash;
        }

        private HttpContent GenerateOkCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={config.ContinueUrl}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateAbortCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={config.CancelUrl}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateRejectedCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={MakeUrlAbsolute(config.ErrorUrl)}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateInvalidCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{SagePayConstants.Response.Status}={SagePayConstants.Response.StatusCodes.Error}");
            responseBody.AppendLine($"{SagePayConstants.Response.RedirectUrl}={MakeUrlAbsolute(config.ErrorUrl)}");
            return new StringContent(responseBody.ToString());
        }

        private string MakeUrlAbsolute(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var result))
                return result.ToString();


            var request = HttpContext.Current.Request;
            string scheme = request.Headers["X-Forwarded-Proto"];
            if (string.IsNullOrWhiteSpace(scheme))
            {
                scheme = request.Url.Scheme;
            }
            string host = request.Headers["X-Original-Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                host = request.Url.Host;
            }
            Uri baseUrl = (new UriBuilder(scheme, host, (!(scheme == "https") || !(host != "localhost") ? request.Url.Port : 443))).Uri;
            
            return new Uri(baseUrl, url).AbsoluteUri;
        }

    }
}
