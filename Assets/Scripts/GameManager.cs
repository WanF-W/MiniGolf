using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏状态枚举
/// </summary>
public enum GameState
{
    Intro,      // 开场运镜中：相机依次经过点位1→2→3
    Idle,       // 待击球：等待玩家按下球棍
    Dragging,   // 拖拽中：玩家正在拖拽确定力度，仪表盘正在摆动
    Moving,     // 球运动中：球已发射，正在飞行或滚动
    Holed       // 进球：球已进入球洞
}

/// <summary>
/// 游戏管理器
/// 统一管理游戏状态、杆数计数、UI更新、相机运镜、落地检测和游戏重置
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("=== 游戏对象引用 ===")]
    [Tooltip("高尔夫球控制器")]
    public BallController ballController;

    [Tooltip("球棍控制器")]
    public ClubController clubController;

    [Tooltip("轨迹显示组件")]
    public TrajectoryDisplay trajectoryDisplay;

    [Tooltip("相机跟随组件")]
    public CameraFollow cameraFollow;

    [Tooltip("球洞检测器")]
    public HoleDetector holeDetector;

    [Tooltip("球洞Transform，用于计算球到球洞的距离")]
    public Transform holeTransform;

    [Tooltip("球的初始位置标记（空GameObject，放在球起始位置）")]
    public Transform ballStartPoint;

    [Header("=== UI引用 ===")]
    [Tooltip("力度条Slider（拖拽时显示力度，带颜色渐变）")]
    public Slider powerSlider;

    [Tooltip("杆数显示文本")]
    public Text strokeCountText;

    [Tooltip("进球提示文本（进球时显示）")]
    public Text holeMessageText;

    [Tooltip("距球洞距离显示文本（球停下时显示）")]
    public Text distanceText;

    [Tooltip("操作提示文本（运镜结束后显示，球发射时隐藏）")]
    public Text tipText;

    [Header("=== 停止检测参数 ===")]
    [Tooltip("球发射后至少经过多少秒才开始检测停下（用于未离开地面的球）")]
    public float stopCheckDelay = 0.5f;

    [Tooltip("球速度持续低于阈值多少秒才判定为停下")]
    public float stopDuration = 0.5f;

    [Tooltip("球掉出地图的Y轴最低值，低于此值视为掉出")]
    public float fallThreshold = -5f;

    [Header("=== 落地检测参数 ===")]
    [Tooltip("判定球在空中的高度阈值（相对发射点Y的偏移量）")]
    public float airHeightThreshold = 0.5f;

    [Tooltip("判定球落地的Y轴容差（相对发射点Y的偏移量）")]
    public float landYThreshold = 0.3f;

    [Tooltip("落地后允许滚动的时间（秒），超过后强制停止")]
    public float rollTimeAfterLanding = 3f;

    /// <summary>
    /// 当前游戏状态（只读属性，外部可读取不可修改）
    /// </summary>
    public GameState CurrentState { get; private set; }

    // 杆数计数
    private int strokeCount = 0;

    // 球发射后的计时器
    private float moveTimer = 0f;

    // 球持续低速的计时器
    private float lowSpeedTimer = 0f;

    // === 落地检测相关 ===
    private bool hasBeenInAir = false;      // 球是否曾离开地面
    private bool hasLanded = false;          // 球是否已落地
    private float landTimer = 0f;            // 落地后计时
    private float groundLevel = 0f;          // 发射时的地面Y坐标

    // 蓄力条颜色渐变（绿→黄→橙→红）
    private Gradient powerGradient;

    void Start()
    {
        // 初始化为开场运镜状态
        CurrentState = GameState.Intro;
        strokeCount = 0;
        UpdateStrokeUI();

        // 初始化蓄力条渐变色
        SetupPowerGradient();

        // 隐藏所有可操作的UI
        if (powerSlider != null)
            powerSlider.gameObject.SetActive(false);
        if (holeMessageText != null)
            holeMessageText.gameObject.SetActive(false);
        if (distanceText != null)
            distanceText.gameObject.SetActive(false);
        if (tipText != null)
            tipText.gameObject.SetActive(false);

        // 设置球洞检测器的球引用
        if (holeDetector != null && ballController != null)
        {
            holeDetector.SetBall(ballController.transform);
        }

        // 游戏开始时将球归位到ballStartPoint位置
        if (ballController != null && ballStartPoint != null)
        {
            ballController.ResetBall(ballStartPoint.position);
        }

        // 球棍在运镜期间隐藏
        if (clubController != null)
        {
            clubController.gameObject.SetActive(false);
        }

        // 开始开场运镜
        if (cameraFollow != null && ballController != null)
        {
            cameraFollow.StartIntro(ballController.transform);
        }
        else
        {
            // 没有相机引用，直接进入Idle
            SetState(GameState.Idle);
        }
    }

    /// <summary>
    /// 创建蓄力条颜色渐变：绿→黄→橙→红
    /// </summary>
    void SetupPowerGradient()
    {
        powerGradient = new Gradient();

        GradientColorKey[] colorKeys = new GradientColorKey[4];
        colorKeys[0].color = Color.green;        // 0% 绿色
        colorKeys[0].time = 0f;
        colorKeys[1].color = Color.yellow;       // 40% 黄色
        colorKeys[1].time = 0.4f;
        colorKeys[2].color = new Color(1f, 0.5f, 0f); // 70% 橙色
        colorKeys[2].time = 0.7f;
        colorKeys[3].color = Color.red;          // 100% 红色
        colorKeys[3].time = 1f;

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0].alpha = 1f;
        alphaKeys[0].time = 0f;
        alphaKeys[1].alpha = 1f;
        alphaKeys[1].time = 1f;

        powerGradient.SetKeys(colorKeys, alphaKeys);
    }

    void Update()
    {
        // === 球运动状态下的处理 ===
        if (CurrentState == GameState.Moving)
        {
            moveTimer += Time.deltaTime;

            // 检测球是否掉出地图
            if (ballController != null && ballController.transform.position.y < fallThreshold)
            {
                OnBallOutOfBounds();
                return;
            }

            float ballY = ballController != null ? ballController.transform.position.y : 0f;

            // === 落地检测 ===
            // 检测球是否离开地面（在空中）
            if (!hasBeenInAir && ballY > groundLevel + airHeightThreshold)
            {
                hasBeenInAir = true;
            }

            // 检测球是否落地（曾经在空中，现在回到地面附近）
            if (hasBeenInAir && !hasLanded && ballY <= groundLevel + landYThreshold)
            {
                hasLanded = true;
                landTimer = 0f;
            }

            // 落地后计时，达到设定时间后强制停止
            if (hasLanded)
            {
                landTimer += Time.deltaTime;
                if (landTimer >= rollTimeAfterLanding)
                {
                    // 强制停止球
                    if (ballController != null)
                    {
                        ballController.ForceStop();
                    }
                    OnBallStopped();
                    return;
                }
            }

            // === 低速检测（用于未离开地面的球）===
            if (moveTimer >= stopCheckDelay && !hasLanded)
            {
                if (ballController != null && ballController.IsStopped())
                {
                    lowSpeedTimer += Time.deltaTime;

                    // 持续低速达到指定时长，判定为停下
                    if (lowSpeedTimer >= stopDuration)
                    {
                        OnBallStopped();
                    }
                }
                else
                {
                    // 速度恢复则重置低速计时
                    lowSpeedTimer = 0f;
                }
            }
        }

        // R键重置游戏
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetGame();
        }
    }

    /// <summary>
    /// 设置游戏状态
    /// 根据状态自动控制UI、相机运镜、球棍和提示文本的显隐
    /// </summary>
    public void SetState(GameState newState)
    {
        CurrentState = newState;

        switch (newState)
        {
            case GameState.Intro:
                if (powerSlider != null) powerSlider.gameObject.SetActive(false);
                if (tipText != null) tipText.gameObject.SetActive(false);
                moveTimer = 0f;
                lowSpeedTimer = 0f;
                break;

            case GameState.Idle:
                if (powerSlider != null) powerSlider.gameObject.SetActive(false);
                // 注意：TipText和球杆不在此处显示
                // 它们只在运镜结束(OnIntroComplete)或按下R(ResetGame)时显示
                moveTimer = 0f;
                lowSpeedTimer = 0f;
                break;

            case GameState.Dragging:
                if (powerSlider != null) powerSlider.gameObject.SetActive(true);
                if (powerSlider != null) powerSlider.value = 0f;
                moveTimer = 0f;
                lowSpeedTimer = 0f;
                // 相机运镜到点位3（球后准备发射视角）
                if (cameraFollow != null)
                    cameraFollow.MoveToLaunchPosition();
                break;

            case GameState.Moving:
                if (powerSlider != null) powerSlider.gameObject.SetActive(false);
                // 球发射，隐藏提示文本（球棍由挥杆动画自动隐藏）
                if (tipText != null) tipText.gameObject.SetActive(false);
                moveTimer = 0f;
                lowSpeedTimer = 0f;
                // 重置落地检测
                hasBeenInAir = false;
                hasLanded = false;
                landTimer = 0f;
                // 记录发射时的地面Y坐标
                groundLevel = ballController != null ? ballController.transform.position.y : 0f;
                // 相机开始跟随球
                if (cameraFollow != null)
                    cameraFollow.StartFollowing();
                break;

            case GameState.Holed:
                if (powerSlider != null) powerSlider.gameObject.SetActive(false);
                if (tipText != null) tipText.gameObject.SetActive(false);
                break;
        }
    }

    /// <summary>
    /// 开场运镜完成回调（由CameraFollow调用）
    /// 运镜结束后显示提示文本和球棍
    /// </summary>
    public void OnIntroComplete()
    {
        // 运镜结束，显示球棍和提示文本
        if (clubController != null)
        {
            clubController.ResetClub();
        }
        if (tipText != null)
        {
            tipText.gameObject.SetActive(true);
        }

        SetState(GameState.Idle);
    }

    /// <summary>
    /// 更新力度UI（同时更新数值和填充颜色）
    /// </summary>
    public void UpdatePowerUI(float power)
    {
        if (powerSlider != null)
        {
            powerSlider.value = power;

            // 更新蓄力条填充颜色（绿→黄→橙→红渐变）
            if (powerGradient != null && powerSlider.fillRect != null)
            {
                if (powerSlider.fillRect.TryGetComponent<Image>(out var fillImage))
                {
                    fillImage.color = powerGradient.Evaluate(power);
                }
            }
        }
    }

    /// <summary>
    /// 增加杆数
    /// </summary>
    public void AddStroke()
    {
        strokeCount++;
        UpdateStrokeUI();
    }

    /// <summary>
    /// 更新杆数UI
    /// </summary>
    void UpdateStrokeUI()
    {
        if (strokeCountText != null)
        {
            strokeCountText.text = "杆数: " + strokeCount;
        }
    }

    /// <summary>
    /// 球停下处理
    /// </summary>
    void OnBallStopped()
    {
        // 停止记录轨迹
        if (trajectoryDisplay != null)
        {
            trajectoryDisplay.StopRecording();
        }

        // 相机运镜回到点位2（整体检视视角）
        if (cameraFollow != null)
        {
            cameraFollow.ReturnToOverview();
        }

        // 计算并显示球到球洞的距离
        UpdateDistanceUI();

        // 重置球棍到新位置（球棍跟随球，因为clubStart是球的子对象）
        if (clubController != null)
        {
            //clubController.ResetClub();
        }

        // 回到待击球状态（会显示提示文本和球棍）
        SetState(GameState.Idle);
    }

    /// <summary>
    /// 计算球到球洞的水平距离并显示
    /// </summary>
    void UpdateDistanceUI()
    {
        if (distanceText == null || ballController == null || holeTransform == null) return;

        Vector3 ballPos = ballController.transform.position;
        Vector3 holePos = holeTransform.position;

        // 只计算水平距离（忽略Y轴高度差）
        float distance = new Vector2(
            ballPos.x - holePos.x,
            ballPos.z - holePos.z
        ).magnitude;

        distanceText.text = "距球洞: " + distance.ToString("F1") + " 米";
        distanceText.gameObject.SetActive(true);
    }

    /// <summary>
    /// 球掉出地图处理
    /// </summary>
    void OnBallOutOfBounds()
    {
        // 停止记录轨迹
        if (trajectoryDisplay != null)
        {
            trajectoryDisplay.StopRecording();
            trajectoryDisplay.ClearTrajectory();
        }

        // 球重置到起点
        if (ballController != null && ballStartPoint != null)
        {
            ballController.ResetBall(ballStartPoint.position);
        }

        // 相机运镜回到点位2
        if (cameraFollow != null)
        {
            cameraFollow.ReturnToOverview();
        }

        // 重置球棍
        if (clubController != null)
        {
            //clubController.ResetClub();
        }

        SetState(GameState.Idle);
    }

    /// <summary>
    /// 进球处理（由HoleDetector调用）
    /// </summary>
    public void OnBallHoled()
    {
        SetState(GameState.Holed);

        // 停止记录轨迹
        if (trajectoryDisplay != null)
        {
            trajectoryDisplay.StopRecording();
        }

        // 显示进球提示
        if (holeMessageText != null)
        {
            holeMessageText.text = "进球！总杆数: " + strokeCount;
            holeMessageText.gameObject.SetActive(true);
        }

        // 隐藏球和球棍
        if (ballController != null)
        {
            ballController.gameObject.SetActive(false);
        }
        if (clubController != null)
        {
            clubController.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 重置游戏（R键或UI按钮调用）
    /// 不清空杆数，只重置球位置和UI
    /// </summary>
    public void ResetGame()
    {
        // 重置球到初始位置
        if (ballController != null && ballStartPoint != null)
        {
            ballController.gameObject.SetActive(true);
            ballController.ResetBall(ballStartPoint.position);
        }

        // 隐藏进球提示和距离信息
        if (holeMessageText != null)
        {
            holeMessageText.gameObject.SetActive(false);
        }
        if (distanceText != null)
        {
            distanceText.gameObject.SetActive(false);
        }

        // 清除轨迹
        if (trajectoryDisplay != null)
        {
            trajectoryDisplay.ClearTrajectory();
        }

        // 相机运镜到点位3（准备发射视角，可直接开始游戏）
        if (cameraFollow != null)
        {
            cameraFollow.MoveToLaunchPosition();
        }

        // 重置球棍并显示
        if (clubController != null)
        {
            clubController.ResetClub();
        }

        // 显示提示文本
        if (tipText != null)
        {
            tipText.gameObject.SetActive(true);
        }

        SetState(GameState.Idle);
    }
}
