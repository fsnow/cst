using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #692 and #674: the Models tab — the reader's short list, and the provider listing they build it from.
/// </summary>
public class AiModelsViewModelTests
{
    /// <summary>Returns a fixed listing without a network. Records how often it was asked, because "fetch
    /// once, not on every keystroke" is a property worth pinning.</summary>
    private sealed class FakeCatalog : IAiModelCatalog
    {
        private readonly AiCatalogResult _result;
        public int Calls { get; private set; }

        public FakeCatalog(params AiCatalogModel[] models)
            : this(AiCatalogResult.Success(models)) { }

        public AiConnection? Asked { get; private set; }

        public FakeCatalog(AiCatalogResult result) => _result = result;

        public Task<AiCatalogResult> FetchAsync(AiConnection connection, CancellationToken ct = default)
        {
            Calls++;
            Asked = connection;
            return Task.FromResult(_result);
        }
    }

    private static (AiModelsViewModel Vm, AiConnectionService Service) Make(
        IAiModelCatalog? catalog = null, IAiProviderLogos? logos = null)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object);
        return (new AiModelsViewModel(service, catalog, logos), service);
    }

    private static AiConnectionDraft Draft(params AiModelEntry[] models) =>
        new("My box", ChatProviderKind.OpenAiCompatible, "http://localhost:8000/v1",
            models, Array.Empty<AiHeader>(), new Dictionary<string, string>());

    private static AiModelGroupViewModel Group(AiModelsViewModel vm) => vm.Groups.Single();

    private static IReadOnlyList<AiCatalogRowViewModel> Rows(AiModelsViewModel vm) =>
        vm.Rows.OfType<AiCatalogRowViewModel>().ToList();

    // ---- the hand-typed list (#692) --------------------------------------------------------------------

    /// <summary>
    /// A model the reader typed arrives on.
    ///
    /// <para>Typing an id <i>is</i> the act of choosing it — nobody types one they do not mean to use — so
    /// all-off here would mean typing a model and then hunting for a switch on another tab before it appeared
    /// anywhere.</para>
    /// </summary>
    [Fact]
    public void A_typed_model_starts_enabled()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("llama3.1:8b", "Llama 3.1 8B")));

        Group(vm).IsExpanded = true;

        Assert.True(Assert.Single(Rows(vm)).Enabled);
    }

    /// <summary>Groups start collapsed with the count in the header — exactly the number a reader needs to
    /// decide whether to expand, which OpenCode omits.</summary>
    [Fact]
    public void Groups_start_collapsed_and_carry_a_count()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(
            new AiModelEntry("a", "A"), new AiModelEntry("b", "B")));

        Assert.False(Group(vm).IsExpanded);
        Assert.Empty(Rows(vm));
        Assert.Equal("2 models saved", Group(vm).CountText);   // collapsed: nothing fetched yet
    }

    /// <summary>Several models under one connection must be on at once — the whole point of a short list you
    /// switch between. A radio would cap it at one and destroy the design silently.</summary>
    [Fact]
    public void Several_models_can_be_on_at_once()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(
            new AiModelEntry("a", "A"), new AiModelEntry("b", "B"), new AiModelEntry("c", "C")));
        Group(vm).IsExpanded = true;

        Assert.All(Rows(vm), r => Assert.True(r.Enabled));
        Assert.Equal(3, service.Connections.Single().Models.Count(m => m.Enabled));
    }

    /// <summary>Turning one off keeps the entry, so a display name the reader typed survives being switched
    /// off and on again.</summary>
    [Fact]
    public void Turning_a_typed_model_off_keeps_it_in_the_list()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("a", "My name for it")));
        Group(vm).IsExpanded = true;

        Rows(vm).Single().Enabled = false;

        var stored = Assert.Single(service.Connections.Single().Models);
        Assert.False(stored.Enabled);
        Assert.Equal("My name for it", stored.DisplayName);
    }

    // ---- the fetched catalogue (#674) ------------------------------------------------------------------

    /// <summary>
    /// A fetched model arrives OFF.
    ///
    /// <para>Four hundred entries turn up because a key was pasted, not because anyone asked for them. The
    /// reader promotes the handful they will switch between — which is also the only way the per-turn picker
    /// stays the single-digit list it needs to be.</para>
    /// </summary>
    [Fact]
    public void A_fetched_model_starts_disabled()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("nvidia/nemotron-nano-9b-v2", "NVIDIA Nemotron Nano 9B V2")));
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;

        Assert.False(Assert.Single(Rows(vm)).Enabled);
    }

    /// <summary>Nothing reaches <c>settings.json</c> until the reader chooses it — storing four hundred
    /// entries so each could carry a <c>false</c> would bloat the file to state what its emptiness already
    /// says.</summary>
    [Fact]
    public void An_untouched_catalogue_is_not_persisted()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B")));
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;

        Assert.Empty(service.Connections.Single().Models);
    }

    // ---- a model the provider dropped (#728) -----------------------------------------------------------

    /// <summary>
    /// A stored model the fetched listing does not carry is marked.
    ///
    /// <para>Providers retire and rename models routinely, free ones especially. Until this, the app went on
    /// showing one as an ordinary row — enabled, pickable, indistinguishable — and the reader found out as a
    /// 404 at the moment they asked a question.</para>
    /// </summary>
    [Fact]
    public void A_stored_model_the_listing_no_longer_carries_is_marked()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("kept", "Kept")));
        service.Add("mine", Draft(
            new AiModelEntry("kept", "Kept"),
            new AiModelEntry("retired", "Retired")));

        Group(vm).IsExpanded = true;

        Assert.True(Rows(vm).Single(r => r.ModelId == "retired").Missing);
        Assert.False(Rows(vm).Single(r => r.ModelId == "kept").Missing);
        Assert.True(service.Connections.Single().Models.Single(m => m.Id == "retired").Missing);
    }

    /// <summary>
    /// A fetch that failed marks nothing.
    ///
    /// <para>An offline moment, a local runner that is not started, a 401 — each would otherwise flag every
    /// model the reader has, turning a transient problem into a screen of false alarms. Absence of evidence
    /// is not evidence, which is the same reason <c>Reachability.Configured</c> is a third state.</para>
    /// </summary>
    [Fact]
    public void A_failed_fetch_marks_nothing()
    {
        // Constructed rather than built with Fail(), which always carries an empty list: that would leave
        // this passing on MarkListing's empty-listing guard while asserting nothing about the Ok check it
        // names, and a future Fail that carried the models it got before dying would mark them all.
        // (fable review, found by mutation)
        var partial = new AiCatalogResult(
            false, "Could not connect to mine.",
            new[] { new AiCatalogModel("something-else", "Something else") }, Reachable: false);

        var (vm, service) = Make(new FakeCatalog(partial));
        service.Add("mine", Draft(new AiModelEntry("mine-model", "Mine")));

        Group(vm).IsExpanded = true;

        Assert.False(Rows(vm).Single().Missing);
        Assert.False(service.Connections.Single().Models.Single().Missing);
    }

    /// <summary>
    /// A listing the endpoint says is incomplete marks nothing.
    ///
    /// <para>A first page, or one whose entries we could only partly read, is a fine thing to show — every
    /// model in it is real. What it cannot support is the inference in the other direction: that a model
    /// absent from it has been retired. Anthropic's listing pages at twenty by default, so without this the
    /// twenty-first model onwards would be reported as gone.</para>
    /// </summary>
    [Fact]
    public void An_incomplete_listing_marks_nothing()
    {
        var (vm, service) = Make(new FakeCatalog(AiCatalogResult.Success(
            new[] { new AiCatalogModel("page-one-model", "Page one") }, complete: false)));
        service.Add("mine", Draft(new AiModelEntry("page-two-model", "Page two")));

        Group(vm).IsExpanded = true;

        Assert.False(service.Connections.Single().Models.Single(m => m.Id == "page-two-model").Missing);
    }

    /// <summary>An empty listing marks nothing either: endpoints answer 200 with an empty <c>data[]</c> for
    /// reasons that have nothing to do with the reader's models, and that would be the loudest possible way
    /// to say nothing.</summary>
    [Fact]
    public void An_empty_listing_marks_nothing()
    {
        var (vm, service) = Make(new FakeCatalog());
        service.Add("mine", Draft(new AiModelEntry("mine-model", "Mine")));

        Group(vm).IsExpanded = true;

        Assert.False(service.Connections.Single().Models.Single().Missing);
    }

    /// <summary>A model that comes back — renamed away and restored, or a listing that was briefly partial —
    /// loses the mark. The mark is a reading of the last good listing, not a verdict.</summary>
    [Fact]
    public void A_model_the_listing_carries_again_is_unmarked()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("back", "Back")));
        service.Add("mine", Draft(new AiModelEntry("back", "Back", Missing: true)));

        Group(vm).IsExpanded = true;

        Assert.False(service.Connections.Single().Models.Single().Missing);
    }

    /// <summary>Marked, never removed or switched off. The reader chose it, a listing is not authority over
    /// their configuration, and an endpoint publishing an incomplete one would otherwise delete valid entries
    /// on their behalf.</summary>
    [Fact]
    public void A_marked_model_is_neither_removed_nor_disabled()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("kept", "Kept")));
        service.Add("mine", Draft(new AiModelEntry("retired", "Retired", Enabled: true)));

        Group(vm).IsExpanded = true;

        var stored = Assert.Single(service.Connections.Single().Models);
        Assert.Equal("retired", stored.Id);
        Assert.True(stored.Enabled);
        Assert.True(Rows(vm).Single(r => r.ModelId == "retired").Enabled);
    }

    /// <summary>The row stops repeating what the provider once published about it. A context window and a
    /// price describe something on offer; this is not one.</summary>
    [Fact]
    public void A_marked_model_no_longer_carries_its_published_facts()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("kept", "Kept")));
        service.Add("mine", Draft(
            new AiModelEntry("retired", "Retired", true, ContextLength: 128_000)));

        Group(vm).IsExpanded = true;

        var row = Rows(vm).Single(r => r.ModelId == "retired");
        Assert.Equal("retired", row.Details);
        Assert.DoesNotContain("context", row.Details, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Promoting one adds it to the reader's stored list, with the provider's display name.</summary>
    [Fact]
    public void Promoting_a_fetched_model_stores_it()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("nvidia/nemotron", "NVIDIA Nemotron")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Rows(vm).Single().Enabled = true;

        var stored = Assert.Single(service.Connections.Single().Models);
        Assert.Equal("nvidia/nemotron", stored.Id);
        Assert.Equal("NVIDIA Nemotron", stored.DisplayName);
        Assert.True(stored.Enabled);
    }

    /// <summary>A typed model that also appears in the listing is one row, not two — the reader's entry wins,
    /// keeping the name they chose.</summary>
    [Fact]
    public void A_typed_model_is_not_duplicated_by_the_listing()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("shared", "Provider's name"),
            new AiCatalogModel("other", "Other")));
        service.Add("mine", Draft(new AiModelEntry("shared", "My name")));

        Group(vm).IsExpanded = true;

        var rows = Rows(vm);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.ModelId == "shared");
        Assert.Equal("My name", rows.Single(r => r.ModelId == "shared").DisplayName);
    }

    /// <summary>The header says how much of a listing has been promoted, which is the number that matters
    /// once a catalogue arrives.</summary>
    [Fact]
    public void The_header_counts_what_is_on_against_what_is_available()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B"), new AiCatalogModel("c", "C")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Rows(vm).Single(r => r.ModelId == "a").Enabled = true;

        Assert.Equal("1 of 3 models listed", Group(vm).CountText);
    }

    /// <summary>Fetched once, on first expand — not on every expand, and not on every keystroke in the search
    /// box.</summary>
    [Fact]
    public void The_listing_is_fetched_once()
    {
        var catalog = new FakeCatalog(new AiCatalogModel("a", "A"));
        var (vm, service) = Make(catalog);
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;
        Group(vm).IsExpanded = false;
        Group(vm).IsExpanded = true;
        vm.Search = "a";

        Assert.Equal(1, catalog.Calls);
    }

    /// <summary>
    /// A failed fetch is reported and nothing else. The typed list stays exactly as it was — the listing is
    /// additive, and an endpoint that publishes none is the ordinary case for a local runner rather than a
    /// broken one.
    /// </summary>
    [Fact]
    public void A_failed_fetch_leaves_the_typed_list_alone_and_says_why()
    {
        var (vm, service) = Make(new FakeCatalog(
            AiCatalogResult.Fail("No response from http://localhost:8000/v1/models — is the endpoint running?")));
        service.Add("mine", Draft(new AiModelEntry("typed", "Typed")));

        Group(vm).IsExpanded = true;

        Assert.True(Group(vm).HasFetchProblem);
        Assert.Contains("localhost:8000", Group(vm).FetchProblem);
        var row = Assert.Single(Rows(vm));
        Assert.Equal("typed", row.ModelId);
        Assert.True(row.Enabled);
    }

    /// <summary>
    /// Toggling a model does not rebuild the list.
    ///
    /// <para>The rows are bound to an observable collection; clearing and refilling it sends the list box's
    /// scroll position back to the top. At four hundred rows that means switching on a model near the bottom
    /// throws the reader back to the first one — and they would then do it again, because the model they just
    /// enabled is no longer on screen to confirm it worked.</para>
    /// </summary>
    [Fact]
    public void Toggling_a_model_does_not_rebuild_the_list()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        var before = Rows(vm).ToList();
        before.Single(r => r.ModelId == "a").Enabled = true;

        Assert.Equal(before, Rows(vm));   // same row objects, same order, same collection contents
    }

    /// <summary>The header still keeps up, even though the list is not rebuilt.</summary>
    [Fact]
    public void Toggling_updates_the_count_without_a_rebuild()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Rows(vm).Single(r => r.ModelId == "a").Enabled = true;

        Assert.Equal("1 of 2 models listed", Group(vm).CountText);
    }

    /// <summary>
    /// A toggle survives a later rebuild.
    ///
    /// <para>Suppressing the rebuild means the group is holding a connection snapshot taken before the write.
    /// If it is not refreshed, the next genuine rebuild — another connection added, a search typed — restores
    /// the old value and the reader watches their choice undo itself.</para>
    /// </summary>
    [Fact]
    public void A_toggle_survives_the_next_rebuild()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;
        Rows(vm).Single(r => r.ModelId == "a").Enabled = true;

        vm.Search = "a";
        vm.Search = "";

        Assert.True(Rows(vm).Single(r => r.ModelId == "a").Enabled);
    }

    // ---- the active model follows what is switched on ------------------------------------------------------

    /// <summary>
    /// Switching a model on, with nothing active, makes it the one that answers.
    ///
    /// <para>The defect this pins: enabling only ever set a flag, so a reader who connected OpenRouter and
    /// switched on a single model was told "No model is configured" by the assistant while looking at the
    /// model they had just enabled. With one model enabled the per-turn picker had nothing to choose between
    /// either, so there was no control anywhere that could set it.</para>
    /// </summary>
    [Fact]
    public void Switching_a_model_on_makes_it_active_when_nothing_is()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("nvidia/nemotron", "Nemotron"), new AiCatalogModel("other", "Other")));
        service.Add("openrouter-ish", Draft());
        Group(vm).IsExpanded = true;
        Assert.Null(service.ActiveModelId);

        Rows(vm).Single(r => r.ModelId == "nvidia/nemotron").Enabled = true;

        Assert.Equal("nvidia/nemotron", service.ActiveModelId);
        Assert.Equal("openrouter-ish", service.Active?.Id);
    }

    /// <summary>A later enable does not steal the choice — the reader picked one already.</summary>
    [Fact]
    public void A_later_enable_does_not_change_the_active_model()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("first", "First"), new AiCatalogModel("second", "Second")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Rows(vm).Single(r => r.ModelId == "first").Enabled = true;
        Rows(vm).Single(r => r.ModelId == "second").Enabled = true;

        Assert.Equal("first", service.ActiveModelId);
    }

    /// <summary>Switching the active model off moves the pointer to another enabled one, rather than leaving
    /// requests aimed at a model the reader has just hidden.</summary>
    [Fact]
    public void Switching_the_active_model_off_moves_to_another_enabled_one()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("a", "A"), new AiModelEntry("b", "B")));
        Group(vm).IsExpanded = true;
        service.SetActive("mine", "a");

        Rows(vm).Single(r => r.ModelId == "a").Enabled = false;

        Assert.Equal("b", service.ActiveModelId);
    }

    /// <summary>With nothing else enabled it clears, which is honest — and the assistant's own message then
    /// says so rather than the request failing at the endpoint.</summary>
    [Fact]
    public void Switching_the_last_enabled_model_off_clears_the_active_one()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("a", "A")));
        Group(vm).IsExpanded = true;
        service.SetActive("mine", "a");

        Rows(vm).Single().Enabled = false;

        Assert.Null(service.ActiveModelId);
    }

    // ---- what gets written down on promotion ---------------------------------------------------------------

    /// <summary>
    /// Promoting a model records what the provider published about it.
    ///
    /// <para>The listing lives only in this tab and only while the window is open, so the per-turn picker
    /// (#693) can never ask again — whatever it is to show has to be written down here.</para>
    /// </summary>
    [Fact]
    public void Promoting_a_model_records_what_the_provider_published()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel(
            "nvidia/nemotron", "Nemotron", ContextLength: 1_000_000,
            InputModalities: new[] { "text", "image" },
            SupportedParameters: new[] { "reasoning" })));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Rows(vm).Single().Enabled = true;

        var stored = Assert.Single(service.Connections.Single().Models);
        Assert.Equal(1_000_000, stored.ContextLength);
        Assert.True(stored.SupportsReasoning);
        Assert.Equal("text, image", stored.Inputs);
    }

    /// <summary>A hand-typed model has nothing published about it, and nothing is invented.</summary>
    [Fact]
    public void A_typed_model_records_no_published_facts()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("typed", "Typed")));
        Group(vm).IsExpanded = true;

        Rows(vm).Single().Enabled = false;
        Rows(vm).Single().Enabled = true;

        var stored = Assert.Single(service.Connections.Single().Models);
        Assert.Null(stored.ContextLength);
        Assert.Null(stored.Inputs);
        Assert.Null(stored.SupportsReasoning);   // nothing published is not "published: no"
    }

    // ---- a listing is contact ------------------------------------------------------------------------------

    /// <summary>
    /// Fetching a listing marks the connection reachable.
    ///
    /// <para>Asking a provider for its models <i>is</i> contacting it, and establishes the same fact a chat
    /// turn does. Without this a reader who had just fetched four hundred models from OpenRouter was still
    /// told the connection had never been checked — the app had contacted the endpoint and thrown the
    /// knowledge away.</para>
    /// </summary>
    [Fact]
    public void A_successful_listing_marks_the_connection_reachable()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("a", "A")));
        service.Add("mine", Draft());
        Assert.Equal(Reachability.Configured, service.Connections.Single().State);

        Group(vm).IsExpanded = true;

        Assert.Equal(Reachability.Reachable, service.Connections.Single().State);
    }

    /// <summary>
    /// An endpoint that refuses is still an endpoint that answered.
    ///
    /// <para>A rejected key or a missing listing proves something was there to say no. Marking that
    /// unreachable would send the reader to check their network over what is a credential problem — the
    /// confusion between "cannot reach" and "reached, and was refused" that #673 exists to keep apart.</para>
    /// </summary>
    [Fact]
    public void A_refused_listing_still_counts_as_contact()
    {
        var (vm, service) = Make(new FakeCatalog(
            AiCatalogResult.Fail("rejected the stored key", reachable: true)));
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;

        Assert.Equal(Reachability.Reachable, service.Connections.Single().State);
        Assert.True(Group(vm).HasFetchProblem);
    }

    /// <summary>A transport failure is the one case that means unreachable — nothing answered.</summary>
    [Fact]
    public void A_listing_that_never_arrived_marks_it_unreachable()
    {
        var (vm, service) = Make(new FakeCatalog(
            AiCatalogResult.Fail("No response from http://localhost:8000/v1/models", reachable: false)));
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;

        Assert.Equal(Reachability.Unreachable, service.Connections.Single().State);
    }

    /// <summary>Where nothing was sent, nothing is claimed — an unfinished connection is not evidence either
    /// way.</summary>
    [Fact]
    public void A_listing_that_was_never_attempted_reports_nothing()
    {
        var (vm, service) = Make(new FakeCatalog(
            AiCatalogResult.Fail("is not finished being set up")));
        service.Add("mine", Draft());

        Group(vm).IsExpanded = true;

        Assert.Equal(Reachability.Configured, service.Connections.Single().State);
    }

    // ---- the documentation link on an empty group (#740) ----------------------------------------------------

    /// <summary>
    /// Offered only once we have asked and come back with nothing.
    ///
    /// <para>Before a fetch, a collapsed group would advertise a link although expanding it would have
    /// produced a listing — which is the opposite of what the link says.</para>
    /// </summary>
    [Fact]
    public void The_doc_link_is_not_offered_before_anything_has_been_asked()
    {
        var (vm, service) = Make(new FakeCatalog(AiCatalogResult.Fail("404")));
        service.AddFromPreset("openrouter", new Dictionary<string, string>());

        Assert.False(Group(vm).ShowDoc);

        Group(vm).IsExpanded = true;

        Assert.True(Group(vm).ShowDoc);
    }

    /// <summary>
    /// A filter hiding everything is not a provider publishing nothing.
    ///
    /// <para>A connection whose four hundred fetched models are all paid would otherwise be described as
    /// having no listing, and pointed at documentation it does not need.</para>
    /// </summary>
    [Fact]
    public void A_filter_that_hides_every_model_does_not_offer_the_doc_link()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("paid", "Paid",
                PromptPricePerMillion: 5m, CompletionPricePerMillion: 5m)));
        service.AddFromPreset("openrouter", new Dictionary<string, string>());
        Group(vm).IsExpanded = true;

        vm.FreeOnly = true;

        Assert.Empty(Rows(vm));            // nothing on screen
        Assert.False(Group(vm).ShowDoc);   // but the provider does publish a listing
    }

    // ---- search and the capability filter ---------------------------------------------------------------

    /// <summary>Search filters across every group, not within the expanded one, and shows what it finds
    /// without the reader expanding anything.</summary>
    [Fact]
    public void Search_reaches_into_collapsed_groups()
    {
        var (vm, service) = Make();
        service.Add("one", Draft(new AiModelEntry("llama3.1:8b", "Llama 3.1 8B")));
        service.Add("two", Draft(new AiModelEntry("qwen2.5:14b", "Qwen 2.5 14B")));

        Assert.Empty(Rows(vm));   // both collapsed

        vm.Search = "qwen";

        var row = Assert.Single(Rows(vm));
        Assert.Equal("qwen2.5:14b", row.ModelId);
    }

    /// <summary>A group with no match disappears rather than sitting there as a header promising results it
    /// does not have.</summary>
    [Fact]
    public void A_group_with_no_matches_is_hidden_while_searching()
    {
        var (vm, service) = Make();
        service.Add("one", Draft(new AiModelEntry("llama3.1:8b", "Llama 3.1 8B")));
        service.Add("two", Draft(new AiModelEntry("qwen2.5:14b", "Qwen 2.5 14B")));

        vm.Search = "qwen";

        Assert.Single(vm.Rows.OfType<AiModelGroupViewModel>());
    }

    /// <summary>Searching matches the wire id as readily as the label — a reader thinking of "nemotron" may
    /// have either in mind.</summary>
    [Fact]
    public void Search_matches_the_id_as_well_as_the_name()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("nvidia/nemotron-nano-9b-v2", "Nano")));

        vm.Search = "nemotron";

        Assert.Single(Rows(vm));
    }

    /// <summary>
    /// The price filter hides what the provider charges for, and is off by default.
    ///
    /// <para>Off by default because on would be a claim about what the reader wants to spend — and on
    /// OpenRouter it would hide 395 of 415 models the first time they looked.</para>
    /// </summary>
    [Fact]
    public void The_price_filter_hides_models_that_cost_money()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("free", "Free",
                PromptPricePerMillion: 0m, CompletionPricePerMillion: 0m),
            new AiCatalogModel("paid", "Paid",
                PromptPricePerMillion: 0.4m, CompletionPricePerMillion: 1.6m)));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Assert.False(vm.FreeOnly);
        Assert.Equal(2, Rows(vm).Count);

        vm.FreeOnly = true;

        Assert.Equal(new[] { "free" }, Rows(vm).Select(r => r.ModelId));
    }

    /// <summary>
    /// An endpoint that publishes no price is unaffected.
    ///
    /// <para>Unknown is not costly. A local runner charges nothing and says nothing, and hiding its models
    /// behind a filter for a field it never sent would empty the group for a reader who is not spending
    /// anything at all.</para>
    /// </summary>
    [Fact]
    public void A_model_with_no_published_price_survives_the_filter()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel("quiet", "Quiet")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        vm.FreeOnly = true;

        Assert.Single(Rows(vm), r => r.ModelId == "quiet");
    }

    /// <summary>A price of zero on only one side still counts as costing money — a model billed for output
    /// alone is not free.</summary>
    [Fact]
    public void A_price_on_either_side_counts_as_costing_money()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("half", "Half",
                PromptPricePerMillion: 0m, CompletionPricePerMillion: 1.6m)));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        vm.FreeOnly = true;

        Assert.Empty(Rows(vm));
    }

    /// <summary>
    /// A model the reader typed is never filtered away.
    ///
    /// <para>They put it there; hiding it behind a filter would read as having lost it. The filter exists to
    /// prune a listing nobody asked for, not to second-guess a choice already made.</para>
    /// </summary>
    [Fact]
    public void A_typed_model_is_never_hidden_by_the_filter()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("odd", "Odd",
                PromptPricePerMillion: 9m, CompletionPricePerMillion: 9m)));
        service.Add("mine", Draft(new AiModelEntry("odd", "Odd")));
        Group(vm).IsExpanded = true;

        vm.FreeOnly = true;

        Assert.Single(Rows(vm), r => r.ModelId == "odd");
    }

    // ---- lifetime ----------------------------------------------------------------------------------------

    /// <summary>Same reason as the Providers tab: the service is a singleton and this is rebuilt on every
    /// Settings open.</summary>
    [Fact]
    public void A_disposed_view_model_stops_listening()
    {
        var (vm, service) = Make();
        service.Add("first", Draft());
        Assert.Single(vm.Groups);

        vm.Dispose();
        service.Add("second", Draft());

        Assert.Single(vm.Groups);
        Assert.Equal(2, service.Connections.Count);
    }

    // ---- provider marks on the group headers (#740) -----------------------------------------------------------

    /// <summary>
    /// A group header asks for its provider's logo, keyed by the connection id.
    ///
    /// <para>The same id models.dev uses for anything added from the catalogue, so the mark on this tab is
    /// the one on the Providers tab. A reader who has learnt to find OpenRouter by its logo should not have
    /// to fall back to reading letters one tab over.</para>
    /// </summary>
    [Fact]
    public async Task A_group_asks_for_its_providers_logo()
    {
        var logos = new FakeLogos("openrouter");
        var (vm, service) = Make(logos: logos);
        service.AddFromPreset("openrouter", new Dictionary<string, string>());

        var group = Group(vm);
        if (group.LogoLoad is { } load) await load;

        Assert.Equal("openrouter", logos.Asked);
        Assert.True(group.HasLogo);
    }

    /// <summary>A provider with no mark keeps its lettered tile — the fallback is never removed, only covered
    /// when something actually rendered.</summary>
    [Fact]
    public async Task A_group_with_no_logo_keeps_its_monogram()
    {
        var logos = new FakeLogos(null);
        var (vm, service) = Make(logos: logos);
        service.Add("my-box", Draft());

        var group = Group(vm);
        if (group.LogoLoad is { } load) await load;

        Assert.False(group.HasLogo);
        Assert.False(string.IsNullOrWhiteSpace(group.Monogram));
    }

    /// <summary>Hands back a path for one id and null for anything else, recording what it was asked.</summary>
    private sealed class FakeLogos : IAiProviderLogos
    {
        private readonly string? _known;
        public FakeLogos(string? known) => _known = known;
        public string? Asked { get; private set; }

        public Task<string?> GetLogoPathAsync(string providerId, CancellationToken ct = default)
        {
            Asked = providerId;
            return Task.FromResult(providerId == _known ? "/tmp/cst-test-logo.svg" : null);
        }
    }

    // ---- what a row says ---------------------------------------------------------------------------------

    /// <summary>The provider's own facts, verbatim and attributed — safe where a table we maintained would
    /// not be. Carried on the row's tooltip rather than a second line of text under every name.</summary>
    [Fact]
    public void A_row_shows_the_published_facts()
    {
        var (vm, service) = Make(new FakeCatalog(new AiCatalogModel(
            "m", "M", ContextLength: 131072, PromptPricePerMillion: 0.4m,
            CompletionPricePerMillion: 1.6m, SupportedParameters: new[] { "reasoning" })));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        var details = Rows(vm).Single().Details;
        Assert.Contains("131K context", details);
        Assert.Contains("$0.4/$1.6 per M", details);
        Assert.Contains("reasoning", details);
        // The id on its own line: it is the string a reader would copy, and burying it in a run of
        // dot-separated facts makes it hard to pick out.
        Assert.StartsWith("m\n", details);
    }

    /// <summary>With nothing published there is no second line to write — the tooltip is the bare id, not an
    /// id followed by an empty run of separators.</summary>
    [Fact]
    public void A_row_with_no_facts_has_no_second_line()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("bare", "Bare")));
        Group(vm).IsExpanded = true;

        Assert.DoesNotContain("\n", Rows(vm).Single().Details);
    }

    /// <summary>An endpoint that publishes nothing degrades to the id rather than showing an empty
    /// line.</summary>
    [Fact]
    public void A_row_with_no_published_facts_shows_its_id()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft(new AiModelEntry("gemma4:12b-mlx", "Gemma4 12B MLX")));
        Group(vm).IsExpanded = true;

        Assert.Equal("gemma4:12b-mlx", Rows(vm).Single().Details);
    }

    /// <summary>Zero is free and may be said so; unknown is not free and must never be rendered as it.</summary>
    [Fact]
    public void A_free_model_says_free_and_an_unpriced_one_says_nothing()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("free", "Free", PromptPricePerMillion: 0m, CompletionPricePerMillion: 0m),
            new AiCatalogModel("quiet", "Quiet")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Assert.Contains("free", Rows(vm).Single(r => r.ModelId == "free").Details);
        Assert.DoesNotContain("free", Rows(vm).Single(r => r.ModelId == "quiet").Details);
    }

    /// <summary>Alphabetical within a group. Nothing here may order by recency: that is what upstream does,
    /// and it is an editorial claim however mechanically it is computed (#689).</summary>
    [Fact]
    public void Rows_are_alphabetical_within_a_group()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("c", "Zephyr"),
            new AiCatalogModel("a", "Apollo"),
            new AiCatalogModel("b", "mercury")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        Assert.Equal(new[] { "Apollo", "mercury", "Zephyr" }, Rows(vm).Select(r => r.DisplayName));
    }

    // ---- what the count is counting (#785) --------------------------------------------------------------

    /// <summary>
    /// The listing is fetched only when the group is first expanded, so before that the rows are the reader's
    /// OWN saved models. "2 of 3" then reads as "this provider has 3 models", which is a different claim from
    /// the one the number supports — and it sent a reader looking for models that were never missing, three
    /// times in one sitting.
    /// </summary>
    [Fact]
    public void Before_a_fetch_the_count_says_the_models_are_the_readers_own()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B"), new AiCatalogModel("c", "C")));
        service.Add("mine", Draft(
            new AiModelEntry("a", "A"), new AiModelEntry("b", "B", Enabled: false)));

        Assert.Equal("1 of 2 models saved", Group(vm).CountText);
    }

    /// <summary>And once the provider has answered, the same words would mean something else, so they change.</summary>
    [Fact]
    public void After_a_fetch_the_count_says_it_is_the_providers_listing()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("a", "A"), new AiCatalogModel("b", "B"), new AiCatalogModel("c", "C")));
        service.Add("mine", Draft(
            new AiModelEntry("a", "A"), new AiModelEntry("b", "B", Enabled: false)));

        Group(vm).IsExpanded = true;

        Assert.Equal("1 of 3 models listed", Group(vm).CountText);
    }

    /// <summary>A narrowed view is a third meaning again: neither what the reader has nor what the provider
    /// offers, and indistinguishable from a short listing without saying so.</summary>
    [Fact]
    public void While_searching_the_count_says_it_is_showing_matches()
    {
        var (vm, service) = Make(new FakeCatalog(
            new AiCatalogModel("alpha", "Alpha"),
            new AiCatalogModel("beta", "Beta"),
            new AiCatalogModel("alpine", "Alpine")));
        service.Add("mine", Draft());
        Group(vm).IsExpanded = true;

        vm.Search = "alp";

        Assert.Equal("0 of 2 models matching", Group(vm).CountText);
    }
}
