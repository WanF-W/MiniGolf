using UnityEngine;
using System.Collections;

/// <summary>
/// 相机运镜控制器
/// 管理三个预设点位间的平滑运镜，以及球飞行时的跟随
///
/// 运镜流程：
/// 1. 游戏开始 → 依次运镜到点位1(球洞检视) → 点位2(整体检视) → 点位3(球后准备)
/// 2. 玩家开始拖拽 → 运镜到点位3(球后准备发射)
/// 3. 球发射 → 从右后方俯瞰左前方跟随球移动（偏移角度可调）
/// 4. 球落地停下 → 运镜回到点位2(整体检视)
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("=== 运镜点位 ===")]
    [Tooltip("点位1：球洞检视视角")]
    public Transform viewPoint1;

    [Tooltip("点位2：整体检视视角")]
    public Transform viewPoint2;

    [Tooltip("点位3：球后准备发射视角")]
    public Transform viewPoint3;

    [Header("=== 运镜参数 ===")]
    [Tooltip("运镜速度（值越大运镜越快）")]
    public float cinematicSpeed = 0.8f;

    [Tooltip("每个点位停留时间（秒）")]
    public float pointHoldTime = 1.5f;

    [Header("=== 球跟随参数 ===")]
    [Tooltip("相机在球后方的距离")]
    public float followDistance = 6f;

    [Tooltip("相机在球上方的高度")]
    public float followHeight = 4f;

    [Tooltip("视线看向前方的距离")]
    public float lookAheadDistance = 5f;

    [Tooltip("跟随平滑速度")]
    public float followSmoothSpeed = 3f;

    [Tooltip("方向平滑速度（相机转向的平滑程度）")]
    public float directionSmoothTime = 0.3f;

    [Tooltip("相机跟随角度偏移（度，正值向右后方偏移）")]
    public float followAngleOffset = 20f;

    [Header("=== 引用 ===")]
    [Tooltip("游戏管理器")]
    public GameManager gameManager;

    // 相机状态枚举
    public enum CameraState { None, Intro, Following, Transitioning }
    public CameraState cameraState { get; private set; } = CameraState.None;

    // 球的Transform
    private Transform ball;

    // 球的Rigidbody
    private Rigidbody ballRb;

    // 当前运行的协程
    private Coroutine currentCoroutine;

    // 跟随时的平滑方向
    private Vector3 smoothedForward = Vector3.forward;
    private Vector3 forwardVelocity = Vector3.zero;
    private bool followInitialized = false;

    void LateUpdate()
    {
        // 只有在跟随状态下才在LateUpdate中处理
        if (cameraState == CameraState.Following)
        {
            UpdateFollow();
        }
    }

    #region 开场运镜

    /// <summary>
    /// 开始开场运镜（游戏开始时由GameManager调用）
    /// 依次经过点位1 → 点位2 → 点位3
    /// </summary>
    public void StartIntro(Transform ballTransform)
    {
        ball = ballTransform;
        ballRb = ball != null ? ball.GetComponent<Rigidbody>() : null;

        if (currentCoroutine != null)
            StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(IntroCoroutine());
    }

    /// <summary>
    /// 开场运镜协程
    /// </summary>
    IEnumerator IntroCoroutine()
    {
        cameraState = CameraState.Intro;

        // 运镜到点位1：球洞检视
        if (viewPoint1 != null)
        {
            yield return MoveToPoint(viewPoint1);
            yield return new WaitForSeconds(pointHoldTime);
        }

        // 运镜到点位2：整体检视
        if (viewPoint2 != null)
        {
            yield return MoveToPoint(viewPoint2);
            yield return new WaitForSeconds(pointHoldTime);
        }

        // 运镜到点位3：球后准备发射
        if (viewPoint3 != null)
        {
            yield return MoveToPoint(viewPoint3);
            yield return new WaitForSeconds(pointHoldTime);
        }

        cameraState = CameraState.None;

        // 通知GameManager运镜完成
        gameManager?.OnIntroComplete();
    }

    #endregion

    #region 球跟随

    /// <summary>
    /// 开始跟随球（球发射后由GameManager调用）
    /// </summary>
    public void StartFollowing()
    {
        if (currentCoroutine != null)
            StopCoroutine(currentCoroutine);

        followInitialized = false;
        cameraState = CameraState.Following;
    }

    /// <summary>
    /// LateUpdate中更新跟随逻辑
    /// 相机从右后方俯瞰左前方，偏移角度由followAngleOffset控制
    /// </summary>
    void UpdateFollow()
    {
        if (ball == null || ballRb == null) return;

        // 获取球的水平速度方向
        Vector3 velocity = ballRb.velocity;
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);

        if (horizontalVel.magnitude > 0.5f)
        {
            Vector3 targetForward = horizontalVel.normalized;

            if (!followInitialized)
            {
                // 第一帧直接设置方向
                smoothedForward = targetForward;
                followInitialized = true;
            }
            else
            {
                // 平滑过渡方向
                smoothedForward = Vector3.SmoothDamp(
                    smoothedForward, targetForward, ref forwardVelocity, directionSmoothTime);
            }
        }

        if (!followInitialized) return;

        // 计算相机偏移方向：从正后方向右偏移followAngleOffset度
        // 180度=正后方，减去偏移角度=右后方
        Vector3 cameraOffsetDir = Quaternion.Euler(0, 180f - followAngleOffset, 0) * smoothedForward;

        // 相机目标位置：球的右后方 + 上方
        Vector3 desiredPos = ball.position
            + cameraOffsetDir * followDistance
            + Vector3.up * followHeight;

        // 平滑移动相机位置
        transform.position = Vector3.Lerp(
            transform.position, desiredPos, followSmoothSpeed * Time.deltaTime);

        // 相机看向球前方（从右后方看左前方）
        Vector3 lookTarget = ball.position + smoothedForward * lookAheadDistance;
        Quaternion targetRot = Quaternion.LookRotation(lookTarget - transform.position);

        // 平滑旋转相机
        transform.rotation = Quaternion.Slerp(
            transform.rotation, targetRot, followSmoothSpeed * Time.deltaTime);
    }

    #endregion

    #region 运镜过渡

    /// <summary>
    /// 运镜到点位3（球后准备发射视角）
    /// 玩家开始拖拽时由GameManager调用
    /// </summary>
    public void MoveToLaunchPosition()
    {
        if (currentCoroutine != null)
            StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(TransitionToPoint(viewPoint3));
    }

    /// <summary>
    /// 运镜回到点位2（整体检视视角）
    /// 球落地停下时由GameManager调用
    /// </summary>
    public void ReturnToOverview()
    {
        if (currentCoroutine != null)
            StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(TransitionToPoint(viewPoint2));
    }

    /// <summary>
    /// 运镜过渡协程
    /// </summary>
    IEnumerator TransitionToPoint(Transform target)
    {
        if (target == null)
        {
            cameraState = CameraState.None;
            yield break;
        }

        cameraState = CameraState.Transitioning;
        yield return MoveToPoint(target);
        cameraState = CameraState.None;
    }

    /// <summary>
    /// 平滑移动到目标点位（位置 + 旋转）
    /// 使用SmoothStep缓动，出入更自然
    /// </summary>
    IEnumerator MoveToPoint(Transform target)
    {
        if (target == null) yield break;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 endPos = target.position;
        Quaternion endRot = target.rotation;

        float duration = 1f / cinematicSpeed;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            // SmoothStep缓动：ease in-out
            t = t * t * (3f - 2f * t);

            transform.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, t), Quaternion.Slerp(startRot, endRot, t));
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 确保最终位置精确
        transform.SetPositionAndRotation(endPos, endRot);
    }

    #endregion
}
