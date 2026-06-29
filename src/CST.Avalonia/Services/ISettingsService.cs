using System.Threading.Tasks;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services
{
    public interface ISettingsService
    {
        Settings Settings { get; }
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();

        /// <summary>
        /// Request a debounced save. Rapid changes coalesce into a single write a short time after the
        /// last change. Use this for UI-driven setting changes instead of fire-and-forget
        /// SaveSettingsAsync. Errors are logged (not thrown). (#67)
        /// </summary>
        void RequestSave();

        /// <summary>
        /// Immediately write any pending debounced save (e.g. on shutdown). No-op if nothing is pending.
        /// </summary>
        Task FlushPendingSaveAsync();

        void UpdateSetting<T>(string propertyName, T value);
        string GetSettingsFilePath();
    }
}