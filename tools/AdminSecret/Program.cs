using Microsoft.AspNetCore.Identity;

// Offline only: no configuration/database/network access. Never put the password
// on a command line or persist it in the repository.
if (Console.IsInputRedirected) throw new InvalidOperationException("Use an interactive terminal; input is hidden.");
string ReadPassword(string prompt)
{
    Console.Error.Write(prompt);
    var value = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (value.Count > 0) value.RemoveAt(value.Count - 1); }
        else if (!char.IsControl(key.KeyChar)) value.Add(key.KeyChar);
    }
    Console.Error.WriteLine();
    return new string(value.ToArray());
}
var password = ReadPassword("New admin password (16+ characters): ");
if (password.Length < 16 || password != ReadPassword("Confirm password: "))
    throw new InvalidOperationException("Passwords must match and contain at least 16 characters.");
Console.Error.WriteLine("Store this Identity hash as the backend Admin__PasswordHash secret. Do not commit it:");
Console.WriteLine(new PasswordHasher<object>().HashPassword(new object(), password));
