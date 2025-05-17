// Utility class to execute code on Unity's main thread

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private readonly Queue<Action> _executionQueue = new Queue<Action>();
    private readonly Queue<TaskCompletionSource<bool>> _taskCompletionSources = new Queue<TaskCompletionSource<bool>>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
                if (_taskCompletionSources.Count > 0)
                {
                    _taskCompletionSources.Dequeue().SetResult(true);
                }
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    public Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();

        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
            _taskCompletionSources.Enqueue(tcs);
        }

        return tcs.Task;
    }
}
