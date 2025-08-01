using System.Threading.Tasks;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services
{
    public interface ISettingsService
    {
        Settings Settings { get; }
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();
        void UpdateSetting<T>(string propertyName, T value);
        string GetSettingsFilePath();
    }
}