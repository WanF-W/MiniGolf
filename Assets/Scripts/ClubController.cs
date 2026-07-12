using UnityEngine;

/// <summary>
/// 高尔夫球棍控制器
/// 根据玩家拖拽力度模拟球棍的蓄力后拉和挥杆击球动作
///
/// 三个Transform定义球棍的运动范围和旋转中心：
/// - clubStart: 球棍击球位置（位置1，靠近球，挥杆终点）
/// - clubEnd:   球棍最大蓄力位置（位置2，向后拉到最远）
/// - pivot:     旋转中心点（球棍握柄端，球棍绕此点旋转）
///
/// 动作流程：
/// 1. 拖拽蓄力 → 球棍绕pivot从clubStart弧线移动到clubEnd（后拉蓄力）
/// 2. 松手挥杆 → 球棍绕pivot从当前位置弧线回到clubStart（挥杆击球）
/// 3. 挥杆到达impactPoint时机 → 触发回调，球被击飞
/// 4. 挥杆动画继续完成 → 球棍隐藏，等球停下后重新出现在新位置
///
/// 注意：clubStart、clubEnd、pivot建议都作为球的子对象，这样球移动后位置自动跟随
/// </summary>
public class ClubController : MonoBehaviour
{
    [Header("=== 音频资源 ===")]
    [Tooltip("击球音效")]
    public AudioClip hitAudio;

    [Tooltip("音频开始时间，去除前奏")]
    public float startTime = 0.9f;

    [Header("=== 球棍位置 ===")]
    [Tooltip("球棍击球位置（位置1，靠近球，挥杆终点）")]
    public Transform clubStart;

    [Tooltip("球棍最大蓄力位置（位置2，向后拉到最远）")]
    public Transform clubEnd;

    [Tooltip("旋转中心点（球棍握柄端，球棍绕此点做弧线运动）。为空则直接线性插值")]
    public Transform pivot;

    [Header("=== 动作参数 ===")]
    [Tooltip("蓄力时球棍跟随力度的平滑速度")]
    public float pullBackSmoothSpeed = 12f;

    [Tooltip("挥杆动画持续时间（秒），值越小挥杆越快")]
    public float swingDuration = 0.12f;

    [Tooltip("挥杆结束后是否自动隐藏球棍")]
    public bool hideAfterSwing = true;

    [Tooltip("球被击飞的时机（0~1，0=挥杆开始瞬间，1=挥杆完全结束）。建议0.8~0.9，让球在球棍即将触球时飞出")]
    public float impactPoint = 0.85f;

    /// <summary>
    /// 击球回调（挥杆到达impactPoint时触发，球在此时被击飞）
    /// </summary>
    public System.Action onClubImpact;

    // 当前力度（0~1，由BallController设置）
    private float currentPower = 0f;

    // 是否正在挥杆
    private bool isSwinging = false;

    // 击球回调是否已触发（每次挥杆只触发一次）
    private bool impactTriggered = false;

    // 挥杆计时器
    private float swingTimer = 0f;

    // 挥杆起始位置和旋转
    private Vector3 swingStartPos;
    private Quaternion swingStartRot;

    void Update()
    {
        if (isSwinging)
        {
            UpdateSwing();
        }
        else
        {
            UpdatePullBack();
        }
    }

