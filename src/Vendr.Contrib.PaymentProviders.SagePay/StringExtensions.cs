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
    }
}
