using System;
using System.IO;
using CST.Avalonia.Constants;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// #317 A6-9: the guard, the --mcp-bridge handshake, and the local-API server all resolve their data dir
    /// through AppConstants.DataDirectory, so they can never target diverging directories.
    /// </summary>
    public class AppConstantsTests
    {
        [Fact]
        public void DataDirectory_is_appdata_plus_the_app_name()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppDataDirectoryName);

            Assert.Equal(expected, AppConstants.DataDirectory);
            Assert.EndsWith(AppConstants.AppDataDirectoryName, AppConstants.DataDirectory);
        }
    }
}
