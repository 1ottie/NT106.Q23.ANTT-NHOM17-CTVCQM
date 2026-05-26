using System;
using System.Collections.Generic;
using System.Linq;
using DrawClient.Models;

namespace DrawClient.Services
{
    public class UndoRedoManager
    {
        private readonly List<DrawAction> _allActions = new List<DrawAction>();
        private readonly Dictionary<int, Stack<string>> _undoStacks = new Dictionary<int, Stack<string>>();
        private readonly Dictionary<int, Stack<string>> _redoStacks = new Dictionary<int, Stack<string>>();
        private readonly Dictionary<string, DrawAction> _actionMap = new Dictionary<string, DrawAction>();
        private const int MAX_HISTORY = 200;

        public event Action OnUndo;
        public event Action OnRedo;

        public int UndoCount
        {
            get
            {
                if (_undoStacks.Count == 0) return 0;
                return _undoStacks.Values.Sum(s => s.Count);
            }
        }

        public int RedoCount
        {
            get
            {
                if (_redoStacks.Count == 0) return 0;
                return _redoStacks.Values.Sum(s => s.Count);
            }
        }

        public void AddAction(DrawAction action)
        {
            if (action == null) return;

            _allActions.Add(action);
            _actionMap[action.Id] = action;

            int uid = action.UserId;
            if (!_undoStacks.ContainsKey(uid))
            {
                _undoStacks[uid] = new Stack<string>();
                _redoStacks[uid] = new Stack<string>();
            }

            _undoStacks[uid].Push(action.Id);
            _redoStacks[uid].Clear();

            // Trim old actions
            if (_allActions.Count > MAX_HISTORY)
            {
                int toRemove = _allActions.Count - MAX_HISTORY;
                var removed = _allActions.GetRange(0, toRemove);
                _allActions.RemoveRange(0, toRemove);

                foreach (var a in removed)
                {
                    _actionMap.Remove(a.Id);
                    foreach (var stack in _undoStacks.Values)
                    {
                        // Can't efficiently remove from stack, but old IDs won't match
                    }
                }
            }
        }

        public DrawAction Undo(int userId)
        {
            if (!_undoStacks.ContainsKey(userId) || _undoStacks[userId].Count == 0)
                return null;

            string actionId = _undoStacks[userId].Pop();
            if (!_actionMap.TryGetValue(actionId, out var action))
                return null;

            action.IsUndone = true;
            _redoStacks[userId].Push(actionId);
            return action;
        }

        public DrawAction Redo(int userId)
        {
            if (!_redoStacks.ContainsKey(userId) || _redoStacks[userId].Count == 0)
                return null;

            string actionId = _redoStacks[userId].Pop();
            if (!_actionMap.TryGetValue(actionId, out var action))
                return null;

            action.IsUndone = false;
            _undoStacks[userId].Push(actionId);
            return action;
        }

        /// <summary>
        /// Undo a specific action by ID (used for remote undo/redo sync)
        /// </summary>
        public DrawAction UndoById(string actionId)
        {
            if (!_actionMap.TryGetValue(actionId, out var action))
                return null;

            action.IsUndone = true;

            // Remove from undo stack of the action's owner
            int uid = action.UserId;
            if (_undoStacks.ContainsKey(uid))
            {
                var stack = _undoStacks[uid];
                var temp = new Stack<string>();
                bool found = false;
                while (stack.Count > 0)
                {
                    string id = stack.Pop();
                    if (id == actionId) { found = true; break; }
                    temp.Push(id);
                }
                while (temp.Count > 0) stack.Push(temp.Pop());
                if (found) _redoStacks[uid].Push(actionId);
            }

            return action;
        }

        /// <summary>
        /// Redo a specific action by ID (used for remote undo/redo sync)
        /// </summary>
        public DrawAction RedoById(string actionId)
        {
            if (!_actionMap.TryGetValue(actionId, out var action))
                return null;

            action.IsUndone = false;

            int uid = action.UserId;
            if (_redoStacks.ContainsKey(uid))
            {
                var stack = _redoStacks[uid];
                var temp = new Stack<string>();
                bool found = false;
                while (stack.Count > 0)
                {
                    string id = stack.Pop();
                    if (id == actionId) { found = true; break; }
                    temp.Push(id);
                }
                while (temp.Count > 0) stack.Push(temp.Pop());
                if (found) _undoStacks[uid].Push(actionId);
            }

            return action;
        }

        public bool CanUndo(int userId)
        {
            return _undoStacks.ContainsKey(userId) && _undoStacks[userId].Count > 0;
        }

        public bool CanRedo(int userId)
        {
            return _redoStacks.ContainsKey(userId) && _redoStacks[userId].Count > 0;
        }

        public bool CanUndo() => _undoStacks.Any(kv => kv.Value.Count > 0);
        public bool CanRedo() => _redoStacks.Any(kv => kv.Value.Count > 0);

        public List<DrawAction> GetAllActions()
        {
            return _allActions.Where(a => !a.IsUndone).ToList();
        }

        public List<DrawAction> GetAllActionsIncludingUndone()
        {
            return new List<DrawAction>(_allActions);
        }

        public void Clear()
        {
            _allActions.Clear();
            _actionMap.Clear();
            _undoStacks.Clear();
            _redoStacks.Clear();
        }
    }
}
