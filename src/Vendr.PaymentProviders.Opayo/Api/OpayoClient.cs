﻿using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;
using Vendr.PaymentProviders.Opayo.Api.Models;
using Vendr.Core;
using Vendr.Core.Logging;
using Vendr.Core.Models;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.PaymentProviders.Opayo.Api
{
    public class OpayoServerClient
    {
        private readonly ILogger logger;
        private readonly OpayoServerClientConfig config;

        public OpayoServerClient(ILogger logger, OpayoServerClientConfig config)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public Dictionary<string, string> InitiateTransaction(bool useTestMode, Dictionary<string, string> inputFields)
        {
            var rawResponse = MakePostRequest(
                GetMethodUrl(inputFields[OpayoConstants.TransactionRequestFields.TransactionType], useTestMode),
                inputFields);

            return GetFields(rawResponse);

        }

        public CallbackResult HandleCallback(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            switch (request.Status)
            {
                case OpayoConstants.CallbackRequest.Status.Abort:
                    return GenerateAbortedCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.Rejected:
                    return GenerateRejectedCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.Registered:
                case OpayoConstants.CallbackRequest.Status.Error:
                    return GenerateErrorCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.Pending:
                    return GeneratePendingCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.Ok:
                    return GenerateOkCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.NotAuthorised:
                    return GenerateNotAuthorisedCallbackResponse(order, request, settings);
                case OpayoConstants.CallbackRequest.Status.Authenticated:
                    return GenerateAuthenticatedCallbackResponse(order, request, settings);
                default:
                    logger.Warn<SagePayServerClient>("Unknown callback response status recieved: {status}", request.Status);
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

        private CallbackResult GenerateOkCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction okay:\n\tOpayoTx: {VPSTxId}", request.VPSTxId);
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                TransactionInfo = validSig
                    ? new TransactionInfo
                    {
                        TransactionId = request.VPSTxId,
                        AmountAuthorized = order.TransactionAmount.Value,
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
                            { OpayoConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { OpayoConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GenerateAuthenticatedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction Authenticated:\n\tOpayoTx: {VPSTxId}", request.VPSTxId);
            
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
                            { OpayoConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { OpayoConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GenerateNotAuthorisedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction not authorised:\n\tOpayoTx: {VPSTxId}", request.VPSTxId);
            
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
                            { OpayoConstants.OrderProperties.TransDetails, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits) },
                            { OpayoConstants.OrderProperties.TransDetailsHash, string.Join(":", request.TxAuthNo, request.CardType, request.Last4Digits).ToMD5Hash() }
                        }
                    : null
            };
        }

        private CallbackResult GeneratePendingCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction pending:\n\tOpayoTx: {VPSTxId}", request.VPSTxId);
            
            var validSig = ValidateVpsSigniture(order, request, settings);

            return new CallbackResult
            {
                TransactionInfo = validSig
                    ? new TransactionInfo
                    {
                        TransactionId = request.VPSTxId,
                        AmountAuthorized = order.TransactionAmount.Value,
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

        private CallbackResult GenerateAbortedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction aborted:\n\tOpayoTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            
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

        private CallbackResult GenerateRejectedCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction rejected:\n\tOpayoTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            
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

        private CallbackResult GenerateErrorCallbackResponse(OrderReadOnly order, CallbackRequestModel request, OpayoSettings settings)
        {
            logger.Warn<OpayoServerClient>("Payment transaction error:\n\tOpayoTx: {VPSTxId}\n\tDetail: {StatusDetail}", request.VPSTxId, request.StatusDetail);
            
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

        private bool ValidateVpsSigniture(OrderReadOnly order, CallbackRequestModel callbackRequest, OpayoSettings settings)
        {
            var md5Values = new List<string>
            {
                callbackRequest.VPSTxId,
                callbackRequest.VendorTxCode,
                callbackRequest.Status,
                callbackRequest.TxAuthNo,
                settings.VendorName.ToLowerInvariant(),
                callbackRequest.AVSCV2,
                order.Properties[OpayoConstants.OrderProperties.SecurityKey]?.Value,
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
            responseBody.AppendLine($"{OpayoConstants.Response.Status}={OpayoConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{OpayoConstants.Response.RedirectUrl}={config.ContinueUrl}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateAbortCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{OpayoConstants.Response.Status}={OpayoConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{OpayoConstants.Response.RedirectUrl}={config.CancelUrl}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateRejectedCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{OpayoConstants.Response.Status}={OpayoConstants.Response.StatusCodes.Ok}");
            responseBody.AppendLine($"{OpayoConstants.Response.RedirectUrl}={MakeUrlAbsolute(config.ErrorUrl)}");
            return new StringContent(responseBody.ToString());
        }

        private HttpContent GenerateInvalidCallbackResponseBody()
        {
            var responseBody = new StringBuilder();
            responseBody.AppendLine($"{OpayoConstants.Response.Status}={OpayoConstants.Response.StatusCodes.Error}");
            responseBody.AppendLine($"{OpayoConstants.Response.RedirectUrl}={MakeUrlAbsolute(config.ErrorUrl)}");
            return new StringContent(responseBody.ToString());
        }

        private string MakeUrlAbsolute(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var result))
                return result.ToString();

            var request = HttpContext.Current.Request;

            var scheme = request.Headers["X-Forwarded-Proto"];
            if (string.IsNullOrWhiteSpace(scheme))
            {
                scheme = request.Url.Scheme;
            }

            var host = request.Headers["X-Original-Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                host = request.Url.Host;
            }

            Uri baseUrl = (new UriBuilder(scheme, host, (!(scheme == "https") || !(host != "localhost") ? request.Url.Port : 443))).Uri;
            
            return new Uri(baseUrl, url).AbsoluteUri;
        }

    }
}
