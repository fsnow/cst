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
            Assert.True(ai.LocalApi.AllowRemoteControl);
            Assert.Equal(0, ai.LocalApi.Port);        // ephemeral
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
            Assert.True(ai.RemoteControlAllowed);
        }

        [Fact]
        public void Master_on_but_local_api_off_disables_server_and_remote_control()
        {
            var ai = new AiSettings
            {
                Enabled = true,
                LocalApi = new LocalApiSettings { Enabled = false, AllowRemoteControl = true }
            };

            Assert.False(ai.LocalApiEnabled);
            Assert.False(ai.RemoteControlAllowed);
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
            Assert.DoesNotContain("remoteControlAllowed", json, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
