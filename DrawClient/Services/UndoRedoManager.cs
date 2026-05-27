using System;
using System.Collections.Generic;
using System.Linq;
using DrawClient.Models;

namespace DrawClient.Services
{
    public class UndoRedoManager
    {
        private readonly List<DrawAction> _allActions = new List<DrawAction>();
        private readonly Dictionary<string, DrawAction> _actionMap = new Dictionary<string, DrawAction>();
        private readonly object _lock = new object();
        private const int MAX_HISTORY = 10000;

        // Group tracking: each stroke (MouseDown→MouseUp) shares a StrokeGroupId.
        // The undo/redo stacks store groupIds, not individual actionIds.
        private readonly Dictionary<string, List<string>> _groupToActions = new Dictionary<string, List<string>>(); // groupId → actionIds
        private readonly Dictionary<string, string> _actionToGroup = new Dictionary<string, string>(); // actionId → groupId

        // Per-user stacks of groupIds
        private readonly Dictionary<int, Stack<string>> _undoGroupStacks = new Dictionary<int, Stack<string>>();
        private readonly Dictionary<int, Stack<string>> _redoGroupStacks = new Dictionary<int, Stack<string>>();

        public int UndoCount
        {
            get { lock (_lock) { return _undoGroupStacks.Values.Sum(s => s.Count); } }
        }

        public int RedoCount
        {
            get { lock (_lock) { return _redoGroupStacks.Values.Sum(s => s.Count); } }
        }

        public void AddAction(DrawAction action)
        {
            if (action == null) return;
            lock (_lock)
            {
                _allActions.Add(action);
                _actionMap[action.Id] = action;

                // Normalise: if no group assigned, the action is its own group.
                string groupId = string.IsNullOrEmpty(action.StrokeGroupId) ? action.Id : action.StrokeGroupId;
                action.StrokeGroupId = groupId;

                _actionToGroup[action.Id] = groupId;
                if (!_groupToActions.ContainsKey(groupId))
                    _groupToActions[groupId] = new List<string>();
                _groupToActions[groupId].Add(action.Id);

                int uid = action.UserId;
                if (!_undoGroupStacks.ContainsKey(uid))
                {
                    _undoGroupStacks[uid] = new Stack<string>();
                    _redoGroupStacks[uid] = new Stack<string>();
                }

                // Push groupId to undo stack only when starting a new group
                if (_undoGroupStacks[uid].Count == 0 || _undoGroupStacks[uid].Peek() != groupId)
                    _undoGroupStacks[uid].Push(groupId);

                // Any new draw action clears the redo stack for that user
                _redoGroupStacks[uid].Clear();

                // Trim history
                if (_allActions.Count > MAX_HISTORY)
                {
                    int toRemove = _allActions.Count - MAX_HISTORY;
                    var removed = _allActions.GetRange(0, toRemove);
                    var removedIds = new HashSet<string>(removed.Select(a => a.Id));
                    _allActions.RemoveRange(0, toRemove);

                    foreach (var a in removed)
                    {
                        _actionMap.Remove(a.Id);
                        if (_actionToGroup.TryGetValue(a.Id, out var gid))
                        {
                            _actionToGroup.Remove(a.Id);
                            if (_groupToActions.TryGetValue(gid, out var ids))
                            {
                                ids.Remove(a.Id);
                                if (ids.Count == 0)
                                    _groupToActions.Remove(gid);
                            }
                        }
                    }

                    // Remove trimmed groupIds from stacks
                    var survivingGroups = new HashSet<string>(_groupToActions.Keys);
                    foreach (var stackUid in _undoGroupStacks.Keys.ToList())
                    {
                        var undoList = _undoGroupStacks[stackUid].ToList();
                        if (undoList.Any(g => !survivingGroups.Contains(g)))
                            _undoGroupStacks[stackUid] = new Stack<string>(undoList.Where(g => survivingGroups.Contains(g)).Reverse());

                        var redoList = _redoGroupStacks[stackUid].ToList();
                        if (redoList.Any(g => !survivingGroups.Contains(g)))
                            _redoGroupStacks[stackUid] = new Stack<string>(redoList.Where(g => survivingGroups.Contains(g)).Reverse());
                    }
                }
            }
        }

        // Returns all DrawActions in the undone group, or null if nothing to undo.
        public List<DrawAction> Undo(int userId)
        {
            lock (_lock)
            {
                if (!_undoGroupStacks.ContainsKey(userId) || _undoGroupStacks[userId].Count == 0)
                    return null;

                string groupId = _undoGroupStacks[userId].Pop();
                _redoGroupStacks[userId].Push(groupId);

                var result = new List<DrawAction>();
                if (_groupToActions.TryGetValue(groupId, out var actionIds))
                {
                    foreach (string aid in actionIds)
                    {
                        if (_actionMap.TryGetValue(aid, out var a))
                        {
                            a.IsUndone = true;
                            result.Add(a);
                        }
                    }
                }
                return result.Count > 0 ? result : null;
            }
        }

        // Returns all DrawActions in the redone group, or null if nothing to redo.
        public List<DrawAction> Redo(int userId)
        {
            lock (_lock)
            {
                if (!_redoGroupStacks.ContainsKey(userId) || _redoGroupStacks[userId].Count == 0)
                    return null;

                string groupId = _redoGroupStacks[userId].Pop();
                _undoGroupStacks[userId].Push(groupId);

                var result = new List<DrawAction>();
                if (_groupToActions.TryGetValue(groupId, out var actionIds))
                {
                    foreach (string aid in actionIds)
                    {
                        if (_actionMap.TryGetValue(aid, out var a))
                        {
                            a.IsUndone = false;
                            result.Add(a);
                        }
                    }
                }
                return result.Count > 0 ? result : null;
            }
        }

