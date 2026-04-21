namespace Samples.OrderService.Application;

public class OrderSagaState
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public Guid? PaymentId { get; set; }
}
