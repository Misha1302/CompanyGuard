namespace Acme.Payments;
public sealed class RawPaymentGateway
{
    public System.Threading.Tasks.Task ChargeAsync(decimal amount) => System.Threading.Tasks.Task.CompletedTask;
}
