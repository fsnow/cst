using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Presentation;

namespace CST.Avalonia.Tests.TestSupport
{
    /// <summary>
    /// Stands in for the reader window so the navigate surface (#187) can be tested over real HTTP without a UI.
    /// Records every request it receives — which is what lets a test assert the CONSENT GATE by absence: a denied
    /// navigate must leave <see cref="Requests"/> empty, not merely return an error.
    /// </summary>
    public sealed class RecordingPresentationService : IPresentationService
    {
        public List<PresentationRequest> Requests { get; } = new();

        /// <summary>What to return; default success. Set to a failure to simulate "no reader window".</summary>
        public PresentationResult Result { get; set; } = PresentationResult.Ok();

        public PresentationRequest? Last => Requests.Count == 0 ? null : Requests[^1];

        public Task<PresentationResult> PresentAsync(PresentationRequest request, CancellationToken ct = default)
        {
            // Honor the SAME precondition the real service does, and reject before recording — otherwise a fake
            // that always succeeds would hide exactly the false-success bugs these tests exist to catch (an
            // unpresentable request must never come back as "presented"). One source of truth: the planner.
            var invalid = PresentationPlanner.Validate(request);
            if (invalid != null) return Task.FromResult(PresentationResult.Fail(invalid));

            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }
}
