using System.Text.Json;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Models
{
    /// <summary>
    /// The "AI" settings area (surface C gate): master "Enable AI Features" defaults OFF; sub-permissions
    /// default ON, so enabling the master turns everything on and the user pares back. Effective state is
    /// always master AND the specific permission. Adding the block is non-breaking — an old settings file
    /// with no "ai" object deserializes to AI-off.
    /// </summary>
    public class AiSettingsTests
    {
        [Fact]
        public void Defaults_master_off_subpermissions_on()
        {
            var ai = new Settings().Ai;

            Assert.False(ai.Enabled);                 // master OFF
            Assert.True(ai.LocalApi.Enabled);         // sub-permissions ON...
            Assert.True(ai.LocalApi.EnableMcpServer);
            Assert.True(ai.LocalApi.AllowRemoteControl);
            // Port/Token are no longer settings fields (#280): the loopback port is ephemeral and the token
            // per-session, both held only in local-api.json.
        }

        [Fact]
        public void Master_off_disables_everything_regardless_of_subpermissions()
        {
            var ai = new AiSettings
            {
                Enabled = false,
                LocalApi = new LocalApiSettings { Enabled = true, AllowRemoteControl = true }
            };

            Assert.False(ai.LocalApiEnabled);
            Assert.False(ai.RemoteControlAllowed);
        }

        [Fact]
        public void Master_on_with_default_subpermissions_enables_everything()
        {
            var ai = new AiSettings { Enabled = true }; // LocalApi defaults on

            Assert.True(ai.LocalApiEnabled);
            Assert.True(ai.McpEnabled);
            Assert.True(ai.ServerShouldRun);
            Assert.True(ai.RemoteControlAllowed);
        }

        [Fact]
        public void Master_off_disables_mcp_and_the_server()
        {
            var ai = new AiSettings { Enabled = false };

            Assert.False(ai.McpEnabled);
            Assert.False(ai.ServerShouldRun);
        }

        [Fact]
        public void Mcp_and_rest_are_independent_surfaces_but_either_runs_the_server()
        {
            // REST on, MCP off: server runs (for /v1) but /mcp is not exposed.
            var restOnly = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings { Enabled = true, EnableMcpServer = false }
            };
            Assert.True(restOnly.LocalApiEnabled);
            Assert.False(restOnly.McpEnabled);
            Assert.True(restOnly.ServerShouldRun);

            // MCP on, REST off: server still runs (for /mcp) even with the /v1 surface disabled.
            var mcpOnly = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings { Enabled = false, EnableMcpServer = true }
            };
            Assert.False(mcpOnly.LocalApiEnabled);
            Assert.True(mcpOnly.McpEnabled);
            Assert.True(mcpOnly.ServerShouldRun);
        }

        [Fact]
        public void Master_on_but_every_surface_off_disables_server_and_remote_control()
        {
            var ai = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings
                    { Enabled = false, EnableMcpServer = false, AllowRemoteControl = true }
            };

            Assert.False(ai.LocalApiEnabled);
            Assert.False(ai.ServerShouldRun);
            Assert.False(ai.RemoteControlAllowed);
        }

        [Fact]
        public void Rest_off_but_mcp_on_still_allows_remote_control()
        {
            // navigate (#187) is offered over BOTH /v1 and /mcp, so consent hangs off "a surface is running"
            // plus the remote-control checkbox — NOT off the REST transport specifically. Keying it to the REST
            // flag would leave an MCP-only user's every navigate denied, with a message telling them to enable a
            // checkbox that is already ticked. Unreachable through today's Settings UI (it couples the two
            // flags), but #280 gives MCP its own toggle. (fable LOW-5)
            var ai = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings
                    { Enabled = false, EnableMcpServer = true, AllowRemoteControl = true }
            };

            Assert.False(ai.LocalApiEnabled);
            Assert.True(ai.ServerShouldRun);
            Assert.True(ai.RemoteControlAllowed);
        }

        [Fact]
        public void Master_on_local_api_on_but_remote_control_off()
        {
            var ai = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings { Enabled = true, AllowRemoteControl = false }
            };

            Assert.True(ai.LocalApiEnabled);          // read-only data access still on
            Assert.False(ai.RemoteControlAllowed);    // ...but no driving the reader
        }

        [Fact]
        public void Missing_ai_block_deserializes_to_defaults_off()
        {
            // An old settings file, before the "ai" area existed.
            const string json = "{ \"version\": \"1.0\", \"xmlBooksDirectory\": \"/x\" }";
            var settings = JsonSerializer.Deserialize<Settings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(settings);
            Assert.NotNull(settings!.Ai);
            Assert.False(settings.Ai.Enabled);        // AI off for upgraded files
            Assert.False(settings.Ai.LocalApiEnabled);
        }

        [Fact]
        public void Computed_helpers_are_not_persisted()
        {
            var json = JsonSerializer.Serialize(new Settings());

            Assert.DoesNotContain("localApiEnabled", json, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mcpEnabled", json, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("serverShouldRun", json, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("remoteControlAllowed", json, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
