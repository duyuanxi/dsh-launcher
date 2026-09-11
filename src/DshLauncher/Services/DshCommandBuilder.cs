namespace DshLauncher.Services
{
    /// <summary>
    /// Builds the argv and URL used to launch <c>dsh web</c>.
    /// The launcher always passes --no-open and opens the browser itself.
    /// </summary>
    public static class DshCommandBuilder
    {
        public static string[] BuildArguments(int port, string host)
        {
            return new[]
            {
                "web",
                "--port", port.ToString(),
                "--host", host,
                "--no-open"
            };
        }

        public static string BuildUrl(string host, int port) => "http://" + host + ":" + port;
    }
}
