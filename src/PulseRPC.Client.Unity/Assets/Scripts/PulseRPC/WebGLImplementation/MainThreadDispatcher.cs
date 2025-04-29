using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulseRPC.WebGLImplementation
{
    /// <summary>
    /// 用于在Unity主线程上执行回调的组件
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly Queue<Action> _actions = new Queue<Action>();
        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _initialized = true;
        }

        private void Update()
        {
            // 执行队列中的所有操作
            lock (_lock)
            {
                while (_actions.Count > 0)
                {
                    Action action = _actions.Dequeue();
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"执行主线程操作时出错: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 将操作排队在主线程上执行
        /// </summary>
        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
                _actions.Enqueue(action);
            }

            EnsureInstanceExists();
        }

        /// <summary>
        /// 确保分发器实例存在
        /// </summary>
        private static void EnsureInstanceExists()
        {
            if (_initialized)
                return;

            if (_instance == null && Application.isPlaying)
            {
                _initialized = true;
                var go = new GameObject("PulseRPC_MainThreadDispatcher");
                _instance = go.AddComponent<MainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
        }
    }
}
