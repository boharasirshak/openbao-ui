using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

return await SecretsCli.RunAsync(args);

internal static class SecretsCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        var store = new TokenStore();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("OPENBAO_ADDR") ?? "http://localhost:8200/"),
        };

        try
        {
            return args[0] switch
            {
                "login" => await LoginAsync(client, store, args[1..]),
                "logout" => await LogoutAsync(client, store),
                "export" => await ExportAsync(client, store, args[1..]),
                "import" => await ImportAsync(client, store, args[1..]),
                "set" => await SetAsync(client, store, args[1..]),
                "run" => await RunProcessAsync(client, store, args[1..]),
                _ => UsageError(),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or ArgumentException or SecurityException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> LoginAsync(HttpClient client, TokenStore store, string[] args)
    {
        var username = RequiredOption(args, "--username") ?? Console.ReadLine();
        var password = RequiredOption(args, "--password") ?? ReadPassword();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Username and password are required.");
        }

        using var response = await client.PostAsJsonAsync(
            $"v1/auth/userpass/login/{Uri.EscapeDataString(username)}",
            new { password });
        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Login failed.");
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("OpenBao returned an invalid login response.");
        await store.SaveAsync(payload.Auth.ClientToken);
        Console.Error.WriteLine($"Logged in; token expires in {payload.Auth.LeaseDuration} seconds.");
        return 0;
    }

    private static async Task<int> LogoutAsync(HttpClient client, TokenStore store)
    {
        var token = await store.ReadAsync();
        if (token is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/token/revoke-self");
            request.Headers.Add("X-Vault-Token", token);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        await store.DeleteAsync();
        return 0;
    }

    private static async Task<int> ExportAsync(HttpClient client, TokenStore store, string[] args)
    {
        var document = await ReadDocumentAsync(client, store, args);
        var format = RequiredOption(args, "--format") ?? "env";
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(document.Values));
            return 0;
        }

        foreach (var pair in document.Values)
        {
            Console.WriteLine($"{pair.Key}={EscapeDotEnv(pair.Value)}");
        }

        return 0;
    }

    private static async Task<int> ImportAsync(HttpClient client, TokenStore store, string[] args)
    {
        var file = RequiredArgument(args, 0, "An import file is required.");
        var values = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(file))
                ?? throw new ArgumentException("The JSON import must be an object of string values.")
            : ParseEnv(await File.ReadAllLinesAsync(file));
        await WriteDocumentAsync(client, store, args, values, GetExpectedVersion(args));
        return 0;
    }

    private static async Task<int> SetAsync(HttpClient client, TokenStore store, string[] args)
    {
        var assignment = args.FirstOrDefault(argument =>
            !argument.StartsWith("--", StringComparison.Ordinal) && argument.Contains('='))
            ?? throw new ArgumentException("KEY=value is required.");
        var separator = assignment.IndexOf('=');
        if (separator <= 0)
        {
            throw new ArgumentException("Expected KEY=value.");
        }

        var document = await ReadDocumentAsync(client, store, args);
        var values = document.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        values[assignment[..separator]] = assignment[(separator + 1)..];
        await WriteDocumentAsync(client, store, args, values, document.Version);
        return 0;
    }

    private static async Task<int> RunProcessAsync(HttpClient client, TokenStore store, string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        if (separator < 0 || separator == args.Length - 1)
        {
            throw new ArgumentException("Use: secrets run [options] -- command [args].");
        }

        var document = await ReadDocumentAsync(client, store, args[..separator]);
        var startInfo = new ProcessStartInfo
        {
            FileName = args[separator + 1],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in args[(separator + 2)..])
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in document.Values)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start command.");
        using var registration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            TryTerminate(process);
        });
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            TryTerminate(process);
        });

        var input = Console.OpenStandardInput().CopyToAsync(process.StandardInput.BaseStream);
        var output = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var error = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await process.WaitForExitAsync();
        await Task.WhenAll(input, output, error);
        return process.ExitCode;
    }

    private static async Task<SecretDocument> ReadDocumentAsync(HttpClient client, TokenStore store, string[] args)
    {
        var token = await store.ReadRequiredAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, SecretPath(args));
        request.Headers.Add("X-Vault-Token", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReadResponse>()
            ?? throw new InvalidOperationException("OpenBao returned an invalid secret response.");
        return new SecretDocument(payload.Data.Data, payload.Data.Metadata.Version);
    }

    private static async Task WriteDocumentAsync(
        HttpClient client,
        TokenStore store,
        string[] args,
        IReadOnlyDictionary<string, string> values,
        int? expectedVersion)
    {
        var token = await store.ReadRequiredAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, SecretPath(args));
        request.Headers.Add("X-Vault-Token", token);
        object body = expectedVersion is null
            ? new { data = values }
            : new { data = values, options = new { cas = expectedVersion } };
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static string SecretPath(string[] args)
    {
        var project = RequiredOption(args, "--project");
        var environment = RequiredOption(args, "--env");
        var path = RequiredOption(args, "--path") ?? "root";
        ValidatePath(project, environment, path);
        return $"v1/{project}/data/{environment}/{path}";
    }

    private static void ValidatePath(string? project, string? environment, string path)
    {
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(environment)
            || path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_')))
        {
            throw new ArgumentException("Project, environment, or secret path is invalid.");
        }
    }

    private static Dictionary<string, string> ParseEnv(IEnumerable<string> lines) =>
        lines.Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.StartsWith("export ", StringComparison.Ordinal) ? line[7..] : line)
            .Select(line => line.IndexOf('=') is var separator && separator > 0
                ? new KeyValuePair<string, string>(line[..separator], UnescapeDotEnv(line[(separator + 1)..]))
                : throw new ArgumentException("Invalid .env line."))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static int? GetExpectedVersion(string[] args) =>
        int.TryParse(RequiredOption(args, "--version"), out var version) ? version : null;

    private static string? RequiredOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string RequiredArgument(string[] args, int index, string message) =>
        index < args.Length ? args[index] : throw new ArgumentException(message);

    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        Console.Error.Write("Password: ");
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }

    private static void TryTerminate(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static string EscapeDotEnv(string value) =>
        value.IndexOfAny([' ', '\t', '\n', '\r', '"', '\'']) >= 0
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")}\""
            : value;

    private static string UnescapeDotEnv(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\")
            : value;

    private static int UsageError()
    {
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("secrets login|logout|run|export|import|set");

    private sealed record SecretDocument(IReadOnlyDictionary<string, string> Values, int Version);
    private sealed record LoginResponse(AuthResponse Auth);
    private sealed record AuthResponse(
        [property: JsonPropertyName("client_token")] string ClientToken,
        [property: JsonPropertyName("lease_duration")] int LeaseDuration);
    private sealed record ReadResponse(SecretData Data);
    private sealed record SecretData(
        IReadOnlyDictionary<string, string> Data,
        SecretMetadata Metadata);
    private sealed record SecretMetadata(int Version);

    private sealed class TokenStore
    {
        private readonly string path = Environment.GetEnvironmentVariable("SECRETS_TOKEN_FILE")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "secrets",
                "token");

        public async Task SaveAsync(string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, token);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        public async Task<string?> ReadAsync() =>
            File.Exists(path) ? await File.ReadAllTextAsync(path) : null;

        public async Task<string> ReadRequiredAsync() =>
            await ReadAsync() ?? throw new InvalidOperationException("Run secrets login first.");

        public Task DeleteAsync()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }
    }
}
