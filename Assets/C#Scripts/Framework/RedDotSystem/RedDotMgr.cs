using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.RedDotSystem
{
    /// <summary>
    /// 管理红点类型规则、具体实例、GameObject 绑定和多父节点状态传播。
    /// </summary>
    public sealed class RedDotMgr : Singleton<RedDotMgr>
    {
        private readonly Dictionary<Enum, BaseRedDot> rules = new();
        private readonly Dictionary<RedDotNodeKey, RedDotNode> nodes = new();
        private readonly Dictionary<Enum, HashSet<RedDotNodeKey>> typeIndex = new();
        private readonly Dictionary<RedDotNodeKey, Dictionary<int, GameObject>> bindings = new();
        private readonly Dictionary<int, RedDotNodeKey> objectBindingKeys = new();
        private readonly HashSet<RedDotNodeKey> nodesBeingCreated = new();
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public int RuleCount => rules.Count;
        public int NodeCount => nodes.Count;

        public override void OnInit()
        {
            if (isInitialized)
            {
                return;
            }

            isInitialized = true;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>
        /// 注册一种红点类型及其唯一规则实例。
        /// </summary>
        public void RegisterRedDot(Enum redDotType, BaseRedDot rule)
        {
            EnsureInitialized();
            ValidateRedDotType(redDotType);
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            if (rules.ContainsKey(redDotType))
            {
                throw new InvalidOperationException($"红点类型“{FormatRedDotType(redDotType)}”已经注册，不能重复注册规则。");
            }

            rules.Add(redDotType, rule);
            typeIndex.Add(redDotType, new HashSet<RedDotNodeKey>());

            try
            {
                rule.Initialize(this, redDotType);
            }
            catch
            {
                rules.Remove(redDotType);
                typeIndex.Remove(redDotType);
                throw;
            }
        }

        /// <summary>
        /// 创建或获取指定红点实例，并立即执行一次自检。
        /// </summary>
        public void RegisterNode(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            EnsureNode(nodeKey);
        }

        public void RegisterSingletonNode(Enum redDotType)
        {
            RegisterNode(RedDotNodeKey.Singleton(redDotType));
        }

        /// <summary>
        /// 将 GameObject 绑定到红点实例。绑定后立即同步当前显隐状态。
        /// 同一个红点实例可以绑定多个 GameObject，同一个 GameObject 只能绑定一个红点实例。
        /// </summary>
        public void BindRedDot(RedDotNodeKey nodeKey, GameObject redDotObject)
        {
            EnsureInitialized();
            if (redDotObject == null)
            {
                throw new ArgumentNullException(nameof(redDotObject));
            }

            RedDotNode node = EnsureNode(nodeKey);
            int objectId = redDotObject.GetInstanceID();

            if (objectBindingKeys.TryGetValue(objectId, out RedDotNodeKey oldNodeKey) && oldNodeKey != nodeKey)
            {
                RemoveBinding(oldNodeKey, objectId, false);
            }

            if (!bindings.TryGetValue(nodeKey, out Dictionary<int, GameObject> nodeBindings))
            {
                nodeBindings = new();
                bindings.Add(nodeKey, nodeBindings);
            }

            nodeBindings[objectId] = redDotObject;
            objectBindingKeys[objectId] = nodeKey;
            SetGameObjectActive(redDotObject, node.IsActive);
        }

        public void BindRedDot(Enum redDotType, int instanceId, GameObject redDotObject)
        {
            BindRedDot(RedDotNodeKey.Create(redDotType, instanceId), redDotObject);
        }

        public void BindSingletonRedDot(Enum redDotType, GameObject redDotObject)
        {
            BindRedDot(RedDotNodeKey.Singleton(redDotType), redDotObject);
        }

        /// <summary>
        /// 解除指定红点实例与 GameObject 的绑定，并隐藏该红点对象。
        /// </summary>
        public bool UnbindRedDot(RedDotNodeKey nodeKey, GameObject redDotObject)
        {
            EnsureInitialized();
            if (redDotObject == null)
            {
                return false;
            }

            int objectId = redDotObject.GetInstanceID();
            if (!objectBindingKeys.TryGetValue(objectId, out RedDotNodeKey boundNodeKey) || boundNodeKey != nodeKey)
            {
                return false;
            }

            return RemoveBinding(nodeKey, objectId, true);
        }

        /// <summary>
        /// 解除 GameObject 当前已有的红点绑定，适用于列表项复用。
        /// </summary>
        public bool UnbindRedDot(GameObject redDotObject)
        {
            EnsureInitialized();
            if (redDotObject == null)
            {
                return false;
            }

            int objectId = redDotObject.GetInstanceID();
            return objectBindingKeys.TryGetValue(objectId, out RedDotNodeKey nodeKey) && RemoveBinding(nodeKey, objectId, true);
        }

        /// <summary>
        /// 重新执行指定红点实例的类型自检。实例尚不存在时会自动创建。
        /// </summary>
        public bool Refresh(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            RedDotNode node = EnsureNode(nodeKey);
            return RefreshNode(node);
        }

        public bool Refresh(Enum redDotType, int instanceId)
        {
            return Refresh(RedDotNodeKey.Create(redDotType, instanceId));
        }

        public bool RefreshSingleton(Enum redDotType)
        {
            return Refresh(RedDotNodeKey.Singleton(redDotType));
        }

        /// <summary>
        /// 刷新指定红点类型当前已经创建的全部单例和多实例节点。
        /// </summary>
        public void RefreshAll(Enum redDotType)
        {
            EnsureInitialized();
            ValidateRegisteredType(redDotType);
            List<RedDotNodeKey> nodeKeys = new(typeIndex[redDotType]);
            foreach (RedDotNodeKey nodeKey in nodeKeys)
            {
                if (nodes.TryGetValue(nodeKey, out RedDotNode node))
                {
                    RefreshNode(node);
                }
            }
        }

        /// <summary>
        /// 根据最新业务 ID 集合同步指定类型的全部多实例节点。
        /// 新 ID 会创建并自检，已有 ID 会重新自检，不再存在的 ID 会被移除。
        /// </summary>
        public void SynchronizeInstances(Enum redDotType, IEnumerable<int> instanceIds)
        {
            EnsureInitialized();
            ValidateRegisteredType(redDotType);
            if (instanceIds == null)
            {
                throw new ArgumentNullException(nameof(instanceIds));
            }

            HashSet<RedDotNodeKey> desiredKeys = new();
            foreach (int instanceId in instanceIds)
            {
                RedDotNodeKey nodeKey = RedDotNodeKey.Create(redDotType, instanceId);
                if (!desiredKeys.Add(nodeKey))
                {
                    continue;
                }

                if (nodes.TryGetValue(nodeKey, out RedDotNode node))
                {
                    RefreshNode(node);
                }
                else
                {
                    EnsureNode(nodeKey);
                }
            }

            List<RedDotNodeKey> existingKeys = new(typeIndex[redDotType]);
            foreach (RedDotNodeKey existingKey in existingKeys)
            {
                if (existingKey.HasInstanceId && !desiredKeys.Contains(existingKey))
                {
                    RemoveNode(existingKey);
                }
            }
        }

        /// <summary>
        /// 直接设置指定节点的自身红点数值，不执行类型自检。
        /// </summary>
        public bool SetCount(RedDotNodeKey nodeKey, int count)
        {
            EnsureInitialized();
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "红点数值不能小于 0。");
            }

            RedDotNode node = EnsureNode(nodeKey);
            return SetSelfValue(node, count);
        }

        public bool SetCount(Enum redDotType, int instanceId, int count)
        {
            return SetCount(RedDotNodeKey.Create(redDotType, instanceId), count);
        }

        public bool SetSingletonCount(Enum redDotType, int count)
        {
            return SetCount(RedDotNodeKey.Singleton(redDotType), count);
        }

        public bool SetActive(RedDotNodeKey nodeKey, bool isActive)
        {
            return SetCount(nodeKey, isActive ? 1 : 0);
        }

        public bool SetActive(Enum redDotType, int instanceId, bool isActive)
        {
            return SetActive(RedDotNodeKey.Create(redDotType, instanceId), isActive);
        }

        public bool SetSingletonActive(Enum redDotType, bool isActive)
        {
            return SetActive(RedDotNodeKey.Singleton(redDotType), isActive);
        }

        public bool TryGetCount(RedDotNodeKey nodeKey, out int count)
        {
            EnsureInitialized();
            if (nodes.TryGetValue(nodeKey, out RedDotNode node))
            {
                count = node.TotalValue;
                return true;
            }

            count = default;
            return false;
        }

        public int GetCount(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            if (nodes.TryGetValue(nodeKey, out RedDotNode node))
            {
                return node.TotalValue;
            }

            throw new KeyNotFoundException($"红点节点“{nodeKey}”尚未创建。");
        }

        public bool IsActive(RedDotNodeKey nodeKey)
        {
            return GetCount(nodeKey) > 0;
        }

        /// <summary>
        /// 移除一个没有子节点的动态红点实例，并撤销它对全部父节点的贡献。
        /// </summary>
        public bool RemoveNode(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            if (!nodes.TryGetValue(nodeKey, out RedDotNode node))
            {
                return false;
            }

            if (node.ChildValues.Count > 0)
            {
                throw new InvalidOperationException($"红点节点“{nodeKey}”仍有 {node.ChildValues.Count} 个子节点，不能直接移除。");
            }

            ClearBindings(nodeKey, true);
            nodes.Remove(nodeKey);
            typeIndex[nodeKey.RedDotType].Remove(nodeKey);

            List<RedDotNodeKey> parentKeys = new(node.ParentKeys);
            foreach (RedDotNodeKey parentKey in parentKeys)
            {
                if (nodes.TryGetValue(parentKey, out RedDotNode parentNode) && parentNode.ChildValues.Remove(nodeKey))
                {
                    RecalculateAndPropagate(parentNode);
                }
            }

            return true;
        }

        protected override void OnRelease()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            foreach (BaseRedDot rule in rules.Values)
            {
                try
                {
                    rule.Release();
                }
                catch (Exception exception)
                {
                    Logger.Instance.LogError($"释放红点规则“{rule.GetType().Name}”失败：{exception.Message}");
                }
            }

            List<RedDotNodeKey> bindingKeys = new(bindings.Keys);
            foreach (RedDotNodeKey bindingKey in bindingKeys)
            {
                ClearBindings(bindingKey, true);
            }

            rules.Clear();
            nodes.Clear();
            typeIndex.Clear();
            bindings.Clear();
            objectBindingKeys.Clear();
            nodesBeingCreated.Clear();
            isInitialized = false;
        }

        private RedDotNode EnsureNode(RedDotNodeKey nodeKey)
        {
            ValidateNodeKey(nodeKey);
            if (nodes.TryGetValue(nodeKey, out RedDotNode existingNode))
            {
                return existingNode;
            }

            if (!rules.TryGetValue(nodeKey.RedDotType, out BaseRedDot rule))
            {
                throw new KeyNotFoundException($"红点类型“{FormatRedDotType(nodeKey.RedDotType)}”尚未注册规则。");
            }

            if (!nodesBeingCreated.Add(nodeKey))
            {
                throw new InvalidOperationException($"创建红点节点“{nodeKey}”时检测到循环父子关系。");
            }

            try
            {
                IReadOnlyList<RedDotNodeKey> resolvedParentKeys = rule.ResolveParentKeys(nodeKey);
                List<RedDotNode> parentNodes = new();
                HashSet<RedDotNodeKey> uniqueParentKeys = new();

                foreach (RedDotNodeKey parentKey in resolvedParentKeys)
                {
                    ValidateNodeKey(parentKey);
                    if (parentKey == nodeKey)
                    {
                        throw new InvalidOperationException($"红点节点“{nodeKey}”不能将自身定义为父节点。");
                    }

                    if (uniqueParentKeys.Add(parentKey))
                    {
                        parentNodes.Add(EnsureNode(parentKey));
                    }
                }

                RedDotNode node = new(nodeKey, rule);
                foreach (RedDotNode parentNode in parentNodes)
                {
                    node.ParentKeys.Add(parentNode.Key);
                }

                nodes.Add(nodeKey, node);
                typeIndex[nodeKey.RedDotType].Add(nodeKey);

                foreach (RedDotNode parentNode in parentNodes)
                {
                    parentNode.ChildValues.Add(nodeKey, 0);
                }

                try
                {
                    RefreshNode(node);
                }
                catch
                {
                    foreach (RedDotNode parentNode in parentNodes)
                    {
                        parentNode.ChildValues.Remove(nodeKey);
                    }

                    nodes.Remove(nodeKey);
                    typeIndex[nodeKey.RedDotType].Remove(nodeKey);
                    throw;
                }

                return node;
            }
            finally
            {
                nodesBeingCreated.Remove(nodeKey);
            }
        }

        private bool RefreshNode(RedDotNode node)
        {
            int checkedValue = node.Rule.Check(node.Key);
            return SetSelfValue(node, checkedValue);
        }

        private bool SetSelfValue(RedDotNode node, int value)
        {
            if (node.SelfValue == value)
            {
                return false;
            }

            node.SelfValue = value;
            return RecalculateAndPropagate(node);
        }

        private bool RecalculateAndPropagate(RedDotNode node)
        {
            int oldTotalValue = node.TotalValue;
            int newTotalValue = node.CalculateTotalValue();
            if (oldTotalValue == newTotalValue)
            {
                return false;
            }

            node.TotalValue = newTotalValue;
            ApplyBindings(node.Key, node.IsActive);

            List<RedDotNodeKey> parentKeys = new(node.ParentKeys);
            foreach (RedDotNodeKey parentKey in parentKeys)
            {
                if (!nodes.TryGetValue(parentKey, out RedDotNode parentNode))
                {
                    Logger.Instance.LogError($"红点节点“{node.Key}”引用的父节点“{parentKey}”不存在。");
                    continue;
                }

                parentNode.ChildValues[node.Key] = newTotalValue;
                RecalculateAndPropagate(parentNode);
            }

            return true;
        }

        private void ApplyBindings(RedDotNodeKey nodeKey, bool isActive)
        {
            if (!bindings.TryGetValue(nodeKey, out Dictionary<int, GameObject> nodeBindings))
            {
                return;
            }

            List<int> invalidObjectIds = null;
            foreach (KeyValuePair<int, GameObject> binding in nodeBindings)
            {
                if (binding.Value == null)
                {
                    invalidObjectIds ??= new List<int>();
                    invalidObjectIds.Add(binding.Key);
                    continue;
                }

                SetGameObjectActive(binding.Value, isActive);
            }

            if (invalidObjectIds == null)
            {
                return;
            }

            foreach (int objectId in invalidObjectIds)
            {
                RemoveBinding(nodeKey, objectId, false);
            }
        }

        private bool RemoveBinding(RedDotNodeKey nodeKey, int objectId, bool hideObject)
        {
            if (!bindings.TryGetValue(nodeKey, out Dictionary<int, GameObject> nodeBindings) || !nodeBindings.TryGetValue(objectId, out GameObject redDotObject))
            {
                return false;
            }

            nodeBindings.Remove(objectId);
            if (nodeBindings.Count == 0)
            {
                bindings.Remove(nodeKey);
            }

            if (objectBindingKeys.TryGetValue(objectId, out RedDotNodeKey boundNodeKey) && boundNodeKey == nodeKey)
            {
                objectBindingKeys.Remove(objectId);
            }

            if (hideObject && redDotObject != null)
            {
                SetGameObjectActive(redDotObject, false);
            }

            return true;
        }

        private void ClearBindings(RedDotNodeKey nodeKey, bool hideObjects)
        {
            if (!bindings.TryGetValue(nodeKey, out Dictionary<int, GameObject> nodeBindings))
            {
                return;
            }

            List<int> objectIds = new(nodeBindings.Keys);
            foreach (int objectId in objectIds)
            {
                RemoveBinding(nodeKey, objectId, hideObjects);
            }
        }

        private void OnSceneUnloaded(Scene unloadedScene)
        {
            List<RedDotNodeKey> nodeKeys = new(bindings.Keys);
            foreach (RedDotNodeKey nodeKey in nodeKeys)
            {
                if (!bindings.TryGetValue(nodeKey, out Dictionary<int, GameObject> nodeBindings))
                {
                    continue;
                }

                List<int> removedObjectIds = null;
                foreach (KeyValuePair<int, GameObject> binding in nodeBindings)
                {
                    if (binding.Value == null || binding.Value.scene == unloadedScene)
                    {
                        removedObjectIds ??= new List<int>();
                        removedObjectIds.Add(binding.Key);
                    }
                }

                if (removedObjectIds == null)
                {
                    continue;
                }

                foreach (int objectId in removedObjectIds)
                {
                    RemoveBinding(nodeKey, objectId, false);
                }
            }
        }

        private static void SetGameObjectActive(GameObject redDotObject, bool isActive)
        {
            if (redDotObject != null && redDotObject.activeSelf != isActive)
            {
                redDotObject.SetActive(isActive);
            }
        }

        private static void ValidateNodeKey(RedDotNodeKey nodeKey)
        {
            if (!nodeKey.IsValid)
            {
                throw new ArgumentException("红点节点 Key 无效。", nameof(nodeKey));
            }
        }

        private static void ValidateRedDotType(Enum redDotType)
        {
            if (redDotType == null)
            {
                throw new ArgumentNullException(nameof(redDotType));
            }
        }

        private void ValidateRegisteredType(Enum redDotType)
        {
            ValidateRedDotType(redDotType);
            if (!rules.ContainsKey(redDotType))
            {
                throw new KeyNotFoundException($"红点类型“{FormatRedDotType(redDotType)}”尚未注册规则。");
            }
        }

        private static string FormatRedDotType(Enum redDotType)
        {
            return redDotType == null ? "null" : $"{redDotType.GetType().Name}.{redDotType}";
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                const string message = "RedDotMgr 尚未初始化，请在项目启动流程中调用 RedDotMgr.Instance.OnInit()。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }
        }
    }
}
