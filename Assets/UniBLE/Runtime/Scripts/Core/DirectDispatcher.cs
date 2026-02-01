using System;

namespace UniBLE
{
    /// <summary>
    /// Dispatcher that invokes actions directly on the calling thread.
    /// Use this when you do not need callbacks dispatched to the Unity main thread.
    /// </summary>
    public class DirectDispatcher : IBleDispatcher
    {
        public void Dispatch(Action action) => action?.Invoke();
    }
}
