# Good and Bad Tests

## Good Tests

**Integration-style**: Test through real interfaces, not mocks of internal parts.

```csharp
// GOOD: Tests observable behavior
[Fact]
public async Task User_Can_Checkout_With_Valid_Cart()
{
    var cart = CartFactory.Create();
    cart.Add(product);

    var result = await checkout.Process(cart, paymentMethod);

    Assert.Equal(CheckoutStatus.Confirmed, result.Status);
}
```

Characteristics:

- Tests behavior users/callers care about
- Uses public API only
- Survives internal refactors
- Describes WHAT, not HOW
- One logical assertion per test

## Bad Tests

**Implementation-detail tests**: Coupled to internal structure.

```csharp
// BAD: Tests implementation details
[Fact]
public async Task Checkout_Calls_PaymentService_Process()
{
    var mockPayment = new Mock<IPaymentService>();
    var sut = new Checkout(mockPayment.Object);

    await sut.Process(cart, payment);

    mockPayment.Verify(p => p.Process(cart.Total), Times.Once);
}
```

Red flags:

- Mocking internal collaborators
- Testing private methods
- Asserting on call counts/order
- Test breaks when refactoring without behavior change
- Test name describes HOW not WHAT
- Verifying through external means instead of interface

```csharp
// BAD: Bypasses interface to verify
[Fact]
public async Task CreateUser_Saves_To_Database()
{
    await users.Create(new UserData { Name = "Alice" });

    var row = await db.QuerySingleAsync<UserRow>(
        "SELECT * FROM users WHERE name = @name",
        new { name = "Alice" });

    Assert.NotNull(row);
}

// GOOD: Verifies through interface
[Fact]
public async Task CreateUser_Makes_User_Retrievable()
{
    var user = await users.Create(new UserData { Name = "Alice" });

    var retrieved = await users.Get(user.Id);

    Assert.Equal("Alice", retrieved.Name);
}
```
