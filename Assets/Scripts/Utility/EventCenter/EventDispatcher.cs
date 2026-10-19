using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using UnityUtils;

[DisallowMultipleComponent]
public class EventDispatcher : Singleton<EventDispatcher>
{
    // 基础事件容器
    private Dictionary<string, UnityEvent> _eventTable = new Dictionary<string, UnityEvent>();
    
    // 泛型事件容器（支持带参数的事件）
    private Dictionary<string, System.Object> _genericEventTable = new Dictionary<string, System.Object>();

    //=== 基础事件方法 ===//
    
    /// <summary>
    /// 订阅无参数事件
    /// </summary>
    public void AddListener(string eventName, UnityAction handler)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            Debug.LogError("事件名不能为空", this);
            return;
        }

        if (!_eventTable.TryGetValue(eventName, out UnityEvent unityEvent))
        {
            unityEvent = new UnityEvent();
            _eventTable.Add(eventName, unityEvent);
        }

        unityEvent.AddListener(handler);
    }

    /// <summary>
    /// 触发无参数事件
    /// </summary>
    public void Dispatch(string eventName)
    {
        if (_eventTable.TryGetValue(eventName, out UnityEvent unityEvent))
        {
            unityEvent.Invoke();
        }
        else
        {
            Debug.LogWarning($"未注册的事件: {eventName}", this);
        }
    }

    //=== 泛型事件方法 ===//
    
    /// <summary>
    /// 订阅带参数事件
    /// </summary>
    public void AddListener<T>(string eventName, UnityAction<T> handler)
    {
        if (!_genericEventTable.TryGetValue(eventName, out System.Object objEvent))
        {
            var genericEvent = new UnityEvent<T>();
            genericEvent.AddListener(handler);
            _genericEventTable.Add(eventName, genericEvent);
        }
        else
        {
            if (objEvent is UnityEvent<T> genericEvent)
            {
                genericEvent.AddListener(handler);
            }
            else
            {
                Debug.LogError($"事件类型不匹配: {eventName}", this);
            }
        }
    }

    /// <summary>
    /// 触发带参数事件
    /// </summary>
    public void Dispatch<T>(string eventName, T eventData)
    {
        if (_genericEventTable.TryGetValue(eventName, out System.Object objEvent))
        {
            if (objEvent is UnityEvent<T> genericEvent)
            {
                genericEvent.Invoke(eventData);
            }
            else
            {
                Debug.LogError($"事件参数类型不匹配: {eventName}", this);
            }
        }
        else
        {
            Debug.LogWarning($"未注册的泛型事件: {eventName}", this);
        }
    }

    //=== 清理方法 ===//
    
    /// <summary>
    /// 移除指定事件的所有监听
    /// </summary>
    public void RemoveEvent(string eventName)
    {
        _eventTable.Remove(eventName);
        _genericEventTable.Remove(eventName);
    }

    /// <summary>
    /// 清空所有事件监听
    /// </summary>
    public void ClearAllListeners()
    {
        _eventTable.Clear();
        _genericEventTable.Clear();
    }

    void OnDestroy()
    {
        ClearAllListeners(); // 对象销毁时自动清理
    }
}
