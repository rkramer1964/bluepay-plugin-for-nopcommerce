using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.BluePay.Controllers;
using Nop.Plugin.Payments.BluePay.Models;
using Nop.Plugin.Payments.BluePay.Validators;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.BluePay
{
    /// <summary>
    /// BluePay payment processor
    /// </summary>
    public class BluePayPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly BluePayPaymentSettings _bluePayPaymentSettings;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IWebHelper _webHelper;
        private readonly ILocalizationService _localizationService;
        private readonly ICountryService _countryService;
        private readonly IStateProvinceService _stateProvinceService;
        #endregion

        #region Ctor

        public BluePayPaymentProcessor(BluePayPaymentSettings bluePayPaymentSettings,
            ICurrencyService currencyService,
            ICustomerService customerService,
            ISettingService settingService,
            IPaymentService paymentService,
            IWebHelper webHelper,
            ILocalizationService localizationService,
            ICountryService countryService,
            IStateProvinceService stateProvinceService)
        {
            this._bluePayPaymentSettings = bluePayPaymentSettings;
            this._currencyService = currencyService;
            this._customerService = customerService;
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._webHelper = webHelper;
            this._localizationService = localizationService;
            this._countryService = countryService;
            this._stateProvinceService = stateProvinceService;
        }

        #endregion

        #region Properties

        public string GetPublicViewComponentName()
        {
            return "PaymentBluePay";
        }

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.Automatic; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Standard; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Get amount in the USD currency
        /// </summary>
        /// <param name="amount">Amount</param>
        /// <returns>Amount in the USD currency</returns>
        private async Task<decimal> GetUsdAmount(decimal amount)
        {
            var usd = await _currencyService.GetCurrencyByCodeAsync("USD");
            if (usd == null)
                throw new Exception("USD currency could not be loaded");

            return await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(amount, usd);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var customer = await _customerService.GetCustomerByIdAsync(processPaymentRequest.CustomerId);
            if (customer == null)
                throw new Exception("Customer cannot be loaded");

            var billAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
            var country = await _countryService.GetCountryByAddressAsync(billAddress);
            var stateProvince = await _stateProvinceService.GetStateProvinceByAddressAsync(billAddress);

            var result = new ProcessPaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                IsSandbox = _bluePayPaymentSettings.UseSandbox,
                CustomerIP = _webHelper.GetCurrentIpAddress(),
                CustomId1 = customer.Id.ToString(),
                CustomId2 = customer.CustomerGuid.ToString(),
                FirstName = billAddress.FirstName,
                LastName = billAddress.LastName,
                Email = billAddress.Email,
                Address1 = billAddress.Address1,
                Address2 = billAddress.Address2,
                City = billAddress.City,
                Country = country.ThreeLetterIsoCode,
                Zip = billAddress.ZipPostalCode,
                Phone = billAddress.PhoneNumber,
                State = stateProvince.Abbreviation,
                CardNumber = processPaymentRequest.CreditCardNumber,
                CardExpire = $"{new DateTime(processPaymentRequest.CreditCardExpireYear, processPaymentRequest.CreditCardExpireMonth, 1):MMyy}",
                CardCvv2 = processPaymentRequest.CreditCardCvv2,
                Amount = (await GetUsdAmount(processPaymentRequest.OrderTotal)).ToString("F", new CultureInfo("en-US")),
                OrderId = processPaymentRequest.OrderGuid.ToString(),
                InvoiceId = processPaymentRequest.OrderGuid.ToString()
            };

            bpManager.Sale(_bluePayPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture);

            if (bpManager.IsSuccessful)
            {
                result.AvsResult = bpManager.AVS;
                result.AuthorizationTransactionCode = bpManager.AuthCode;
                if (_bluePayPaymentSettings.TransactMode == TransactMode.AuthorizeAndCapture)
                {
                    result.CaptureTransactionId = bpManager.TransactionId;
                    result.CaptureTransactionResult = bpManager.Message;
                    result.NewPaymentStatus = PaymentStatus.Paid;
                }
                else
                {
                    result.AuthorizationTransactionId = bpManager.TransactionId;
                    result.AuthorizationTransactionResult = bpManager.Message;
                    result.NewPaymentStatus = PaymentStatus.Authorized;
                }
            }
            else
                result.AddError(bpManager.Message);

            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public async Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                IsSandbox = _bluePayPaymentSettings.UseSandbox,
                MasterId = capturePaymentRequest.Order.AuthorizationTransactionId,
                Amount = (await GetUsdAmount(capturePaymentRequest.Order.OrderTotal)).ToString("F", new CultureInfo("en-US"))
            };

            bpManager.Capture();

            if (bpManager.IsSuccessful)
            {
                result.NewPaymentStatus = PaymentStatus.Paid;
                result.CaptureTransactionId = bpManager.TransactionId;
                result.CaptureTransactionResult = bpManager.Message;
            }
            else
                result.AddError(bpManager.Message);

            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                IsSandbox = _bluePayPaymentSettings.UseSandbox,
                MasterId = refundPaymentRequest.Order.CaptureTransactionId,
                Amount = refundPaymentRequest.IsPartialRefund ? (await GetUsdAmount(refundPaymentRequest.AmountToRefund)).ToString("F", new CultureInfo("en-US")) : null
            };

            bpManager.Refund();

            if (!bpManager.IsSuccessful)
                result.AddError(bpManager.Message);
            else
                result.NewPaymentStatus = refundPaymentRequest.IsPartialRefund 
                    && refundPaymentRequest.Order.RefundedAmount + refundPaymentRequest.AmountToRefund < refundPaymentRequest.Order.OrderTotal 
                    ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
            
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                IsSandbox = _bluePayPaymentSettings.UseSandbox,
                MasterId = !string.IsNullOrEmpty(voidPaymentRequest.Order.AuthorizationTransactionId) ?
                    voidPaymentRequest.Order.AuthorizationTransactionId : voidPaymentRequest.Order.CaptureTransactionId
            };

            bpManager.Void();

            if (bpManager.IsSuccessful)
                result.NewPaymentStatus = PaymentStatus.Voided;
            else
                result.AddError(bpManager.Message);

            return Task.FromResult(result);
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public async Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var customer = await _customerService.GetCustomerByIdAsync(processPaymentRequest.CustomerId);
            if (customer == null)
                throw new Exception("Customer cannot be loaded");

            var billAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
            var country = await _countryService.GetCountryByAddressAsync(billAddress);
            var stateProvince = await _stateProvinceService.GetStateProvinceByAddressAsync(billAddress);
            var result = new ProcessPaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                IsSandbox = _bluePayPaymentSettings.UseSandbox,
                CustomerIP = _webHelper.GetCurrentIpAddress(),
                CustomId1 = customer.Id.ToString(),
                CustomId2 = customer.CustomerGuid.ToString(),
                FirstName = billAddress.FirstName,
                LastName = billAddress.LastName,
                Email = billAddress.Email,
                Address1 = billAddress.Address1,
                Address2 = billAddress.Address2,
                City = billAddress.City,
                Country = country != null ? country.ThreeLetterIsoCode : null,
                Zip = billAddress.ZipPostalCode,
                Phone = billAddress.PhoneNumber,
                State = stateProvince != null ? stateProvince.Abbreviation : null,
                CardNumber = processPaymentRequest.CreditCardNumber,
                CardExpire = $"{new DateTime(processPaymentRequest.CreditCardExpireYear, processPaymentRequest.CreditCardExpireMonth, 1):MMyy}",
                CardCvv2 = processPaymentRequest.CreditCardCvv2,
                Amount = (await GetUsdAmount(processPaymentRequest.OrderTotal)).ToString("F", new CultureInfo("en-US")),
                OrderId = processPaymentRequest.OrderGuid.ToString(),
                InvoiceId = processPaymentRequest.OrderGuid.ToString(),
                DoRebill = "1",
                RebillAmount = (await GetUsdAmount(processPaymentRequest.OrderTotal)).ToString("F", new CultureInfo("en-US")),
                RebillCycles = processPaymentRequest.RecurringTotalCycles > 0 ? (processPaymentRequest.RecurringTotalCycles - 1).ToString() : null,
                RebillFirstDate = $"{processPaymentRequest.RecurringCycleLength} {processPaymentRequest.RecurringCyclePeriod.ToString().TrimEnd('s').ToUpperInvariant()}",
                RebillExpression = $"{processPaymentRequest.RecurringCycleLength} {processPaymentRequest.RecurringCyclePeriod.ToString().TrimEnd('s').ToUpperInvariant()}"
            };

            bpManager.SaleRecurring();

            if (bpManager.IsSuccessful)
            {
                result.NewPaymentStatus = PaymentStatus.Paid;
                result.SubscriptionTransactionId = bpManager.RebillId;
                result.AuthorizationTransactionCode = bpManager.AuthCode;
                result.AvsResult = bpManager.AVS;
                result.AuthorizationTransactionId = bpManager.TransactionId;
                result.CaptureTransactionId = bpManager.TransactionId;
                result.CaptureTransactionResult = bpManager.Message;
            }
            else
                result.AddError(bpManager.Message);

            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            var bpManager = new BluePayManager
            {
                AccountId = _bluePayPaymentSettings.AccountId,
                UserId = _bluePayPaymentSettings.UserId,
                SecretKey = _bluePayPaymentSettings.SecretKey,
                MasterId = cancelPaymentRequest.Order.SubscriptionTransactionId
            };

            bpManager.CancelRecurring();

            if (!bpManager.IsSuccessfulCancelRecurring)
                result.AddError(bpManager.Message);

            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            var result = await _paymentService.CalculateAdditionalFeeAsync(cart,
                _bluePayPaymentSettings.AdditionalFee, _bluePayPaymentSettings.AdditionalFeePercentage);
            return result;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            var warnings = new List<string>();

            //validate
            var validator = new PaymentInfoValidator(_localizationService);
            var model = new PaymentInfoModel
            {
                CardNumber = form["CardNumber"],
                ExpireMonth = form["ExpireMonth"],
                ExpireYear = form["ExpireYear"],
                CardCode = form["CardCode"]
            };
            var validationResult = validator.Validate(model);
            if (!validationResult.IsValid)
                warnings.AddRange(validationResult.Errors.Select(error => error.ErrorMessage));

            return Task.FromResult<IList<string>>(warnings);
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest
            {
                CreditCardNumber = form["CardNumber"],
                CreditCardExpireMonth = int.Parse(form["ExpireMonth"]),
                CreditCardExpireYear = int.Parse(form["ExpireYear"]),
                CreditCardCvv2 = form["CardCode"]
            });
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentBluePay/Configure";
        }

        /// <summary>
        /// Get type of controller
        /// </summary>
        /// <returns>Controller type</returns>
        public Type GetControllerType()
        {
            return typeof(PaymentBluePayController);
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override async Task InstallAsync()
        {
            //settings
            await _settingService.SaveSettingAsync(new BluePayPaymentSettings
            {
                TransactMode = TransactMode.Authorize,
                UseSandbox = true
            });

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AccountId", "Account ID");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AccountId.Hint", "Specify BluePay account number.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFee", "Additional fee");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.SecretKey", "Secret key");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.SecretKey.Hint", "Specify API secret key.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.TransactMode", "Transaction mode");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.TransactMode.Hint", "Specify transaction mode.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UserId", "User ID");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UserId.Hint", "Specify BluePay user number.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UseSandbox", "Use sandbox");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UseSandbox.Hint", "Check to enable sandbox (testing environment).");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.BluePay.PaymentMethodDescription", "Pay by credit / debit card");

            await base.InstallAsync();
        }
        
        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<BluePayPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AccountId");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AccountId.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFee");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFee.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFeePercentage");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.AdditionalFeePercentage.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.SecretKey");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.SecretKey.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.TransactMode");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.TransactMode.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UserId");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UserId.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UseSandbox");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.Fields.UseSandbox.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.BluePay.PaymentMethodDescription");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.BluePay.PaymentMethodDescription");
        }
        #endregion
    }
}