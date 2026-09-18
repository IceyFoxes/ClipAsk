# Third-party dependencies

ClipAsk's GPL-3.0-only license covers its own code. Dependencies retain their licenses and copyright notices.

| Component | Pinned version | License | Source |
| --- | --- | --- | --- |
| Markdig | 1.3.2 | BSD-2-Clause | https://github.com/xoofx/markdig |
| WpfMath | 2.1.0 | MIT AND OFL-1.1 | https://github.com/ForNeVeR/xaml-math |
| XamlMath.Shared | 2.1.0 | MIT | https://github.com/ForNeVeR/xaml-math |
| OpenAI Codex CLI | 0.151.0 | Apache-2.0 | https://github.com/openai/codex/tree/rust-v0.151.0 |
| Microsoft .NET Desktop Runtime | 10.0.12 | Microsoft .NET Library terms, plus third-party notices | https://github.com/dotnet/runtime/tree/v10.0.12 |

These license expressions were checked against the installed NuGet package metadata. WpfMath includes font-related licensing; retain the upstream font notices with any binary distribution.

Pinned upstream license texts: [Markdig](licenses/Markdig.txt), [XAML-Math](licenses/XamlMath.md), [font notices](licenses/XamlMath-Fonts.md), [OpenAI Codex](licenses/OpenAI-Codex-Apache-2.0.txt), and the [Codex NOTICE](licenses/OpenAI-Codex-NOTICE.txt). The upstream font notices additionally identify the Knuth License for the Computer Modern font files.

The self-contained release package bundles the official Codex CLI runtime tree and Microsoft .NET Desktop Runtime. `scripts/package.sh` and `scripts/package.ps1` copy the exact .NET license and third-party notices supplied by the pinned SDK into the binary distribution. Codex's required Apache NOTICE is retained. Test-only packages are listed in `tests/ClipAsk.Core.Tests/ClipAsk.Core.Tests.csproj` and are not part of the desktop distribution.

The release checklist still requires an audit of the actual publish output before each release. See [the release checklist](docs/releasing.md).
