using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Vendr.Core;
using Vendr.Core.Api;
using Vendr.Core.Models;

namespace Vendr.Contrib.PaymentProviders.SagePay
{
    public static class SagePayInputLoader
    {
        public static Dictionary<string, string> LoadInputs(OrderReadOnly order, SagePaySettings settings, VendrContext context)
        {
            var inputFields = new Dictionary<string, string>();

            LoadBasicSettings(inputFields, settings);
            LoadOrderValues(inputFields, order, settings, context);

            return inputFields;
        }

        private static void LoadBasicSettings(Dictionary<string, string> inputFields, SagePaySettings settings)
        {
            settings.VendorName.MustNotBeNullOrWhiteSpace(nameof(settings.VendorName));
            inputFields.Add(SagePayConstants.TransactionRequestFields.VpsProtocol, string.IsNullOrWhiteSpace(settings.VPSProtocol) ? SagePaySettings.Defaults.VPSProtocol : settings.VPSProtocol);
            inputFields.Add(SagePayConstants.TransactionRequestFields.TransactionType, (string.IsNullOrWhiteSpace(settings.TxType) ? SagePaySettings.Defaults.TxType : settings.TxType).ToUpper());
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

            var description = $"Markes Internation order - {order.TotalQuantity} items";
            if (string.IsNullOrWhiteSpace(settings.OrderPropertyDescription) == false)
            {
                var tempStore = order.Properties[settings.OrderPropertyBillingAddress2];
                if (string.IsNullOrWhiteSpace(tempStore.Value) == false)
                    description = tempStore.Value.Truncate(100);
            }
            inputFields.Add(SagePayConstants.TransactionRequestFields.Description, description);
            

            LoadBillingDetails(inputFields, order, settings, context);
            LoadDeliveryDetails(inputFields, order, settings, context);

            if (settings.IncludeDisplayOrderLines)
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
            settings.OrderPropertyBillingSurname.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyBillingSurname));
            tempStore = order.Properties[settings.OrderPropertyBillingSurname];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyBillingSurname), "Billing surname must be provided");
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

        private static void LoadDeliveryDetails(Dictionary<string, string> inputFields, OrderReadOnly order, SagePaySettings settings, VendrContext context)
        {
            string tempStore;
            settings.OrderPropertyDeliverySurname.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyDeliverySurname));
            tempStore = order.Properties[settings.OrderPropertyDeliverySurname];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyDeliverySurname), "Delivery surname must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Surname, tempStore.Truncate(20));

            settings.OrderPropertyDeliveryFirstName.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyDeliveryFirstName));
            tempStore = order.Properties[settings.OrderPropertyDeliveryFirstName];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyDeliveryFirstName), "Delviery forenames must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Firstnames, tempStore.Truncate(20));

            settings.OrderPropertyDeliveryAddress1.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyDeliveryAddress1));
            tempStore = order.Properties[settings.OrderPropertyDeliveryAddress1];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyDeliveryAddress1), "Delivery address 1 must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Address1, tempStore.Truncate(100));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyDeliveryAddress2) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyDeliveryAddress2];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Address2, tempStore.Truncate(100));
            }

            settings.OrderPropertyDeliveryCity.MustNotBeNullOrWhiteSpace(nameof(settings.OrderPropertyDeliveryCity));
            tempStore = order.Properties[settings.OrderPropertyDeliveryCity];
            if (string.IsNullOrWhiteSpace(tempStore))
                throw new ArgumentNullException(nameof(settings.OrderPropertyDeliveryCity), "Delivery city must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.City, tempStore.Truncate(40));

            if (string.IsNullOrWhiteSpace(settings.OrderPropertyDeliveryPostcode) == false)
            {
                tempStore = order.Properties[settings.OrderPropertyDeliveryPostcode];
                if (string.IsNullOrWhiteSpace(tempStore) == false)
                    inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.PostCode, tempStore.Truncate(10));
            }

            var deliveryCountry = order.ShippingInfo.CountryId.HasValue
                ? context.Services.CountryService.GetCountry(order.ShippingInfo.CountryId.Value)
                : null;

            if (deliveryCountry == null)
                throw new ArgumentNullException("deliveryCountry", "Delivery country must be provided");
            inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.Country, deliveryCountry.Code);

            if (deliveryCountry.Code == "US")
            {
                tempStore = order.Properties[settings.OrderPropertyDeliveryCounty];
                if (string.IsNullOrWhiteSpace(tempStore))
                    throw new ArgumentNullException(nameof(settings.OrderPropertyDeliveryCounty), "Delivery State must be provided for the US");
                inputFields.Add(SagePayConstants.TransactionRequestFields.Delivery.State, tempStore);
            }
        }



    }
}
