using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Services.Events;
using Nop.Services.Orders;

namespace Nop.Plugin.Payments.BluePay.Infrastructure.Cache
{
    /// <summary>
    /// RecurringPaymentInserted event consumer
    /// </summary>
    public partial class RecurringPaymentInsertedConsumer : IConsumer<EntityInsertedEvent<RecurringPayment>>
    {
        private readonly IOrderService _orderService;

        public RecurringPaymentInsertedConsumer(IOrderService orderService)
        {
            this._orderService = orderService;
        }

        /// <summary>
        /// Handles the event.
        /// </summary>
        /// <param name="payment">The recurring payment.</param>
        public async Task HandleEventAsync(EntityInsertedEvent<RecurringPayment> eventMessage)
        {
            var recurringPayment = eventMessage.Entity;
            if (recurringPayment == null)
                return;

            var rp = await _orderService.GetRecurringPaymentHistoryAsync(recurringPayment);
            var io = await _orderService.GetOrderByIdAsync(recurringPayment.InitialOrderId);
            //first payment already was paid on the BluePay, let's add it to history
            if (rp.Count == 0 &&
                io != null &&
                io.PaymentMethodSystemName == "Payments.BluePay")
            {
                await _orderService.InsertRecurringPaymentHistoryAsync(new RecurringPaymentHistory
                    {
                        RecurringPaymentId = recurringPayment.Id,
                        OrderId = recurringPayment.InitialOrderId,
                        CreatedOnUtc = DateTime.UtcNow
                    });
            }
        }
    }
}
