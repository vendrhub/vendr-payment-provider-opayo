using System.Linq;
using System.Web;

namespace Vendr.Contrib.PaymentProviders.SagePay.Models
{
    public class CallbackRequestModel
    {
        public CallbackRequestModel(HttpRequestBase request)
        {
            RawRequest = request;
        }
        public string Status { get; set; }
        public string StatusDetail { get; set; }
        public string VPSTxId { get; set; }
        public string VPSProtocol { get; set; }
        public string TxType { get; set; }
        public string GiftAid { get; set; }
        public string VPSSignature { get; set; }
        public string VendorTxCode { get; set; }
        public string TxAuthNo { get; set; }
        public string AVSCV2 { get; set; }
        public string AddressResult { get; set; }
        public string AddressStatus { get; set; }
        public string PostCodeResult { get; set; }
        public string CV2Result { get; set; }
        public string SecureStatus {get;set;}
        public string CAVV { get; set; }
        public string PayerStatus { get; set; }
        public string CardType { get; set; }
        public string Last4Digits { get; set; }
        public string DeclineCode { get; set; }
        public string ExpiryDate { get; set; }
        public string FraudResponse { get; set; }
        public string BankAuthCode { get; set; }
        public decimal? Surcharge { get; set; }
        public HttpRequestBase RawRequest { get; }

        public static CallbackRequestModel FromRequest(HttpRequestBase request)
        {
            return new CallbackRequestModel(request)
            {
                Status = request.Form.Get(nameof(Status)),
                StatusDetail = request.Form.Get(nameof(StatusDetail)),
                GiftAid = request.Form.Get(nameof(GiftAid)),
                TxType = request.Form.Get(nameof(TxType)),
                VendorTxCode = request.Form.Get(nameof(VendorTxCode)),
                VPSProtocol = request.Form.Get(nameof(VPSProtocol)),
                VPSSignature = request.Form.Get(nameof(VPSSignature)),
                VPSTxId = request.Form.Get(nameof(VPSTxId)),
                TxAuthNo = request.Form.Get(nameof(TxAuthNo)),
                AVSCV2 = HttpUtility.UrlDecode(request.Form.Get(nameof(AVSCV2))),
                AddressResult = HttpUtility.UrlDecode(request.Form.Get(nameof(AddressResult))),
                AddressStatus = HttpUtility.UrlDecode(request.Form.Get(nameof(AddressStatus))),
                PostCodeResult = HttpUtility.UrlDecode(request.Form.Get(nameof(PostCodeResult))),
                CV2Result = HttpUtility.UrlDecode(request.Form.Get(nameof(CV2Result))),
                SecureStatus = request.Form.Get("3DSecureStatus"),
                CAVV = request.Form.Get(nameof(CAVV)),
                PayerStatus = HttpUtility.UrlDecode(request.Form.Get(nameof(PayerStatus))),
                CardType = request.Form.Get(nameof(CardType)),
                Last4Digits = request.Form.Get(nameof(Last4Digits)),
                DeclineCode = HttpUtility.UrlDecode(request.Form.Get(nameof(DeclineCode))),
                ExpiryDate = HttpUtility.UrlDecode(request.Form.Get(nameof(ExpiryDate))),
                FraudResponse = HttpUtility.UrlDecode(request.Form.Get(nameof(FraudResponse))),
                BankAuthCode = HttpUtility.UrlDecode(request.Form.Get(nameof(BankAuthCode))),
                Surcharge = request.Form.AllKeys.Any(k => k.Equals(nameof(Surcharge))) ? decimal.Parse(request.Form.Get(nameof(Surcharge))) : decimal.Zero
            };
        }
    }
}
