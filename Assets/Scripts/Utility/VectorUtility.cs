using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class VectorUtility
{
    /// <summary>
    /// 获取两点之间距离一定百分比的一个点
    /// </summary>
    /// <param name="start">起始点</param>
    /// <param name="end">结束点</param>
    /// <param name="distance">起始点到目标点距离百分比</param>
    /// <returns></returns>
    public static Vector3 GetBetweenPointByPercent(Vector3 start, Vector3 end, float percent=0.5f)
    {
        Vector3 normal = (end - start).normalized;
        float distance = Vector3.Distance(start, end);
        return normal * (distance * percent) + start;
    }
 
    /// <summary>
    /// 获取两点之间一定距离的点
    /// </summary>
    /// <param name="start">起始点</param>
    /// <param name="end">结束点</param>
    /// <param name="distance">距离</param>
    /// <returns></returns>
    public static Vector3 GetBetweenPoint(Vector3 start, Vector3 end, float distance)
    {
        Vector3 normal = (end - start).normalized;
        return normal * distance + start;
    }
}
