using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface IUndoRedoManager
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    
    event EventHandler? StateChanged;

    void Execute(IUndoableCommand command);
    void Undo();
    void Redo();
    void Clear();
}

public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
