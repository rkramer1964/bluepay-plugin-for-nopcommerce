using FluentValidation;
using Nop.Plugin.Payments.BluePay.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Payments.BluePay.Validators
{
    public class PaymentInfoValidator : BaseNopValidator<PaymentInfoModel>
    {
        public PaymentInfoValidator(ILocalizationService localizationService)
        {
            RuleFor(x => x.CardNumber).IsCreditCard().WithMessageAwait(localizationService.GetResourceAsync("Payment.CardNumber.Wrong"));
            RuleFor(x => x.ExpireMonth).NotEmpty().WithMessageAwait(localizationService.GetResourceAsync("Payment.ExpireMonth.Required"));
            RuleFor(x => x.ExpireYear).NotEmpty().WithMessageAwait(localizationService.GetResourceAsync("Payment.ExpireYear.Required"));
            RuleFor(x => x.CardCode).Matches(@"^[0-9]{3,4}$").WithMessageAwait(localizationService.GetResourceAsync("Payment.CardCode.Wrong"));
        }
    }
}