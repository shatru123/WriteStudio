# Contributing to WriteStudio

Thank you for your interest in contributing to **WriteStudio**!

## Development Guidelines

1. **Architecture Separation**: Keep UI logic in `WriteStudio.App` and core recording/domain logic in `WriteStudio.Core`, `WriteStudio.Whiteboard`, `WriteStudio.Audio`, `WriteStudio.Recording`, `WriteStudio.Rendering`, and `WriteStudio.Storage`.
2. **Presenter Privacy**: Never pass reference slide data to `WriteStudio.Rendering`.
3. **Coding Standards**:
   - Modern C# 13 features.
   - Nullable reference types enabled.
   - Async/await with `CancellationToken` support for all I/O and hardware streams.
4. **Testing**:
   - Write unit tests for new domain features and state transitions.
   - Run `dotnet test` before submitting changes.

## Submitting Pull Requests
1. Fork the repository and create a feature branch (`git checkout -b feature/awesome-feature`).
2. Ensure `dotnet test` passes with zero failures.
3. Commit changes with clear, descriptive commit messages.
4. Open a pull request against `main`.
