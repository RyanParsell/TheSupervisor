# When to Mock

Mock at **system boundaries** only:

- External APIs (payment, email, etc.)
- Databases (sometimes - prefer a real test DB)
- Time/randomness
- File system (sometimes)

Don't mock:

- Your own classes/modules
- Internal collaborators
- Anything you control

## Designing for Mockability

At system boundaries, design interfaces that are easy to mock:

**1. Use dependency injection**

Pass external dependencies in rather than constructing them internally:

```csharp
// Easy to mock
public Task<ChargeResult> ProcessPayment(Order order, IPaymentClient paymentClient)
    => paymentClient.Charge(order.Total);

// Hard to mock
public Task<ChargeResult> ProcessPayment(Order order)
{
    var client = new StripeClient(Environment.GetEnvironmentVariable("STRIPE_KEY"));
    return client.Charge(order.Total);
}
```

**2. Prefer SDK-style interfaces over generic clients**

Create a specific method for each external operation instead of one generic method with conditional logic:

```csharp
// GOOD: Each method is independently mockable
public interface IOrderApi
{
    Task<User> GetUser(Guid id);
    Task<IReadOnlyList<Order>> GetOrders(Guid userId);
    Task<Order> CreateOrder(OrderRequest data);
}

// BAD: Mocking requires conditional logic inside the mock
public interface IHttpApi
{
    Task<HttpResponseMessage> Send(string endpoint, HttpRequestOptions options);
}
```

The SDK-style approach means:

- Each mock returns one specific shape
- No conditional logic in test setup
- Easier to see which endpoints a test exercises
- Type safety per endpoint
