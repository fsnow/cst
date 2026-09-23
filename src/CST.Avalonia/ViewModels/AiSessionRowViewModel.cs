using System;
using System.Collections.Generic;
using CST.Avalonia.Services.Ai;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// One conversation in the Assistant's session list, as the panel shows it. (#997, ASSISTANT_SESSIONS.md §3.3)
///
/// <para><b>[fsnow]</b>: <i>"Claude Code itself is my model and we should aim for feature parity with CC in context
/// management, named sessions, session restoration, etc."</i> — this is the row of the <c>/resume</c> picker
/// transposed: a name, when, how many turns, and (in place of a directory) the books the conversation was
/// about.</para>
///
/// <para><b>Updated in place, not rebuilt.</b> The list is refreshed after every turn, rename, delete and switch
/// (<see cref="AiAssistantViewModel.RefreshSessionsAsync"/>), and a rebuilt row is a new object: whatever the view
/// was doing with the old one — an open rename box, keyboard focus, the selection — would go with it, at the end
/// of every answer. So a row keeps its identity for as long as its session exists, and only its values change.
/// [suggestion]</para>
/// </summary>
public sealed class AiSessionRowViewModel : ReactiveObject
{
    private string _name;
    private DateTimeOffset _lastActive;
    private int _turnCount;
    private IReadOnlyList<string> _bookIds;
    private bool _isActive;
    private bool _canDelete = true;

    internal AiSessionRowViewModel(AiSessionSummary summary)
    {
        Id = summary.Id;
        _name = summary.Name;
        _lastActive = summary.LastActive;
        _turnCount = summary.TurnCount;
        _bookIds = summary.BookIds;
    }

    /// <summary>The session's id — the parameter <see cref="AiAssistantViewModel.SwitchToSessionCommand"/> and
    /// <see cref="AiAssistantViewModel.DeleteSessionCommand"/> take. Never changes for a row.</summary>
    public string Id { get; }

    /// <summary>The auto-name or the reader's own. <b>[fsnow]</b>: <i>"Auto from the first turn, renamable"</i>.
    /// </summary>
    public string Name
    {
        get => _name;
        private set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>When a turn last ended in it — what the list is ordered by, newest first. A rename does not move
    /// it (see <see cref="AiAssistantViewModel.RenameSessionAsync"/>).</summary>
    public DateTimeOffset LastActive
    {
        get => _lastActive;
        private set => this.RaiseAndSetIfChanged(ref _lastActive, value);
    }

    public int TurnCount
    {
        get => _turnCount;
        private set => this.RaiseAndSetIfChanged(ref _turnCount, value);
    }

    /// <summary>
    /// The books its citations name, distinct, in first-appearance order — <b>ids</b>, i.e. the XML file names
    /// (<c>s0101m.mul.xml</c>), as <see cref="AiSessionSummary.BookIds"/> carries them. [observed] Turning them into
    /// readable names is the view's job today; nothing in the summary carries a book name.
    /// </summary>
    public IReadOnlyList<string> BookIds
    {
        get => _bookIds;
        private set => this.RaiseAndSetIfChanged(ref _bookIds, value);
    }

    /// <summary>Whether this is the conversation on screen in the panel.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>
    /// Whether Delete may be offered on this row now. False only for the conversation a turn is running in (or
    /// being switched to): deleting a session whose turn is still streaming would have that turn's save write
    /// the file straight back. Every other row can go at any time.
    ///
    /// <para>Per row rather than one flag on the panel, because the rule is per row — a single
    /// <c>CanDeleteSession</c> would have to disable every row's Delete to protect one of them. Kept current by
    /// the panel whenever <see cref="AiAssistantViewModel.IsBusy"/> changes.</para>
    /// </summary>
    public bool CanDelete
    {
        get => _canDelete;
        internal set => this.RaiseAndSetIfChanged(ref _canDelete, value);
    }

    // ---- The view's own state for this row: a rename box open, a delete awaiting its second click. ----
    // Held here, not in the view, because a row keeps its identity across refreshes (see the class comment): an
    // open rename box survives the refresh at the end of an answer. At most one of the two is open at a time.

    private bool _isRenaming;
    private string _renameText = string.Empty;
    private bool _isConfirmingDelete;

    /// <summary>Whether the row shows its rename box in place of its name.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRenaming, value);
            this.RaisePropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>The rename box's text. Starts as the current name.</summary>
    public string RenameText
    {
        get => _renameText;
        set => this.RaiseAndSetIfChanged(ref _renameText, value ?? string.Empty);
    }

    /// <summary>
    /// Whether the row is asking "Delete this conversation?". <b>[fsnow]</b> chose delete <i>"Yes, with
    /// confirmation"</i>; the form — an inline second click, as the Providers rows do — is [suggestion].
    /// </summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isConfirmingDelete, value);
            this.RaisePropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>Neither renaming nor confirming: the row shows its name and its Rename and Delete actions.</summary>
    public bool IsIdle => !IsRenaming && !IsConfirmingDelete;

    public void BeginRename()
    {
        IsConfirmingDelete = false;
        RenameText = Name;
        IsRenaming = true;
    }

    public void CancelRename() => IsRenaming = false;

    /// <summary>
    /// Close the rename box and say what to send, or null when there is nothing to send: a blank name (which the
    /// panel would refuse anyway) or the name unchanged (which would be a write for nothing).
    /// </summary>
    public AiSessionRename? CommitRename()
    {
        IsRenaming = false;
        var name = RenameText.Trim();
        return name.Length == 0 || name == Name ? null : new AiSessionRename(Id, name);
    }

    public void BeginDelete()
    {
        IsRenaming = false;
        IsConfirmingDelete = true;
    }

    public void CancelDelete() => IsConfirmingDelete = false;

    /// <summary>Take the values of a fresh summary of the same session.</summary>
    internal void Update(AiSessionSummary summary)
    {
        Name = summary.Name;
        LastActive = summary.LastActive;
        TurnCount = summary.TurnCount;
        BookIds = summary.BookIds;
    }
}

/// <summary>
/// What <see cref="AiAssistantViewModel.RenameSessionCommand"/> takes: which conversation, and the name the reader
/// typed. One object because a command carries one parameter. (#997)
/// </summary>
public sealed record AiSessionRename(string Id, string Name);
