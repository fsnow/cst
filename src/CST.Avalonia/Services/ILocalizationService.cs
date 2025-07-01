using System;
using System.Globalization;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace CST.Avalonia.Services;

public interface ILocalizationService
{
    IObservable<CultureInfo> CurrentCulture { get; }
    CultureInfo GetCurrentCulture();
    Task ChangeCultureAsync(CultureInfo culture);
    string GetString(string key);
    CultureInfoDisplayItem[] GetAvailableLanguages();
}

public class CultureInfoDisplayItem
{
    public CultureInfo CultureInfo { get; set; }
    public string DisplayName { get; set; }
    
    public CultureInfoDisplayItem(CultureInfo cultureInfo, string displayName)
    {
        CultureInfo = cultureInfo;
        DisplayName = displayName;
    }
    
    public override string ToString() => DisplayName;
}