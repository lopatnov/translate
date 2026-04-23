# Contributing to Lopatnov.Translate

Contributing to open source can be a rewarding way to learn, share knowledge, and build experience.
We appreciate every contribution, no matter how small.

Please read our [Code of Conduct](CODE_OF_CONDUCT.md) before contributing.

---

## Table of Contents

- [Types of Contributions](#types-of-contributions)
- [Before You Start](#before-you-start)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Submitting a Pull Request](#submitting-a-pull-request)

---

## Types of Contributions

There are many ways to contribute:

**Developers**
- Fix bugs reported in [Issues](https://github.com/lopatnov/translate/issues)
- Implement features from the backlog
- Improve test coverage (xUnit + Moq)
- Refactor and improve code quality

**Writers**
- Improve this documentation or the README
- Add or fix inline code comments

**Testers**
- Test the Docker setup on different platforms
- Report bugs with clear reproduction steps
- Verify that fixes actually work

**Supporters**
- Star the repository to help others find the project
- Share Lopatnov.Translate with others who might find it useful
- Answer questions in [Discussions](https://github.com/lopatnov/translate/discussions)

---

## Before You Start

**For minor changes** (typo fixes, small documentation edits, obvious bug fixes) — open a pull request directly.

**For major changes** (new features, architectural changes, breaking changes) — [open an issue](https://github.com/lopatnov/translate/issues) first to discuss the approach. This avoids wasted effort if the direction doesn't fit the project goals.

---

## Development Setup

Follow the [Local Development](README.md#local-development) section in the README to get the project running.

**Build verification** (must pass before submitting a PR):

```bash
dotnet build translate.slnx --configuration Release
dotnet test translate.slnx --filter "Category!=Integration"
```

---

## Making Changes

1. **Fork** the repository and **clone** your fork:

   ```bash
   git clone https://github.com/<your-username>/translate.git
   cd translate
   ```

2. **Add the upstream remote** so you can keep your fork up to date:

   ```bash
   git remote add upstream https://github.com/lopatnov/translate.git
   ```

3. **Sync** with upstream before starting:

   ```bash
   git fetch upstream
   git checkout main
   git merge upstream/main
   ```

4. **Create a feature branch**:

   ```bash
   git checkout -b feature/your-feature-name
   ```

5. **Make your changes.** Follow the project conventions:
   - All code, comments, and commit messages in **English**
   - Async by default — every public I/O method accepts `CancellationToken`
   - Strategy pattern for providers: one interface, many implementations
   - Do not modify `*.proto` or provider interfaces without discussion
   - Run `dotnet build` after every change

6. **Commit** using a conventional prefix:

   | Prefix | When to use |
   |---|---|
   | `feat:` | New feature |
   | `fix:` | Bug fix |
   | `docs:` | Documentation only |
   | `test:` | Adding or fixing tests |
   | `refactor:` | Code change that neither fixes a bug nor adds a feature |
   | `chore:` | Build process, CI, tooling |

   Example: `feat: add WhisperRecognizer for speech-to-text`

7. **Push** your branch and **open a pull request** against `main`.

---

## Submitting a Pull Request

- Fill in the pull request template
- Make sure `dotnet build` and unit tests both pass
- Describe *what* changed and *why*
- Link any related issue with `Closes #123`

A maintainer will review your PR as soon as possible. Thank you for contributing!
