using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Unit tests for ScriptService's single, deterministic initialization path and script-change behaviour (#81).
/// The old OnStateChanged auto-init path was removed; InitializeFromState is now the only way state seeds the
/// service, so these pin that contract down.
/// </summary>
public class ScriptServiceTests
{
    private static (ScriptService svc, ApplicationState state, Mock<IApplicationStateService> mock) Create(
        Script saved = Script.Devanagari)
    {
        var state = new ApplicationState();
        state.Preferences.CurrentScript = saved;
        var mock = new Mock<IApplicationStateService>();
        mock.Setup(s => s.Current).Returns(state);
        mock.Setup(s => s.SaveStateAsync()).ReturnsAsync(true);
        var svc = new ScriptService(NullLogger<ScriptService>.Instance, mock.Object);
        return (svc, state, mock);
    }

    [Fact]
    public void Default_IsLatin()
        => Assert.Equal(Script.Latin, Create().svc.CurrentScript);

    [Fact]
    public void InitializeFromState_RestoresSavedScript_AndFiresEvent()
    {
        var (svc, _, _) = Create(Script.Devanagari); // differs from the Latin default -> restored + fires
        Script? fired = null;
        svc.ScriptChanged += s => fired = s;

        svc.InitializeFromState();

        Assert.Equal(Script.Devanagari, svc.CurrentScript);
        Assert.Equal(Script.Devanagari, fired);
    }

    [Fact]
    public void InitializeFromState_SavedMatchesDefault_NoChangeNoEvent()
    {
        var (svc, _, _) = Create(Script.Latin); // saved == default -> nothing to restore
        bool fired = false;
        svc.ScriptChanged += _ => fired = true;

        svc.InitializeFromState();

        Assert.Equal(Script.Latin, svc.CurrentScript);
        Assert.False(fired);
    }

    [Fact]
    public void InitializeFromState_IsIdempotent()
    {
        var (svc, _, _) = Create(Script.Thai);
        svc.InitializeFromState();

        int count = 0;
        svc.ScriptChanged += _ => count++;
        svc.InitializeFromState(); // already in sync -> no-op, no event

        Assert.Equal(0, count);
        Assert.Equal(Script.Thai, svc.CurrentScript);
    }

    [Fact]
    public void NoStateService_InitializeIsNoOp_DefaultsLatin()
    {
        var svc = new ScriptService(NullLogger<ScriptService>.Instance, null);
        svc.InitializeFromState(); // must not throw with no state service
        Assert.Equal(Script.Latin, svc.CurrentScript);
    }

    [Fact]
    public void SettingScript_WritesToState_MarksDirty_AndFires()
    {
        var (svc, state, mock) = Create();
        Script? fired = null;
        svc.ScriptChanged += s => fired = s;

        svc.CurrentScript = Script.Bengali;

        Assert.Equal(Script.Bengali, svc.CurrentScript);
        Assert.Equal(Script.Bengali, state.Preferences.CurrentScript); // written back into state
        Assert.Equal(Script.Bengali, fired);
        mock.Verify(s => s.MarkDirty(), Times.Once); // persisted via dirty flag, not a forced save (STATE-2)
    }

    [Fact]
    public void SettingSameScript_NoEvent_NoSave()
    {
        var (svc, _, mock) = Create();
        bool fired = false;
        svc.ScriptChanged += _ => fired = true;

        svc.CurrentScript = Script.Latin; // unchanged from default

        Assert.False(fired);
        mock.Verify(s => s.MarkDirty(), Times.Never);
    }

    [Fact]
    public void GetScriptDisplayName_LatinIsRoman()
    {
        var (svc, _, _) = Create();
        Assert.Equal("Roman", svc.GetScriptDisplayName(Script.Latin));
        Assert.Equal("Devanagari", svc.GetScriptDisplayName(Script.Devanagari));
    }

    [Fact]
    public void ConvertToCurrentScript_UsesCurrentScript()
    {
        var (svc, _, _) = Create();
        svc.CurrentScript = Script.Latin;
        // Devanagari ka (U+0915, inherent 'a') -> Latin; the converter capitalizes the word-initial letter.
        Assert.Equal("Ka", svc.ConvertToCurrentScript("\u0915"));
    }
}
