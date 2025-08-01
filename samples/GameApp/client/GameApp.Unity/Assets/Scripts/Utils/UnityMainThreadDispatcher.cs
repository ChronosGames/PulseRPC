using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameApp.Unity.Utils
{
    /// <summary>
    /// Unity 主线程调度器 - 用于在主线程中执行操作
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> ExecutionQueue = new Queue<Action>();

        public static UnityMainThreadDispatcher Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            lock (ExecutionQueue)
            {
                while (ExecutionQueue.Count > 0)
                {
                    try
                    {
                        ExecutionQueue.Dequeue().Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error executing queued action: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 在主线程中执行操作
        /// </summary>
        public void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (ExecutionQueue)
            {
                ExecutionQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// 在主线程中延迟执行操作
        /// </summary>
        public void EnqueueDelayed(Action action, float delay)
        {
            if (action == null)
            {
                return;
            }

            StartCoroutine(DelayedExecute(action, delay));
        }

        private IEnumerator DelayedExecute(Action action, float delay)
        {
            yield return new WaitForSeconds(delay);
            Enqueue(action);
        }

        /// <summary>
        /// 检查当前是否在主线程
        /// </summary>
        public static bool IsMainThread()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
        }

        /// <summary>
        /// 确保在主线程中执行操作
        /// </summary>
        public static void EnsureMainThread(Action action)
        {
            if (IsMainThread())
            {
                action?.Invoke();
            }
            else
            {
                Instance?.Enqueue(action);
            }
        }
    }
}
