namespace ControlPlane.Api;

public static class ProblemMessage
{
    /// <summary>
    /// ArgumentException appends "(Parameter 'x')" to its message. That is useful in a
    /// stack trace and noise to the person reading the error, so it is stripped before
    /// the message reaches the client.
    /// </summary>
    public static string ForClient(this ArgumentException error)
    {
        var marker = error.Message.IndexOf(" (Parameter ", StringComparison.Ordinal);
        return marker < 0 ? error.Message : error.Message[..marker];
    }
}
