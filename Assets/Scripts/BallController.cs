using UnityEngine;

/// <summary>
/// 高尔夫球控制器
/// 处理玩家拖拽输入、力度计算、方向锁定和球的发射
///
/// 操作流程：
/// 1. 玩家鼠标按下球棍 → 开始拖拽，球棍开始后拉蓄力（球本身不动）
/// 2. 向下拖拽鼠标 → 拖拽距离决定力度（只取鼠标Y轴），球棍向后移动
/// 3. 拖拽期间 → 方向仪表盘自动左右摆动
/// 4. 松手 → 球棍挥杆动画播放
/// 5. 挥杆动画完成瞬间 → 球被击飞发射
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    [Header("=== 力度参数 ===")]
    [Tooltip("最大屏幕拖拽距离（像素），向下拖拽此距离时力度达到最大")]
    public float maxScreenDragDistance = 300f;

    [Tooltip("最大发射力度")]
    public float maxPower = 60f;

    [Tooltip("最小有效力度，低于此值视为取消击球")]
    public float minEffectivePower = 0.05f;

    [Header("=== 发射参数 ===")]
    [Tooltip("发射仰角（度），给球一个向上的分量，使其有抛物线轨迹")]
    public float launchAngle = 20f;

    [Header("=== 物理参数 ===")]
    [Tooltip("球的弹力（0~1，值越大落地弹得越高）")]
    public float bounciness = 0.6f;

    [Tooltip("球的摩擦力（0~1，值越大滚动减速越快）")]
    public float friction = 0.4f;

    [Header("=== 引用对象 ===")]
    [Tooltip("方向仪表盘组件（控制摆动和方向锁定）")]
    public DirectionIndicator directionIndicator;

    [Tooltip("球棍控制器（蓄力和挥杆动画）")]
    public ClubController clubController;

    [Tooltip("球洞Transform，用于确定基准击球方向")]
    public Transform holeTransform;

    [Tooltip("轨迹显示组件")]
    public TrajectoryDisplay trajectoryDisplay;

    [Tooltip("游戏管理器")]
    public GameManager gameManager;

    // 刚体组件
    private Rigidbody rb;

    // 拖拽状态
    private bool isDragging = false;

    // 鼠标按下时的Y坐标
    private float startMouseY;

    // 当前力度（0~1标准化值）
    private float currentPower = 0f;

    // 基准方向（球到球洞的水平方向，Y轴归零）
    private Vector3 baseForward = Vector3.forward;

    // === 待发射参数（松手后暂存，等挥杆完成时使用） ===
    private bool isWaitingForSwing = false;
    private float pendingLockedAngle = 0f;
    private float pendingFinalPower = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        SetupPhysicMaterial();
    }

    /// <summary>
    /// 在代码中创建并设置球的物理材质
    /// 确保弹力和摩擦力由脚本控制，不依赖场景中的材质配置
    /// </summary>
    void SetupPhysicMaterial()
    {
        if (TryGetComponent<SphereCollider>(out var collider))
        {
            PhysicMaterial mat = new PhysicMaterial("BallPhysicMaterial")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Maximum
            };
            collider.material = mat;
        }
    }

    void Update()
    {
        // 只有在Idle和Dragging状态才允许操作
        if (gameManager != null && gameManager.CurrentState != GameState.Idle && gameManager.CurrentState != GameState.Dragging)
            return;

        // === 鼠标按下：检测是否点中了球棍 ===
        if (Input.GetMouseButtonDown(0))
        {
            if (clubController == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                // 检测是否点击了球棍本身或其子对象
                if (hit.collider.transform == clubController.transform ||
                    hit.collider.transform.IsChildOf(clubController.transform))
                {
                    StartDragging();
                }
            }
        }

        // === 拖拽中：持续更新力度 ===
        if (isDragging && Input.GetMouseButton(0))
        {
            UpdateDrag();
        }

        // === 松手：触发挥杆，球在挥杆完成时发射 ===
        if (isDragging && Input.GetMouseButtonUp(0))
        {
            ReleaseBall();
        }
    }

    /// <summary>
    /// 开始拖拽
    /// 记录鼠标Y，计算基准方向，启动方向仪表盘
    /// </summary>
    void StartDragging()
    {
        isDragging = true;

        // 记录鼠标初始Y坐标
        startMouseY = Input.mousePosition.y;

        // 计算基准方向：从球指向球洞的水平方向
        if (holeTransform != null)
        {
            baseForward = holeTransform.position - transform.position;
            baseForward.y = 0;
            baseForward.Normalize();
        }
        else
        {
            baseForward = Vector3.forward;
        }

        // 启动方向仪表盘摆动
        if (directionIndicator != null)
        {
            directionIndicator.StartSwinging();
        }

        // 球棍归零力度（回到clubStart位置）
        if (clubController != null)
        {
            clubController.UpdatePower(0f);
        }

        // 切换游戏状态（会触发相机运镜到点位3）
        if (gameManager != null)
        {
            gameManager.SetState(GameState.Dragging);
        }
    }

    /// <summary>
    /// 拖拽中，实时更新力度
    /// 只取鼠标Y轴变化量，球本身不动，球棍根据力度后移
    /// </summary>
    void UpdateDrag()
    {
        // 计算鼠标Y轴拖拽量（向下拖为正值）
        float dragDeltaY = startMouseY - Input.mousePosition.y;

        // 只取正值（向下拖拽才蓄力），钳制到最大距离
        float clampedDelta = Mathf.Clamp(dragDeltaY, 0f, maxScreenDragDistance);

        // 计算标准化力度（0~1）
        currentPower = clampedDelta / maxScreenDragDistance;

        // 更新球棍位置（球棍根据力度后拉）
        if (clubController != null)
        {
            clubController.UpdatePower(currentPower);
        }

        // 通知GameManager更新力度UI
        if (gameManager != null)
        {
            gameManager.UpdatePowerUI(currentPower);
        }
    }

    /// <summary>
    /// 松手触发挥杆
    /// 锁定方向仪表盘角度，暂存发射参数，触发球棍挥杆动画
    /// 球在挥杆动画完成时才被发射（OnSwingComplete回调）
    /// </summary>
    void ReleaseBall()
    {
        isDragging = false;

        // 锁定方向仪表盘当前角度
        float lockedAngle = 0f;
        if (directionIndicator != null)
        {
            lockedAngle = directionIndicator.LockDirection();
            directionIndicator.StopSwinging();
        }

        // 力度过小，取消击球
        if (currentPower < minEffectivePower)
        {
            currentPower = 0f;
            // 球棍回到起始位置（不挥杆）
            if (clubController != null)
            {
                clubController.UpdatePower(0f);
            }
            if (gameManager != null)
            {
                gameManager.SetState(GameState.Idle);
                gameManager.UpdatePowerUI(0f);
            }
            return;
        }

        // 暂存发射参数，等挥杆完成时使用
        pendingLockedAngle = lockedAngle;
        pendingFinalPower = currentPower * maxPower;
        isWaitingForSwing = true;

        // 触发球棍挥杆动画，注册击球回调
        if (clubController != null)
        {
            clubController.onClubImpact = OnSwingComplete;
            clubController.Swing();
        }
        else
        {
            // 没有球棍，直接发射
            OnSwingComplete();
        }

        // 隐藏力度条
        if (gameManager != null)
        {
            gameManager.UpdatePowerUI(0f);
        }

        currentPower = 0f;
    }

    /// <summary>
    /// 挥杆完成回调：球棍击中球的瞬间，球被发射
    /// </summary>
    void OnSwingComplete()
    {
        if (!isWaitingForSwing) return;
        isWaitingForSwing = false;

        // 计算发射方向：基准方向 + 锁定角度偏移
        Vector3 launchDir = Quaternion.Euler(0, pendingLockedAngle, 0) * baseForward;

        // 添加仰角分量（水平方向乘cos，垂直方向乘sin）
        float radAngle = launchAngle * Mathf.Deg2Rad;
        Vector3 force = new Vector3(
            launchDir.x * Mathf.Cos(radAngle),
            Mathf.Sin(radAngle),
            launchDir.z * Mathf.Cos(radAngle)
        ) * pendingFinalPower;

        // 施加脉冲力发射球
        rb.AddForce(force, ForceMode.Impulse);

        // 通知GameManager（会触发相机跟随）
        if (gameManager != null)
        {
            gameManager.AddStroke();
            gameManager.SetState(GameState.Moving);
        }

        // 开始记录轨迹
        if (trajectoryDisplay != null)
        {
            trajectoryDisplay.StartRecording();
        }
    }

    /// <summary>
    /// 重置球到指定位置（用于游戏重置）
    /// 同时重置Rigidbody状态确保位置正确
    /// </summary>
    public void ResetBall(Vector3 position)
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        transform.position = position;
        rb.WakeUp();
    }

    /// <summary>
    /// 强制停止球（落地后超时调用）
    /// </summary>
    public void ForceStop()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 检测球是否已停下（速度和角速度都接近0）
    /// </summary>
    public bool IsStopped()
    {
        return rb.velocity.magnitude < 0.05f && rb.angularVelocity.magnitude < 0.05f;
    }

    /// <summary>
    /// 获取球的Rigidbody（供GameManager检测落地用）
    /// </summary>
    public Rigidbody GetRigidbody()
    {
        return rb;
    }
}
