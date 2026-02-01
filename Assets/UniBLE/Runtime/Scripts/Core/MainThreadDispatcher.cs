using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniBLE
{
    /// <summary>
    /// Utility for dispatching actions to the Unity main thread
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour, IBleDispatcher
    {
        private static MainThreadDispatcher _instance;

        /// <summary>
        /// Returns the singleton instance, initializing if necessary.
        /// </summary>
        public static MainThreadDispatcher Instance
        {
            get
            {
                if (!_isInitialized) Initialize();
                return _instance;
            }
        }

        private static readonly object _lock = new object();
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();
        private static volatile bool _isInitialized;

        /// <summary>
        /// Initialize the dispatcher automatically when the game starts
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_isInitialized) return;

            var go = new GameObject("UniBLE_MainThreadDispatcher");
            _instance = go.AddComponent<MainThreadDispatcher>();
            DontDestroyOnLoad(go);
            _isInitialized = true;
        }

        /// <summary>
        /// Enqueue an action to be executed on the main thread
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            lock (_lock)
            {
                _actionQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// IBleDispatcher implementation — delegates to Enqueue
        /// </summary>
        public void Dispatch(Action action) => Enqueue(action);

        private void Update()
        {
            Action[] actions = null;
            lock (_lock)
            {
                if (_actionQueue.Count > 0)
                {
                    actions = _actionQueue.ToArray();
                    _actionQueue.Clear();
                }
            }

            if (actions != null)
            {
                foreach (var action in actions)
                {
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
