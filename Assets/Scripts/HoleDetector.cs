using UnityEngine;

/// <summary>
/// 球洞检测器
/// 通过Trigger碰撞检测球是否进入球洞
/// 只有球实际碰到球洞的Collider才判定进球，飞过洞口不会进球
///
/// 注意：球洞对象需要有Collider并勾选Is Trigger
/// Collider高度应较低（如0.2），确保只有地面滚动的球才能进入
/// </summary>
public class HoleDetector : MonoBehaviour
{
    [Header("=== 引用 ===")]
    [Tooltip("游戏管理器")]
    public GameManager gameManager;

    // 球的Transform
    private Transform ball;

    /// <summary>
    /// 设置球引用（由GameManager在Start中调用）
    /// </summary>
    public void SetBall(Transform ballTransform)
    {
        ball = ballTransform;
    }

    /// <summary>
    /// 球进入球洞的Trigger区域
    /// 只有实际碰撞才触发，飞过洞口不会触发
    /// </summary>
    void OnTriggerEnter(Collider other)
    {
        if (gameManager == null || ball == null) return;

        // 只有球在运动状态才检测进洞
        if (gameManager.CurrentState != GameState.Moving) return;

        // 检测进入的是否是球
        if (other.transform == ball)
        {
            gameManager.OnBallHoled();
        }
    }
}
