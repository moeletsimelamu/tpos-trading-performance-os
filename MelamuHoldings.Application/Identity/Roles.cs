namespace MelamuHoldings.Application.Identity;

// Every account created with `create-owner` is an Owner. Finance endpoints require this role,
// so a later read-only role can be added without opening them up.
public static class Roles
{
    public const string Owner = "Owner";
}

public static class Policies
{
    public const string Owner = "Owner";
}
