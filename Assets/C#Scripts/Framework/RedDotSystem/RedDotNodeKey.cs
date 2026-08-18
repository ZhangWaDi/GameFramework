using System;

namespace GameFramework.RedDotSystem
{
    /// <summary>
    /// 使用红点类型和可选实例 ID 唯一标识一个红点节点。
    /// </summary>
    public readonly struct RedDotNodeKey : IEquatable<RedDotNodeKey>
    {
        private RedDotNodeKey(Enum redDotType, int instanceId, bool hasInstanceId)
        {
            RedDotType = redDotType ?? throw new ArgumentNullException(nameof(redDotType));
            InstanceId = instanceId;
            HasInstanceId = hasInstanceId;
        }

        public Enum RedDotType { get; }
        public int InstanceId { get; }
        public bool HasInstanceId { get; }
        public bool IsValid => RedDotType != null;

        /// <summary>
        /// 创建不区分业务实例 ID 的单例红点节点 Key。
        /// </summary>
        public static RedDotNodeKey Singleton(Enum redDotType)
        {
            return new RedDotNodeKey(redDotType, default, false);
        }

        /// <summary>
        /// 创建由业务实例 ID 区分的红点节点 Key。
        /// </summary>
        public static RedDotNodeKey Create(Enum redDotType, int instanceId)
        {
            return new RedDotNodeKey(redDotType, instanceId, true);
        }

        public bool Equals(RedDotNodeKey other)
        {
            return Equals(RedDotType, other.RedDotType) && InstanceId == other.InstanceId && HasInstanceId == other.HasInstanceId;
        }

        public override bool Equals(object obj)
        {
            return obj is RedDotNodeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = RedDotType != null ? RedDotType.GetType().GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (RedDotType != null ? RedDotType.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ InstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ HasInstanceId.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            if (RedDotType == null)
            {
                return "InvalidRedDotNodeKey";
            }

            string typeName = $"{RedDotType.GetType().Name}.{RedDotType}";
            return HasInstanceId ? $"{typeName}[{InstanceId}]" : $"{typeName}[Singleton]";
        }

        public static bool operator ==(RedDotNodeKey left, RedDotNodeKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RedDotNodeKey left, RedDotNodeKey right)
        {
            return !left.Equals(right);
        }
    }
}
