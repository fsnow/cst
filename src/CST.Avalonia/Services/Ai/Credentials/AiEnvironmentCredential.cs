namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// The one place that decides whether a connection authenticates from the environment. (#714)
///
/// <para><b>Shared because two copies disagreed.</b> The first version of this work implemented the rule in
/// the chat resolver and in the connection service, and left the model-listing path reading the credential
/// store alone — so an adopted connection could answer a question and fail to list its models, reporting
/// <i>"the provider rejected the stored key"</i> for a connection that has no stored key. That contradiction
/// between two surfaces is what #673 exists to prevent, and <c>AiModelCatalog.Authenticate</c>'s own comment
/// says as much, two lines above the code that had it. (fable)</para>
/// </summary>
internal static class AiEnvironmentCredential
{
    /// <summary>
    /// The adopted key, or null.
    ///
    /// <para>Requires the reader's recorded opt-in AND the variable they consented to. Reading the recorded
    /// name rather than re-deriving it from the preset is what keeps a catalogue refresh from silently
    /// changing which credential goes to this endpoint.</para>
    /// </summary>
    internal static string? For(bool usesEnvironmentKey, string? variableName, IAiEnvironmentKeys? keys)
    {
        if (!usesEnvironmentKey || keys is null) return null;
        if (string.IsNullOrWhiteSpace(variableName)) return null;
        var value = keys.Read(variableName!);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
