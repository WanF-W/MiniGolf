using UnityEngine;

/// <summary>
/// 方向仪表盘控制器
/// 拖拽时指针自动左右摆动，松手时锁定当前角度作为击球方向
///
/// 优化：指针经过中心±10度区域时速度减慢，方便玩家瞄准
/// </summary>
public class DirectionIndicator : MonoBehaviour
{
    [Header("=== 摆动参数 ===")]
    [Tooltip("最大摆动角度（度），指针在 -maxAngle 到 +maxAngle 之间来回摆动")]
    public float maxAngle = 45f;

    [Tooltip("摆动速度（度/秒），数值越大指针摆动越快")]
    public float swingSpeed = 120f;

    [Header("=== 中心慢速区域 ===")]
    [Tooltip("中心慢速区域角度范围（度），在此范围内摆动速度减慢")]
    public float slowZoneAngle = 10f;

    [Tooltip("慢速区域的速度倍数（0~1，越小越慢）")]
    public float slowZoneSpeedMultiplier = 0.4f;

    [Header("=== UI显示 ===")]
    [Tooltip("UI指针Transform（2D仪表盘上的指针，绕Z轴旋转）")]
    public RectTransform uiPointer;

    [Tooltip("仪表盘面板GameObject（控制显示/隐藏）")]
    public GameObject dashboardPanel;

    // 当前摆动角度（-maxAngle ~ +maxAngle）
    private float currentAngle = 0f;

    // 摆动方向：1=正向递增，-1=反向递减
    private int swingDirection = 1;

    // 是否正在摆动
    private bool isSwinging = false;

    void Update()
    {
        if (!isSwinging) return;

        // 根据当前角度计算摆动速度
        float currentSpeed = swingSpeed;
        if (Mathf.Abs(currentAngle) < slowZoneAngle)
        {
            // 在中心慢速区域内，降低摆动速度
            currentSpeed = swingSpeed * slowZoneSpeedMultiplier;
        }

        // 按摆动速度更新角度
        currentAngle += swingDirection * currentSpeed * Time.deltaTime;

        // 到达边界时反转摆动方向
        if (currentAngle >= maxAngle)
        {
            currentAngle = maxAngle;
            swingDirection = -1;
        }
        else if (currentAngle <= -maxAngle)
        {
            currentAngle = -maxAngle;
            swingDirection = 1;
        }

        // 更新UI指针旋转（UI坐标中Z轴旋转对应屏幕上的旋转）
        if (uiPointer != null)
        {
            uiPointer.localEulerAngles = new Vector3(0, 0, -currentAngle);
        }
    }

    /// <summary>
    /// 开始摆动，重置角度为0
    /// </summary>
    public void StartSwinging()
    {
        isSwinging = true;
        currentAngle = 0f;
        swingDirection = 1;

        // 显示仪表盘面板
        if (dashboardPanel != null)
            dashboardPanel.SetActive(true);
    }

    /// <summary>
    /// 停止摆动，隐藏显示
    /// </summary>
    public void StopSwinging()
    {
        isSwinging = false;

        // 隐藏仪表盘面板
        if (dashboardPanel != null)
            dashboardPanel.SetActive(false);
    }

    /// <summary>
    /// 锁定当前方向，返回锁定角度
    /// 松手时由BallController调用
    /// </summary>
    public float LockDirection()
    {
        return currentAngle;
    }

    /// <summary>
    /// 获取当前实时角度（用于预览）
    /// </summary>
    public float GetCurrentAngle()
    {
        return currentAngle;
    }
}
