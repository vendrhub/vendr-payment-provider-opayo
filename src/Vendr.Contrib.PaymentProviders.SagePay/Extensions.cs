using System.Text;
using Vendr.Core.Models;
using Vendr.Core.Web;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    public static class StringExtensions
    {

        public static string Truncate(this string self, int length)
        {
            if (string.IsNullOrWhiteSpace(self)) return self;
            if (self.Length <= length) return self;
            return self.Substring(0, length);
        }

        internal static string ReplacePlaceHolders(this string self, OrderReadOnly order)
        {
            return self.Replace(SagePayConstants.PlaceHolders.OrderReference, order.GenerateOrderReference().OrderNumber)
                .Replace(SagePayConstants.PlaceHolders.OrderId, order.Id.ToString());
        }
    }

    public static class ByteArrayExtensions
    {
        public static string ToHex(this byte[] self)
        {   
            StringBuilder hex = new StringBuilder(self.Length * 2);
            foreach (byte b in self)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
            
        }
    }
}
