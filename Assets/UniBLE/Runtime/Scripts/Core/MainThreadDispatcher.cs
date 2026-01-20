using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniBLE
{
    /// <summary>
    /// Utility for dispatching actions to the Unity main thread
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();
        private static bool _isInitialized;

        /// <summary>
        /// Enqueue an action to be executed on the main thread
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            EnsureInitialized();

            lock (_lock)
            {
                _actionQueue.Enqueue(action);
            }
        }

        private static void EnsureInitialized()
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                var go = new GameObject("UniBLE_MainThreadDispatcher");
                _instance = go.AddComponent<MainThreadDispatcher>();
                DontDestroyOnLoad(go);
                _isInitialized = true;
            }
        }

        private void Update()
        {
            lock (_lock)
            {
                while (_actionQueue.Count > 0)
                {
                    var action = _actionQueue.Dequeue();
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            _isInitialized = false;
            _instance = null;
        }
    }
}
