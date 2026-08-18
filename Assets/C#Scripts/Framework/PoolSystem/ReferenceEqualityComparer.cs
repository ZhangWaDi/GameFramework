using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameFramework.PoolSystem
{
    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
