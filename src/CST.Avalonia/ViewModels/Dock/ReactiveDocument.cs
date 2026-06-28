using System.Runtime.Serialization;
using Dock.Model.Controls;

namespace CST.Avalonia.ViewModels.Dock;

/// <summary>
/// Reactive document base class for document ViewModels. All shared dockable behavior lives in
/// <see cref="ReactiveDockableBase"/>; this only adds the <see cref="IDocument"/> marker. (#74)
/// </summary>
[DataContract(IsReference = true)]
public class ReactiveDocument : ReactiveDockableBase, IDocument
{
}
