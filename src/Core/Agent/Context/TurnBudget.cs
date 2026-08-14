namespace GEmuera.Core.Agent.Context;

/// <summary>
/// 每回合 token 预算执行器。由解释器线程同步调用（CALLSHARP 语义），
/// 检查点流水线跨线程重置——用 lock 保证余量扣减的原子性。
/// 离线兜底不变量：TryConsume 余量不足时返回 false 且不扣减，调用方走降级。
/// </summary>
public sealed class TurnBudget
{
    readonly object gate = new();
    readonly int tokensPerTurn;
    int remaining;

    public TurnBudget(int tokensPerTurn)
    {
        this.tokensPerTurn = tokensPerTurn;
        remaining = tokensPerTurn;
    }

    /// <summary>当前剩余额度（快照语义）。</summary>
    public int Remaining
    {
        get { lock (gate) return remaining; }
    }

    /// <summary>尝试扣减；余量不足返回 false 且不扣减。</summary>
    public bool TryConsume(int tokens)
    {
        lock (gate)
        {
            if (tokens < 0 || tokens > remaining)
                return false;
            remaining -= tokens;
            return true;
        }
    }

    /// <summary>回合边界重置额度。</summary>
    public void Reset()
    {
        lock (gate) remaining = tokensPerTurn;
    }
}
