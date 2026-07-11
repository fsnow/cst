using System.Text.Json;
using System.Text.Json.Nodes;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Generates a pre-populated MCP client configuration for a "Copy MCP configuration" affordance in Settings.
    /// Emits the #278 <c>--mcp-bridge</c> config: the chat client spawns CST Reader's own (signed) binary with
    /// <c>--mcp-bridge</c>, and that relay reads the current <c>local-api.json</c> — so the config carries NO port
    /// or token and survives every restart while the API stays ephemeral + per-session.
    /// </summary>
    internal static class McpClientConfig
    {
        /// <summary>
        /// The <c>claude_desktop_config.json</c> snippet that launches the CST Reader <c>--mcp-bridge</c> relay.
        /// <paramref name="command"/> is the path to the app executable (e.g. <c>Environment.ProcessPath</c>, or
        /// the bundle's launch wrapper). Paste into that file's <c>mcpServers</c>.
        /// </summary>
        public static string ClaudeDesktop(string command, string serverName = "cst-reader")
        {
            var config = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [serverName] = new JsonObject
                    {
                        ["command"] = command,
                        ["args"] = new JsonArray("--mcp-bridge"),
                    },
                },
            };
            return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
