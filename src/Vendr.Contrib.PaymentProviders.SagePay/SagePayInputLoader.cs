using System;
using System.Collections.Generic;
using System.Globalization;
using Vendr.Core;
using Vendr.Core.Api;
using Vendr.Core.Models;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    public static class SagePayInputLoader
    {
        public static Dictionary<string, string> LoadInputs(OrderReadOnly order, SagePaySettings settings, VendrContext context, string callbackUrl)
        {
            var inputFields = new Dictionary<string, string>();

            LoadBasicSettings(inputFields, settings, callbackUrl);
            LoadOrderValues(inputFields, order, settings, context);

            return inputFields;
        }

        private static void LoadBasicSettings(Dictionary<string, string> inputFields, SagePaySettings settings, string callbackUrl)
        {
            settings.VendorName.MustNotBeNullOrWhiteSpace(nameof(settings.VendorName));
            inputFields.Add(SagePayConstants.TransactionRequestFields.VpsProtocol, SagePaySettings.Defaults.VPSProtocol);
            inputFields.Add(SagePayConstants.TransactionRequestFields.TransactionType, (string.IsNullOrWhiteSpace(settings.TxType) ? SagePaySettings.Defaults.TxType : settings.TxType).ToUpper());
            inputFields.Add(SagePayConstants.TransactionRequestFields.Vendor, settings.VendorName);
            inputFields.Add(SagePayConstants.TransactionRequestFields.NotificationURL, callbackUrl);
        }

        private static void LoadOrderValues(Dictionary<string, string> inputFields, OrderReadOnly order, SagePaySettings settings, VendrContext context)
        {

            inputFields.Add(SagePayConstants.TransactionRequestFields.VendorTxCode, order.OrderNumber);

            var currency = context.Services.CurrencyService.GetCurrency(order.CurrencyId);
            var currencyCode = currency.Code.ToUpperInvariant();

            // Ensure currency has valid ISO 4217 code
            if (!Iso4217.CurrencyCodes.ContainsKey(currencyCode))
                throw new Exception("Currency must be a valid ISO 4217 currency code: " + currency.Name);

            inputFields.Add(SagePayConstants.TransactionRequestFields.Currency, currencyCode);
            inputFields.Add(SagePayConstants.TransactionRequestFields.Amount, order.TotalPrice.Value.WithTax.ToString("0.00", CultureInfo.InvariantCulture));

            var description = $"Vendr order - {order.TotalQuantity} items";
            if (string.IsNullOrWhiteSpace(settings.OrderPropertyDescription) == false)
            {
                var tempStore = order.Properties[settings.OrderPropertyDescription];
                if (string.IsNullOrWhiteSpace(tempStore?.Value) == false)
                    description = tempStore.Value.Truncate(100);
            }
            inputFields.Add(SagePayConstants.TransactionRequestFields.Description, description);
            

            LoadBillingDetails(inputFields, order, settings, context);
            LoadShippingDetails(inputFields, order, settings, context);

            if (settings.DisplayOrderLines)
                LoadOrderLines(inputFields, order);

        }

        private static void LoadOrderLines(Dictionary<string, string> inputFields, OrderReadOnly order)
        {
            var orderLines = new List<string>();
            foreach(var item in order.OrderLines)
            {
                orderLines.Add($"{item.ProductReference}:{item.Quantity}:{item.UnitPrice.Value.WithoutTax:0.00}:{item.UnitPrice.Value.Tax:0.00}:{item.UnitPrice.Value.WithTax:0.00}:{item.TotalPrice.Value.WithTax:0.00}");
            }

            orderLines.Insert(0, orderLines.Count.ToString());

            inputFields.Add(SagePayConstants.TransactionRequestFields.Basket, string.Join(":", orderLines));
        }

        private static void LoadBillingDetails(Dictionary<string, string> inputFields, OrderReadOnly order, SagePaySettings settings, VendrContext context)
        {
            string tempStore;
            settings.OrderPropertyBillingLastName.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyBillingLastName));
            tempStore = order.Properties[settings.OrderPropertyBillingLastName];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyBillingLastName), "Billing last name must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.Surname, tempStore.Truncate(20));

            settings.OrderPropertyBillingFirstName.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyBillingFirstName));
            tempStore = order.Properties[settings.OrderPropertyBillingFirstName];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyBillingFirstName), "Billing forenames must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.Firstnames, tempStore.Truncate(20));

            settings.OrderPropertyBillingAddress1.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyBillingAddress1));
            tempStore = order.Properties[settings.OrderPropertyBillingAddress1];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyBillingAddress1), "Billing address 1 must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.Address1, tempStore.Truncate(100));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyBillingAddress2) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyBillingAddress2];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.Address2, tempStore.Truncate(100));
            }

            settings.OrderPropertyBillingCity.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyBillingCity));
            tempStore = order.Properties[settings.OrderPropertyBillingCity];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyBillingCity), "Billing city must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.City, tempStore.Truncate(40));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyBillingPostcode) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyBillingPostcode];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.PostCode, tempStore.Truncate(10));
            }

            var billingCountry = order.PaymentInfo.CountryId.HasValue
                ? context.Services.CountryService.GetCountry(order.PaymentInfo.CountryId.Value)
                : null;

            if (billingCountry == null)
                throw new ArgumentNullException("billingCountry", "Billing country must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.Country, billingCountry.Code);

            if (billingCountry.Code == "US")
            {
                tempStore = order.Properties[settings.OrderPropertyBillingCounty];
                if (string.IsNullOrWhiteSpace(tempStore))
                    throw new ArgumentNullException(nameof(settings.OrderPropertyBillingCounty), "Billing State must be provided for the US");
                inputFields.Add(SagePayConstants.TransactionRequestFields.Billing.State, tempStore);
            }
        }

        private static void LoadShippingDetails(Dictionary<string, string> inputFields, OrderReadOnly order, SagePaySettings settings, VendrContext context)
        {
            string tempStore;
            settings.OrderPropertyShippingLastName.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyShippingLastName));
            tempStore = order.Properties[settings.OrderPropertyShippingLastName];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyShippingLastName), "Shiping last name must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Surname, tempStore.Truncate(20));

            settings.OrderPropertyShippingFirstName.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyShippingFirstName));
            tempStore = order.Properties[settings.OrderPropertyShippingFirstName];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyShippingFirstName), "Delviery forenames must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Firstnames, tempStore.Truncate(20));

            settings.OrderPropertyShippingAddress1.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyShippingAddress1));
            tempStore = order.Properties[settings.OrderPropertyShippingAddress1];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyShippingAddress1), "Shipping address 1 must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Address1, tempStore.Truncate(100));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyShippingAddress2) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyShippingAddress2];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Address2, tempStore.Truncate(100));
            }

            settings.OrderPropertyShippingCity.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyShippingCity));
            tempStore = order.Properties[settings.OrderPropertyShippingCity];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyShippingCity), "Shipping city must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.City, tempStore.Truncate(40));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyShippingPostcode) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyShippingPostcode];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.PostCode, tempStore.Truncate(10));
            }

            var shippingCountry = order.ShippingInfo.CountryId.HasValue
                ? context.Services.CountryService.GetCountry(order.ShippingInfo.CountryId.Value)
                : null;

            if (shippingCountry == null)
                throw new ArgumentNullException("shippingCountry", "Shipping country must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Country, shippingCountry.Code);

            if (shippingCountry.Code == "US")
            {
                tempStore = order.Properties[settings.OrderPropertyShippingCounty];
                if (string.IsNullOrWhiteSpace(tempStore))
                    throw new ArgumentNullException(nameof(settings.OrderPropertyShippingCounty), "Shipping State must be provided for the US");
                inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.State, tempStore);
            }
        }



    }
}
