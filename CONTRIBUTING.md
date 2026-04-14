# Contributing to Unity REPL

Thank you for your interest in contributing.

## Getting Started

1. Fork the repository and clone your fork.
2. Open a Unity project (2021.3 or later) and add the package locally by pointing `Packages/manifest.json` at your clone:
   ```json
   "com.lambda-labs.unity-repl": "file:../../path-to-your-clone"
   ```
3. The REPL server activates automatically via `InitializeOnLoad`.

## Running Tests

```bash
cd tests~
bash repl.test.sh
```

## Submitting Changes

1. Create a branch from `master` for your change.
2. Keep commits focused — one logical change per commit.
3. Open a pull request with a clear description of the problem and solution.

## Code Style

- Follow the existing conventions in the codebase (namespace `LambdaLabs.UnityRepl.Editor`, `s_` prefix for static fields).
- Editor-only code goes in `Editor/`; anything needed at runtime goes in `Runtime/`.

## License

By contributing, you agree that your contributions will be licensed under the [AGPL-3.0 License](LICENSE).
