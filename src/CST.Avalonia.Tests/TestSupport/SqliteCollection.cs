using Xunit;

namespace CST.Avalonia.Tests.TestSupport;

/// <summary>
/// Serialises every test class that touches SQLite. (#960)
///
/// <para><b>The hazard is <c>SqliteConnection.ClearAllPools()</c>, which is process-wide.</b> Nine classes
/// call it in their cleanup to release file handles so a temp <c>.db</c> can be deleted. xunit runs
/// collections in parallel, so one class's teardown was free to dispose a pooled <c>sqlite3</c> handle that
/// another class's constructor was in the middle of using — surfacing as
/// <c>ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'</c> thrown
/// from a fixture builder, against a database file with a <c>Guid.NewGuid()</c> name that no other test could
/// possibly know.</para>
///
/// <para>It failed roughly once in ten full-suite runs and never in isolation, which is the signature: the
/// necessary condition is another SQLite class running concurrently, and running one class alone removes it.
/// Putting them in one collection removes that condition rather than papering over the symptom.</para>
///
/// <para><b>Why this rather than <c>Pooling=False</c> everywhere.</b> That would also work — an unpooled
/// handle is not in a pool for <c>ClearAllPools</c> to find — and <c>LexiconTests</c> already does it for one
/// connection. But it would leave the sledgehammer in place for anything added later that forgets, whereas a
/// collection is inherited by construction. It follows the precedent already in this project:
/// <c>[Collection("LocalApiIntegration")]</c> serialises the endpoint tests for the same class of reason.</para>
///
/// <para>The cost is that these nine no longer run in parallel with <i>each other</i>; they still run in
/// parallel with everything else. They are fixture-and-assert tests over tiny in-memory schemas, so the
/// measured suite time did not move.</para>
/// </summary>
[CollectionDefinition("Sqlite")]
public sealed class SqliteCollection
{
}
