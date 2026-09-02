# WriteStudio Developer Guide

## Architecture Overview

WriteStudio follows Clean Architecture and MVVM principles with Dependency Injection:

```
WriteStudio.Core        <-- Pure domain models, timeline engine, abstractions, clock
WriteStudio.Whiteboard  <-- Canvas stroke geometry, tools, undo/redo, pages
WriteStudio.Audio       <-- PCM capture, WAV writer, VU meter calculation
WriteStudio.Camera      <-- Webcam layout presets, device discovery
WriteStudio.Slides      <-- Presenter reference documents (strictly isolated)
WriteStudio.Recording   <-- Multi-track recording session orchestrator
WriteStudio.Rendering   <-- Vector timeline reconstructor, SkiaSharp, FFmpeg pipe
WriteStudio.Storage     <-- .wstudio project bundles, autosave, crash recovery
WriteStudio.App         <-- WPF Desktop composition root, MVVM ViewModels & Views
```

## Adding a New Whiteboard Tool

1. Add tool to `StrokeToolType` in `WriteStudio.Core/Models/StrokeToolType.cs`.
2. Implement geometry point generation in `WriteStudio.Whiteboard/Geometry/StrokeGeometryHelper.cs`.
3. Add rendering logic in `WriteStudio.Rendering/SkiaFrameRenderer.cs`.
4. Expose tool property & command in `WriteStudio.App/ViewModels/WhiteboardViewModel.cs`.
5. Add UI tool button in `WriteStudio.App/Views/MainWindow.xaml`.

## Testing

Run tests via command line:
```bash
dotnet test
```
All unit tests are cross-platform and run on Windows, macOS, and Linux.
