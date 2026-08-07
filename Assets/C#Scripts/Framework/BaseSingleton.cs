using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework
{
    /// <summary>
    /// 不依赖 Unity 生命周期的普通 C# 单例基类。
    /// </summary>
    /// <typeparam name="T">继承当前基类的具体单例类型。</typeparam>
    public abstract class Singleton<T> where T : Singleton<T>, new()
    {
        private static readonly object SyncRoot = new();
        private static T instance;

        /// <summary>
        /// 获取单例。首次访问时创建实例，但不会自动调用 <see cref="OnInit"/>。
        /// </summary>
        public static T Instance
        {
            get
            {
                lock (SyncRoot)
                {
                    return instance ??= new T();
                }
            }
        }

        /// <summary>
        /// 当前是否已经创建实例。读取该属性不会创建实例。
        /// </summary>
        public static bool HasInstance
        {
            get
            {
                lock (SyncRoot)
                {
                    return instance != null;
                }
            }
        }

        /// <summary>
        /// 尝试获取已创建的实例。调用该方法不会创建实例。
        /// </summary>
        public static bool TryGetInstance(out T value)
        {
            lock (SyncRoot)
            {
                value = instance;
                return value != null;
            }
        }

        /// <summary>
        /// 由项目启动流程显式调用的初始化入口。
        /// </summary>
        public virtual void OnInit()
        {
        }

        /// <summary>
        /// 实例被清除前的释放入口。
        /// </summary>
        protected virtual void OnRelease()
        {
        }

        /// <summary>
        /// 清除当前实例。未创建实例时不会为了清理而创建新实例。
        /// </summary>
        public static void Clear()
        {
            T instanceToRelease;

            lock (SyncRoot)
            {
                instanceToRelease = instance;
                instance = null;
            }

            instanceToRelease?.OnRelease();
        }
    }

    /// <summary>
    /// 依赖 Unity 生命周期的 MonoBehaviour 单例基类。
    /// 仅应在 Unity 主线程的运行时访问。
    /// </summary>
    /// <typeparam name="T">继承当前基类的具体 MonoBehaviour 类型。</typeparam>
    [DisallowMultipleComponent]
    public abstract class SingletonMono<T> : MonoBehaviour where T : SingletonMono<T>
    {
        private static T instance;

        /// <summary>
        /// 获取场景中的单例；不存在时会创建常驻 GameObject。
        /// 在编辑模式或应用退出过程中不会创建对象。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                if (!Application.isPlaying)
                {
                    Debug.LogError(
                        $"[{typeof(T).Name}] 不能在非运行状态下自动创建 MonoBehaviour 单例。");
                    return null;
                }

                if (SingletonRuntimeState.IsQuitting)
                {
                    return null;
                }

                instance = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
                if (instance != null)
                {
                    Object.DontDestroyOnLoad(instance.gameObject);
                    return instance;
                }

                GameObject singletonObject = new(typeof(T).Name);
                instance = singletonObject.AddComponent<T>();
                return instance;
            }
        }

        /// <summary>
        /// 当前是否存在有效实例。读取该属性不会查找或创建对象。
        /// </summary>
        public static bool HasInstance => instance != null;

        /// <summary>
        /// 尝试获取当前有效实例。调用该方法不会查找或创建对象。
        /// </summary>
        public static bool TryGetInstance(out T value)
        {
            value = instance;
            return value != null;
        }

        protected virtual void Awake()
        {
            T current = this as T;
            if (current == null)
            {
                Debug.LogError(
                    $"{GetType().Name} 的单例泛型参数必须是自身类型，例如 SingletonMono<{GetType().Name}>。",
                    this);
                Object.Destroy(this);
                return;
            }

            if (instance != null && instance != current)
            {
                Debug.LogWarning(
                    $"检测到重复的 {typeof(T).Name} 单例，已销毁后创建的 GameObject。",
                    gameObject);
                Object.Destroy(gameObject);
                return;
            }

            instance = current;
            Object.DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 由项目启动流程显式调用的初始化入口。
        /// </summary>
        public virtual void OnInit()
        {
        }

        protected virtual void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }

    /// <summary>
    /// 共享 MonoBehaviour 单例的应用退出状态，并兼容关闭 Domain Reload 的编辑器设置。
    /// </summary>
    internal static class SingletonRuntimeState
    {
        internal static bool IsQuitting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            IsQuitting = true;
        }
    }
}
