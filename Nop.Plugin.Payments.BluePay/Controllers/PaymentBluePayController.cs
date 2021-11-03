using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.BluePay.Models;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Services.Messages;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.BluePay.Controllers
{
    public class PaymentBluePayController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly ISettingService _settingService;
        private readonly IPermissionService _permissionService;
        private readonly IStoreContext _storeContext;
        private readonly INotificationService _notificationService;
        #endregion

        #region Ctor

        public PaymentBluePayController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            ISettingService settingService,
            IPermissionService permissionService,
            IStoreContext storeContext,
            INotificationService notificationService)
        {
            this._localizationService = localizationService;
            this._logger = logger;
            this._orderProcessingService = orderProcessingService;
            this._orderService = orderService;
            this._settingService = settingService;
            this._permissionService = permissionService;
            this._storeContext = storeContext;
            this._notificationService = notificationService;
        }

        #endregion

        #region Methods
        [AutoValidateAntiforgeryToken]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var bluePayPaymentSettings = await _settingService.LoadSettingAsync<BluePayPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseSandbox = bluePayPaymentSettings.UseSandbox,
                TransactModeId = Convert.ToInt32(bluePayPaymentSettings.TransactMode),
                AccountId = bluePayPaymentSettings.AccountId,
                UserId = bluePayPaymentSettings.UserId,
                SecretKey = bluePayPaymentSettings.SecretKey,
                AdditionalFee = bluePayPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = bluePayPaymentSettings.AdditionalFeePercentage,
                TransactModeValues = await bluePayPaymentSettings.TransactMode.ToSelectListAsync(),
                ActiveStoreScopeConfiguration = storeScope
            };
            if (storeScope > 0)
            {
                model.UseSandbox_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.UseSandbox, storeScope);
                model.TransactModeId_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.TransactMode, storeScope);
                model.AccountId_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.AccountId, storeScope);
                model.UserId_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.UserId, storeScope);
                model.SecretKey_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.SecretKey, storeScope);
                model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(bluePayPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.BluePay/Views/Configure.cshtml", model);
        }

        [AutoValidateAntiforgeryToken]
        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var bluePayPaymentSettings = await _settingService.LoadSettingAsync<BluePayPaymentSettings>(storeScope);

            //save settings
            bluePayPaymentSettings.UseSandbox = model.UseSandbox;
            bluePayPaymentSettings.TransactMode = (TransactMode)model.TransactModeId;
            bluePayPaymentSettings.AccountId = model.AccountId;
            bluePayPaymentSettings.UserId = model.UserId;
            bluePayPaymentSettings.SecretKey = model.SecretKey;
            bluePayPaymentSettings.AdditionalFee = model.AdditionalFee;
            bluePayPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.TransactMode, model.TransactModeId_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.AccountId, model.AccountId_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.UserId, model.UserId_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.SecretKey, model.SecretKey_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(bluePayPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        [AutoValidateAntiforgeryToken]
        [HttpPost]
        public async Task<ActionResult> Rebilling()
        {
            var parameters = Request.Form;

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var bluePayPaymentSettings = await _settingService.LoadSettingAsync<BluePayPaymentSettings>(storeScope);
            var bpManager = new BluePayManager
            {
                AccountId = bluePayPaymentSettings.AccountId,
                UserId = bluePayPaymentSettings.UserId,
                SecretKey = bluePayPaymentSettings.SecretKey
            };

            if (!bpManager.CheckRebillStamp(parameters))
            {
                await _logger.ErrorAsync("BluePay recurring error: the response has been tampered with");
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            var authId = bpManager.GetAuthorizationIdByRebillId(parameters["rebill_id"]);
            if (string.IsNullOrEmpty(authId))
            {
                await _logger.ErrorAsync($"BluePay recurring error: the initial transaction for rebill {parameters["rebill_id"]} was not found");
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            var initialOrder = await GetOrderByAuthorizationTransactionIdAndPaymentMethodAsync(authId, "Payments.BluePay");
            if (initialOrder == null)
            {
                await _logger.ErrorAsync($"BluePay recurring error: the initial order with the AuthorizationTransactionId {parameters["rebill_id"]} was not found");
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            var recurringPayment = (await _orderService.SearchRecurringPaymentsAsync(initialOrderId: initialOrder.Id)).FirstOrDefault();
            var processPaymentResult = new ProcessPaymentResult();
            if (recurringPayment != null)
            {
                switch (parameters["status"])
                {
                    case "expired":
                    case "active":
                        processPaymentResult.NewPaymentStatus = PaymentStatus.Paid;
                        await _orderProcessingService.ProcessNextRecurringPaymentAsync(recurringPayment, processPaymentResult);
                        break;
                    case "failed":
                    case "error":
                        processPaymentResult.RecurringPaymentFailed = true;
                        processPaymentResult.Errors.Add($"BluePay recurring order {initialOrder.Id} {parameters["status"]}");
                        await _orderProcessingService.ProcessNextRecurringPaymentAsync(recurringPayment, processPaymentResult);
                        break;
                    case "deleted":
                    case "stopped":
                        await _orderProcessingService.CancelRecurringPaymentAsync(recurringPayment);
                        await _logger.InformationAsync($"BluePay recurring order {initialOrder.Id} was {parameters["status"]}");
                        break;
                }
            }

            return new StatusCodeResult((int)HttpStatusCode.OK);
        }

        private async Task<Order> GetOrderByAuthorizationTransactionIdAndPaymentMethodAsync(string authId, string v)
        {
            var order = (await _orderService.SearchOrdersAsync(paymentMethodSystemName: v))
                .Where(o => o.AuthorizationTransactionId.Equals(authId, StringComparison.InvariantCultureIgnoreCase))
                .FirstOrDefault();

            return order;
        }

        #endregion
    }
}