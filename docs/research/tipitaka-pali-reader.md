# Tipitaka Pali Reader (TPR)

Competitive reference notes on the Tipitaka Pali Reader, a Flutter-based Pali reading app developed under the American Monk / Bhante Subhūti umbrella.

**Last updated:** 2026-04-20

## Project Links

- **Main site:** https://americanmonk.org/tipitaka-pali-reader/
- **Source (primary):** https://github.com/bksubhuti/tipitaka-pali-reader
- **Releases:** https://github.com/bksubhuti/tipitaka-pali-reader/releases
- **Linux distribution (Flathub):** https://flathub.org/en/apps/org.americanmonk.TipitakaPaliReader
- **Privacy policy:** https://americanmonk.org/privacy-policy-for-tipitaka-pali-reader/
- **Developer's other projects:** https://americanmonk.org/my-digital-projects-for-buddhism/
- **Fork (kokoye2007):** https://github.com/kokoye2007/tipitaka-pali-reader

## People

- **Lead programmer:** Ven. Ashin Pannyadazza
- **Project management + some programming:** Bhante Subhūti (bksubhuti on GitHub)
- **Origin:** Merger of two prior projects ("Tipitaka Pali" and "TPP")

## Tech Stack

- **Framework:** Flutter (Dart) — single codebase
- **Storage / Search:** SQLite with Full-Text Search (FTS) indexing
- **Distribution:** Flatpak, AppImage, native installers for macOS/Windows, app stores for mobile

## Platform Support

| Platform | Supported |
|----------|-----------|
| macOS    | Yes       |
| Windows  | Yes       |
| Linux    | Yes (Flatpak + AppImage) |
| iOS      | Yes       |
| Android  | Yes       |

**Five-platform reach from one codebase** is the structural advantage of the Flutter choice.

## Feature Set

### Text & Reading

- Full Tipiṭaka Pāḷi corpus reader
- Extensions system for user-installable content
- Line-by-line English/Pāḷi mula sutta extensions (user-addable)

### Search

- Indexed SQLite FTS for fast queries
- Velthuis ASCII typing support (Pali input standard) for Roman-script input

### Dictionaries (shipped / integrated)

- **Digital Pāḷi Dictionary (DPD)** — bundled
- **Pāḷi English Ultimate (PEU)** — bundled
- Dictionary updates deliverable via extension mechanism

### AI Integration

- OpenRouter API key integration (recent release)
- Users can obtain free-tier API keys without a credit card
- Enables LLM-assisted lookup/analysis inside the reader

### Distribution & Community

- Open source on GitHub
- Active release cadence
- Community fork exists (kokoye2007)

## Comparison vs. CST Reader (as of 2026-04-20)

### Where TPR is ahead of CST Reader

1. **Platform reach** — 5 platforms (incl. iOS/Android/Linux) vs. CST Reader's macOS + Windows-in-progress
2. **Dictionaries shipping** — DPD + PEU integrated today; CST Reader has this on the roadmap only
3. **AI integration** — OpenRouter already shipped; CST Reader has semantic search on the research list but nothing implemented
4. **Extension model** — user-installable content and dictionary updates
5. **Mobile-first audience fit** — meditation retreats, travel, daily practice favor phones/tablets

### Where CST Reader is ahead of TPR

1. **Search depth** — Lucene.NET with position-based indexing, proximity search, two-color highlighting, wildcard + regex across 14 scripts; SQLite FTS is simpler
2. **Script accuracy** — 14-script round-trip validation framework, 99.96% accuracy across 2,248 corpus-derived test words
3. **Desktop UX** — dockable panels, per-tab script, full session restoration (scroll, highlights, window positions)
4. **Source verification** — View Source PDF with context-aware page navigation for Burmese 1957/2010 editions (156+ book mappings)
5. **Production macOS distribution** — Developer ID signed + notarized DMG installers
6. **Advanced search UI** — filterable by Pitaka/Commentary, live book counts, persistent search highlights across sessions

## Strategic Read

TPR and CST Reader optimize for **different audiences**:

- **TPR:** practitioners. Mobile reach, built-in dictionaries for reading, AI for casual lookup, low-friction install. Flutter was the right tool for this goal.
- **CST Reader:** scholars. Deep search, script rigor, source-text verification, desktop power-user workflows. Avalonia/.NET fits this goal.

These are **genuinely differentiated products, not head-to-head competitors.** A practitioner reading daily suttas on a phone is a different user than a scholar cross-referencing Burmese source pages against proximity searches in Tibetan script.

## Planning Implications

Things worth considering if/when planning CST Reader roadmap:

- **Dictionary priority** — TPR shipping DPD + PEU raises the baseline expectation; your roadmap item for dictionaries may deserve higher priority
- **Mobile question** — is there demand from the scholarly audience for a read-only mobile companion? (Full parity is likely not worth it; a read-only view of synced library might be)
- **AI/LLM lookup** — OpenRouter-style integration is low-cost to implement and a growing user expectation
- **Extension model** — TPR's user-installable content approach is interesting for a community-maintained commentary or translation layer
- **Linux** — Flathub/AppImage distribution is relatively low lift on Avalonia/.NET; worth considering once Windows ships
