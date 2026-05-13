using System;
using System.Collections.Generic;
using DrawClient.Models;

namespace DrawClient.Services
{
    public class UndoRedoManager
    {
        private Stack<DrawAction> _undoStack = new Stack<DrawAction>();
        private Stack<DrawAction> _redoStack = new Stack<DrawAction>();
        private const int MAX_HISTORY = 100;

        public event Action OnHistoryChanged;
        public event Action<DrawAction> OnUndo;
        public event Action<DrawAction> OnRedo;

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public void AddAction(DrawAction action)
        {
            if (action == null) return;
            _redoStack.Clear();
            _undoStack.Push(action);

            // Giới hạn số lượng (giữ MAX_HISTORY phần tử mới nhất)
            if (_undoStack.Count > MAX_HISTORY)
            {
                var temp = new Stack<DrawAction>();
                var list = new List<DrawAction>(_undoStack);
                // list[0] là cũ nhất, list[^1] là mới nhất. Giữ lại MAX_HISTORY phần tử cuối.
                for (int i = list.Count - MAX_HISTORY; i < list.Count; i++)
                    temp.Push(list[i]);
                _undoStack = temp;
            }
            OnHistoryChanged?.Invoke();
        }

        public DrawAction Undo()
        {
            if (_undoStack.Count == 0) return null;
            var action = _undoStack.Pop();
            _redoStack.Push(action);
            OnHistoryChanged?.Invoke();
            OnUndo?.Invoke(action);
            return action;
        }

        public DrawAction Redo()
        {
            if (_redoStack.Count == 0) return null;
            var action = _redoStack.Pop();
            _undoStack.Push(action);
            OnHistoryChanged?.Invoke();
            OnRedo?.Invoke(action);
            return action;
        }

        public bool CanUndo() => _undoStack.Count > 0;
        public bool CanRedo() => _redoStack.Count > 0;
        public void Clear() { _undoStack.Clear(); _redoStack.Clear(); OnHistoryChanged?.Invoke(); }
        public IEnumerable<DrawAction> GetAllActions()
        {
            return new List<DrawAction>(_undoStack);
        }
    }
}