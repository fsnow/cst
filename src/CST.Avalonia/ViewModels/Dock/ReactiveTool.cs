using System.Runtime.Serialization;
using Dock.Model.Controls;

namespace CST.Avalonia.ViewModels.Dock;

/// <summary>
/// Reactive tool base class for tool ViewModels. All shared dockable behavior lives in
/// <see cref="ReactiveDockableBase"/>; this only adds the <see cref="ITool"/> / <see cref="IDocument"/>
/// markers (tools are also treated as documents in this app's layout). (#74)
/// </summary>
[DataContract(IsReference = true)]
public class ReactiveTool : ReactiveDockableBase, ITool, IDocument
{
}
