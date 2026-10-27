using System.Text;
using MelamuHoldings.Application.Services;

namespace MelamuHoldings.API.Cli;

// Creates the operator's login. Run from the API project: `dotnet run -- create-owner`
public static class CreateOwnerCommand
{
    public const string Name = "create-owner";

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<OwnerAccountService>();

        var email = Prompt("Email: ");
        var displayName = Prompt("Display name: ");
        var password = PromptSecret($"Password (min {OwnerAccountService.MinimumPasswordLength} characters): ");
        var confirmation = PromptSecret("Confirm password: ");

        if (password != confirmation)
        {
            Console.Error.WriteLine("Passwords do not match. No account was created.");
            return 1;
        }

        var result = await accounts.CreateOwnerAsync(email, displayName, password);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"{result.Error} No account was created.");
            return 1;
        }

        Console.WriteLine("Owner account created.");
        return 0;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine() ?? string.Empty;
    }

    // Masked when typed at a terminal; read as a plain line when input is piped in
    private static string PromptSecret(string label)
    {
        Console.Write(label);
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.WriteLine();
        return buffer.ToString();
    }
}
