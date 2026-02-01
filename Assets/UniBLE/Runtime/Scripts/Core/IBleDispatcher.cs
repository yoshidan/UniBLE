using System;

namespace UniBLE
{
    public interface IBleDispatcher
    {
        void Dispatch(Action action);
    }
}