        // Undo all actions in a group by groupId — used for network sync (one message per stroke).
        public bool UndoByGroupId(string groupId)
        {
            lock (_lock)
            {
                if (!_groupToActions.TryGetValue(groupId, out var actionIds) || actionIds.Count == 0)
                    return false;

                if (!_actionMap.TryGetValue(actionIds[0], out var first))
                    return false;

                int uid = first.UserId;

                foreach (var aid in actionIds)
                {
                    if (_actionMap.TryGetValue(aid, out var a))
                        a.IsUndone = true;
                }

                // Remove groupId from undo stack, push to redo stack
                if (_undoGroupStacks.TryGetValue(uid, out var undoStack))
                {
                    var temp = new Stack<string>();
                    bool found = false;
                    while (undoStack.Count > 0)
                    {
                        string g = undoStack.Pop();
                        if (g == groupId && !found) { found = true; break; }
                        temp.Push(g);
                    }
                    while (temp.Count > 0) undoStack.Push(temp.Pop());

                    if (found)
                    {
                        if (!_redoGroupStacks.ContainsKey(uid))
                            _redoGroupStacks[uid] = new Stack<string>();
                        _redoGroupStacks[uid].Push(groupId);
                    }
                }

                return true;
            }
        }

        // Redo all actions in a group by groupId — used for network sync.
        public bool RedoByGroupId(string groupId)
        {
            lock (_lock)
            {
                if (!_groupToActions.TryGetValue(groupId, out var actionIds) || actionIds.Count == 0)
                    return false;

                if (!_actionMap.TryGetValue(actionIds[0], out var first))
                    return false;

                int uid = first.UserId;

                foreach (var aid in actionIds)
                {
                    if (_actionMap.TryGetValue(aid, out var a))
                        a.IsUndone = false;
                }

                if (_redoGroupStacks.TryGetValue(uid, out var redoStack))
                {
                    var temp = new Stack<string>();
                    bool found = false;
                    while (redoStack.Count > 0)
                    {
                        string g = redoStack.Pop();
                        if (g == groupId && !found) { found = true; break; }
                        temp.Push(g);
                    }
                    while (temp.Count > 0) redoStack.Push(temp.Pop());

                    if (found)
                    {
                        if (!_undoGroupStacks.ContainsKey(uid))
                            _undoGroupStacks[uid] = new Stack<string>();
                        _undoGroupStacks[uid].Push(groupId);
                    }
                }

                return true;
            }
        }

        // Used for remote UNDO sync — undoes one specific action by ID.
        public DrawAction UndoById(string actionId)
        {
            lock (_lock)
            {
                if (!_actionMap.TryGetValue(actionId, out var action))
                    return null;

                action.IsUndone = true;

                int uid = action.UserId;
                // Remove the groupId from undo stack if present, push to redo stack
                if (_undoGroupStacks.ContainsKey(uid))
                {
                    string groupId = action.StrokeGroupId ?? actionId;
                    var stack = _undoGroupStacks[uid];
                    var temp = new Stack<string>();
                    bool found = false;
                    while (stack.Count > 0)
                    {
                        string g = stack.Pop();
                        if (g == groupId && !found) { found = true; break; }
                        temp.Push(g);
                    }
                    while (temp.Count > 0) stack.Push(temp.Pop());
                    if (found && _redoGroupStacks.ContainsKey(uid))
                        _redoGroupStacks[uid].Push(groupId);
                }

                return action;
            }
        }

        // Used for remote REDO sync — redoes one specific action by ID.
        public DrawAction RedoById(string actionId)
        {
            lock (_lock)
            {
                if (!_actionMap.TryGetValue(actionId, out var action))
                    return null;

                action.IsUndone = false;

                int uid = action.UserId;
                if (_redoGroupStacks.ContainsKey(uid))
                {
                    string groupId = action.StrokeGroupId ?? actionId;
                    var stack = _redoGroupStacks[uid];
                    var temp = new Stack<string>();
                    bool found = false;
                    while (stack.Count > 0)
                    {
                        string g = stack.Pop();
                        if (g == groupId && !found) { found = true; break; }
                        temp.Push(g);
                    }
                    while (temp.Count > 0) stack.Push(temp.Pop());
                    if (found && _undoGroupStacks.ContainsKey(uid))
                        _undoGroupStacks[uid].Push(groupId);
                }

                return action;
            }
        }

        public bool CanUndo(int userId)
        {
            lock (_lock) { return _undoGroupStacks.ContainsKey(userId) && _undoGroupStacks[userId].Count > 0; }
        }

        public bool CanRedo(int userId)
        {
            lock (_lock) { return _redoGroupStacks.ContainsKey(userId) && _redoGroupStacks[userId].Count > 0; }
        }

        public bool CanUndo() { lock (_lock) { return _undoGroupStacks.Any(kv => kv.Value.Count > 0); } }
        public bool CanRedo() { lock (_lock) { return _redoGroupStacks.Any(kv => kv.Value.Count > 0); } }

        public List<DrawAction> GetAllActions()
        {
            lock (_lock) { return _allActions.Where(a => !a.IsUndone).ToList(); }
        }

        public List<DrawAction> GetAllActionsIncludingUndone()
        {
            lock (_lock) { return new List<DrawAction>(_allActions); }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _allActions.Clear();
                _actionMap.Clear();
                _groupToActions.Clear();
                _actionToGroup.Clear();
                _undoGroupStacks.Clear();
                _redoGroupStacks.Clear();
            }
        }
    }
}
