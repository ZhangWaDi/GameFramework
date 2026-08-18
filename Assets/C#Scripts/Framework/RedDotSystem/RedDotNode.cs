using System;
using System.Collections.Generic;

namespace GameFramework.RedDotSystem
{
    /// <summary>
    /// 保存一个具体红点实例的状态和父子关系，不持有任何 Unity 场景对象。
    /// </summary>
    internal sealed class RedDotNode
    {
        internal RedDotNode(RedDotNodeKey key, BaseRedDot rule)
        {
            Key = key;
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        internal RedDotNodeKey Key { get; }
        internal BaseRedDot Rule { get; }
        internal HashSet<RedDotNodeKey> ParentKeys { get; } = new();
        internal Dictionary<RedDotNodeKey, int> ChildValues { get; } = new();
        internal int SelfValue { get; set; }
        internal int TotalValue { get; set; }
        internal bool IsActive => TotalValue > 0;

        internal int CalculateTotalValue()
        {
            long totalValue = SelfValue;
            foreach (int childValue in ChildValues.Values)
            {
                totalValue += childValue;
                if (totalValue > int.MaxValue)
                {
                    throw new OverflowException($"红点节点“{Key}”的聚合数值超过 Int32 最大值。");
                }
            }

            return (int)totalValue;
        }
    }
}
