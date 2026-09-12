using Acme.Payments;
namespace Acme.Orders;
public sealed class OrderService
{
    private readonly RawPaymentGateway _gateway = new();
    public System.Threading.Tasks.Task Pay(decimal amount) => _gateway.ChargeAsync(amount);
}
