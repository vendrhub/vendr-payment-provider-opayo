using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Vendr.Core;
using Vendr.Core.Logging;
using Vendr.Core.Models;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    [PaymentProvider("sagepay", "SagePay", "SagePay payment provider", Icon = "icon-credit-card")]
    public class SagePayPaymentProvider : PaymentProviderBase<SagePaySettings>
    {
        private readonly ILogger logger;

        private Dictionary<string, string> inputFields { get; set; }
        public SagePayPaymentProvider(VendrContext vendr, ILogger logger)
            : base(vendr)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));            
        }

        public override bool FinalizeAtContinueUrl => true;

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SagePaySettings settings)
        {
            var form = new PaymentForm(cancelUrl, FormMethod.Post);
            LoadInputFields(order, settings);
            var responseDetails = InitiateTransaction(settings.UseTestMode);

            var status = responseDetails[SagePayConstants.Response.Status];
            if (status == SagePayConstants.Response.StatusCodes.Ok || status == SagePayConstants.Response.StatusCodes.Repeated)
            {
                if(SaveToOrder(order, responseDetails, continueUrl, cancelUrl))
                {
                    form.Action = responseDetails[SagePayConstants.Response.NextUrl];
                }
            }
            else
                logger.Warn<SagePayPaymentProvider>("Sage Pay(" + order.CartNumber + ") - Generate html form error - status: " + status + " | status details: " + responseDetails["StatusDetail"]);
            

            return new PaymentFormResult()
            {
                Form = form
            };
        }

        private bool SaveToOrder(OrderReadOnly order, Dictionary<string, string> responseDetails, string continueUrl, string cancelUrl)
        {
            try
            {
                using (var uow = Vendr.Uow.Create())
                {
                    var writableOrder = order.AsWritable(uow)
                       .SetProperty(SagePayConstants.OrderProperties.SecurityKey, new PropertyValue(responseDetails[SagePayConstants.Response.SecurityKey], true))
                       .SetProperty(SagePayConstants.OrderProperties.ContinueUrl, new PropertyValue(continueUrl, true))
                       .SetProperty(SagePayConstants.OrderProperties.CancelUrl, new PropertyValue(cancelUrl, true))
                       .SetProperty(SagePayConstants.OrderProperties.TransactionId, new PropertyValue(responseDetails[SagePayConstants.Response.TransactionId], true));

                    Vendr.Services.OrderService.SaveOrder(writableOrder);
                    uow.Complete();
                }
            }
            catch (Exception ex)
            {
                logger.Error<SagePayPaymentProvider>(ex, "Error updating order with SagePay transaction details");
                return false;
            }

            return true;
            
        }

        private Dictionary<string,string> InitiateTransaction(bool useTestMode)
        {
            var rawResponse = MakePostRequest(
                GetMethodUrl(inputFields[SagePayConstants.TransactionRequestFields.TransactionType], useTestMode),
                inputFields);

            return GetFields(rawResponse);


        }

        private void LoadInputFields(OrderReadOnly order, SagePaySettings settings)
        {
            settings.MustNotBeNull(nameof(settings));
            inputFields = SagePayInputLoader.LoadInputs(order, settings, Vendr);
        }        
        
        public override string GetCancelUrl(OrderReadOnly order, SagePaySettings settings)
        {
            return string.Empty;
        }

        public override string GetErrorUrl(OrderReadOnly order, SagePaySettings settings)
        {
            return string.Empty;
        }

        public override string GetContinueUrl(OrderReadOnly order, SagePaySettings settings)
        {
            settings.MustNotBeNull("settings");
            settings.ContinueUrl.MustNotBeNull("settings.ContinueUrl");

            return settings.ContinueUrl;
        }

        public override CallbackResult ProcessCallback(OrderReadOnly order, HttpRequestBase request, SagePaySettings settings)
        {
            return new CallbackResult
            {
                TransactionInfo = new TransactionInfo
                {
                    AmountAuthorized = order.TotalPrice.Value.WithTax,
                    TransactionFee = 0m,
                    TransactionId = Guid.NewGuid().ToString("N"),
                    PaymentStatus = PaymentStatus.Authorized
                }
            };
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
                        case "PURCHASE":
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
                        case "PURCHASE":
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


        protected string MakePostRequest(string url, IDictionary<string, string> inputFields)
        {
            string requestContents = string.Empty;
            if (inputFields != null)
            {
                requestContents = string.Join("&", (
                    from i in inputFields
                    select string.Format("{0}={1}", i.Key, HttpUtility.UrlEncode(i.Value))).ToArray<string>());
            }
            return MakePostRequest(url, requestContents);
        }

        protected string MakePostRequest(string url, string request)
        {
            string empty = string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            HttpWebRequest length = (HttpWebRequest)WebRequest.Create(url);
            length.Method = "POST";
            length.ContentLength = (long)bytes.Length;
            StreamWriter streamWriter = new StreamWriter(length.GetRequestStream(), Encoding.ASCII);
            try
            {
                streamWriter.Write(Encoding.ASCII.GetString(bytes));
            }
            finally
            {
                ((IDisposable)streamWriter).Dispose();
            }
            using (Stream responseStream = length.GetResponse().GetResponseStream())
            {
                if (responseStream != null)
                {
                    StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8);
                    try
                    {
                        empty = streamReader.ReadToEnd();
                    }
                    finally
                    {
                        ((IDisposable)streamReader).Dispose();
                    }
                }
            }
            return empty;
        }

        protected Dictionary<string, string> GetFields(string response)
        {
            return response.Split(
                Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                .ToDictionary(
                    i => i.Substring(0, i.IndexOf("=", StringComparison.Ordinal)), 
                    i => i.Substring(i.IndexOf("=", StringComparison.Ordinal) + 1, i.Length - (i.IndexOf("=", StringComparison.Ordinal) + 1)));
        }

    }
}