    // 替代 PlayClipAtPoint，支持设置起始时间
    public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float startTime = 0f)
    {
        // 1. 创建临时物体
        GameObject tempGO = new GameObject("TempAudio");
        tempGO.transform.position = position;

        // 2. 添加 AudioSource 并设置参数
        AudioSource audioSource = tempGO.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.time = startTime;      // 关键：设置起始时间
        audioSource.spatialBlend = 1f;     // 3D 音效
        audioSource.Play();

        // 3. 播放完毕后自动销毁
        Destroy(tempGO, clip.length - startTime);
    }

    /// <summary>
    /// 根据力度计算球棍目标位置（支持绕pivot弧线运动）
    /// </summary>
    Vector3 GetTargetPosition(float power, Vector3 startPos, Vector3 endPos)
    {
        if (pivot != null)
        {
            // 绕pivot做弧线运动：用Slerp插值方向，使球棍头部沿弧线移动
            Vector3 pivotPos = pivot.position;
            Vector3 startDir = startPos - pivotPos;
            Vector3 endDir = endPos - pivotPos;
            return pivotPos + Vector3.Slerp(startDir, endDir, power);
        }
        else
        {
            // 无pivot时直接线性插值
            return Vector3.Lerp(startPos, endPos, power);
        }
    }

    /// <summary>
    /// 蓄力中：球棍从clubStart向clubEnd移动
    /// 根据当前力度在两个位置间插值
    /// </summary>
    void UpdatePullBack()
    {
        if (clubStart == null || clubEnd == null) return;

        // 目标位置：绕pivot弧线插值或线性插值
        Vector3 targetPos = GetTargetPosition(currentPower, clubStart.position, clubEnd.position);
        Quaternion targetRot = Quaternion.Slerp(clubStart.rotation, clubEnd.rotation, currentPower);

        // 平滑移动到目标位置
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, targetPos, pullBackSmoothSpeed * Time.deltaTime), Quaternion.Slerp(transform.rotation, targetRot, pullBackSmoothSpeed * Time.deltaTime));
    }

    /// <summary>
    /// 挥杆中：球棍从当前位置快速回到clubStart
    /// 使用EaseOutCubic曲线，开始快然后减速，模拟挥杆冲击感
    /// 在impactPoint时机触发击球回调，球棍继续挥完后再隐藏
    /// </summary>
    void UpdateSwing()
    {
        swingTimer += Time.deltaTime;
        float t = swingTimer / swingDuration;

        // 在impactPoint时机触发击球回调（球被击飞）
        if (!impactTriggered && t >= impactPoint)
        {
            PlayClipAtPoint(hitAudio, Camera.main.transform.position, startTime);
            impactTriggered = true;
            onClubImpact?.Invoke();
        }

        if (t >= 1f)
        {
            // 挥杆完全结束
            isSwinging = false;
            transform.SetPositionAndRotation(clubStart.position, clubStart.rotation);

            // 隐藏球棍
            if (hideAfterSwing)
                gameObject.SetActive(false);
        }
        else
        {
            // EaseOutCubic：开始快然后减速
            float easedT = 1f - Mathf.Pow(1f - t, 3f);

            // 绕pivot弧线运动
            Vector3 targetPos = GetTargetPosition(easedT, swingStartPos, clubStart.position);
            transform.SetPositionAndRotation(targetPos, Quaternion.Slerp(swingStartRot, clubStart.rotation, easedT));
        }
    }

    /// <summary>
    /// 更新当前力度（由BallController在拖拽时调用）
    /// </summary>
    public void UpdatePower(float power)
    {
        if (!isSwinging)
        {
            currentPower = Mathf.Clamp01(power);
        }
    }

    /// <summary>
    /// 触发挥杆（由BallController在松手时调用）
    /// 球棍从当前位置快速挥回clubStart
    /// </summary>
    public void Swing()
    {
        if (isSwinging) return;

        isSwinging = true;
        impactTriggered = false;
        swingTimer = 0f;
        swingStartPos = transform.position;
        swingStartRot = transform.rotation;
    }

    /// <summary>
    /// 重置球棍到击球位置并显示
    /// （球停下或游戏重置时调用）
    /// </summary>
    public void ResetClub()
    {
        isSwinging = false;
        impactTriggered = false;
        currentPower = 0f;
        gameObject.SetActive(true);

        if (clubStart != null)
        {
            transform.SetPositionAndRotation(clubStart.position, clubStart.rotation);
        }
    }
}
