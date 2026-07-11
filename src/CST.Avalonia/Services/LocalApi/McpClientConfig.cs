using System.Text.Json;
using System.Text.Json.Nodes;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Generates a pre-populated MCP client configuration from the current port + token, for a "Copy MCP
    /// configuration" affordance in Settings (#275). Currently the Claude Desktop snippet (the `mcp-remote`
    /// stdio bridge over the loopback `/mcp` endpoint).
    /// </summary>
    internal static class McpClientConfig
    {
        /// <summary>
        /// The <c>claude_desktop_config.json</c> snippet that connects Claude Desktop to <c>/mcp</c> via the
        /// <c>mcp-remote</c> bridge with the given port + bearer token. Paste into that file's <c>mcpServers</c>.
        /// </summary>
        public static string ClaudeDesktop(int port, string token, string serverName = "cst-reader")
        {
            var config = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [serverName] = new JsonObject
                    {
                        ["command"] = "npx",
                        ["args"] = new JsonArray(
                            "mcp-remote",
                            $"http://127.0.0.1:{port}/mcp",
                            "--transport", "http-only",
                            "--header", $"Authorization: Bearer {token}"),
                    },
                },
            };
            return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
