# Contributing to ClipAsk

Thanks for helping improve ClipAsk. The goal is a reliable, short capture-to-answer interaction on Windows.

## Report a bug

Include your Windows version, display scaling and monitor arrangement, ClipAsk version or commit, steps to reproduce, and expected versus actual behavior. For response rendering bugs, include a sanitized Markdown sample. Do not include credentials or private screenshots.

## Make a change

1. Fork or branch from the repository and keep the change focused.
2. Follow the setup in [README.md](README.md) and the project guidance in [AGENTS.md](AGENTS.md).
3. Build `ClipAsk.slnx` in Release and run `tests/ClipAsk.Core.Tests/ClipAsk.Core.Tests.csproj`.
4. For UI changes, run the synthetic smoke checks and manually exercise affected interactions on Windows.
5. Describe the problem, resulting behavior, and validation in your pull request.

Keep prompts and provider policy centralized in `src/ClipAsk.Core/Providers`. Preserve deliberate capture boundaries and the managed browser authentication flow. Discuss larger changes before adding chat, OCR, history, or editing features.

Do not run live model benchmarks without an agreed input count and usage budget. Automated core and synthetic UI tests do not need live questions.

By submitting a contribution, you agree to license it under GPL-3.0-only. Third-party code must carry its original attribution and compatible license terms.
