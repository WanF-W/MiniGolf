using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 轨迹显示组件
/// 球运动时按固定时间间隔记录位置，用LineRenderer渲染完整运动轨迹
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryDisplay : MonoBehaviour
{
    [Header("=== 轨迹参数 ===")]
    [Tooltip("采样间隔（秒），每隔多久记录一个轨迹点。值越小轨迹越精细")]
    public float sampleInterval = 0.02f;

    [Tooltip("轨迹线起始宽度")]
    public float startWidth = 0.06f;

    [Tooltip("轨迹线结束宽度")]
    public float endWidth = 0.02f;

    [Tooltip("轨迹最大点数，超过则移除最早的点（防止无限增长）")]
    public int maxPoints = 3000;

    [Header("=== 轨迹颜色 ===")]
    [Tooltip("轨迹起始颜色（球发射处）")]
    public Color startColor = new Color(1f, 0.8f, 0f, 0.9f);   // 橙黄色

    [Tooltip("轨迹结束颜色（球当前位置）")]
    public Color endColor = new Color(0f, 1f, 0.6f, 0.9f);      // 青绿色

    // LineRenderer组件
    private LineRenderer lineRenderer;

    // 轨迹点列表
    private List<Vector3> trajectoryPoints = new List<Vector3>();

    // 采样计时器
    private float sampleTimer = 0f;

    // 是否正在记录
    private bool isRecording = false;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        SetupLineRenderer();
    }

    /// <summary>
    /// 初始化LineRenderer参数
    /// </summary>
    void SetupLineRenderer()
    {
        lineRenderer.startWidth = startWidth;
        lineRenderer.endWidth = endWidth;
        lineRenderer.startColor = startColor;
        lineRenderer.endColor = endColor;
        lineRenderer.positionCount = 0;
        lineRenderer.useWorldSpace = true;  // 使用世界坐标，这样轨迹线不会随球移动
        lineRenderer.numCornerVertices = 5;  // 拐角圆滑度
        lineRenderer.numCapVertices = 5;     // 端点圆滑度
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
    }

    void Update()
    {
        if (!isRecording) return;

        sampleTimer += Time.deltaTime;

        // 达到采样间隔时记录一个点
        if (sampleTimer >= sampleInterval)
        {
            sampleTimer = 0f;
            AddPoint(transform.position);
        }
    }

    /// <summary>
    /// 添加一个轨迹点并更新LineRenderer
    /// </summary>
    void AddPoint(Vector3 point)
    {
        trajectoryPoints.Add(point);

        // 超过最大点数时移除最早的点
        if (trajectoryPoints.Count > maxPoints)
        {
            trajectoryPoints.RemoveAt(0);
        }

        // 更新LineRenderer
        lineRenderer.positionCount = trajectoryPoints.Count;
        lineRenderer.SetPositions(trajectoryPoints.ToArray());
    }

    /// <summary>
    /// 开始记录轨迹（球发射时调用）
    /// </summary>
    public void StartRecording()
    {
        isRecording = true;
        sampleTimer = sampleInterval;  // 立即记录第一个点
    }

    /// <summary>
    /// 停止记录轨迹（球停下时调用）
    /// </summary>
    public void StopRecording()
    {
        isRecording = false;
    }

    /// <summary>
    /// 清除所有轨迹（重置游戏时调用）
    /// </summary>
    public void ClearTrajectory()
    {
        isRecording = false;
        trajectoryPoints.Clear();
        lineRenderer.positionCount = 0;
    }
}
