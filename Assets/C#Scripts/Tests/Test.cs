using GameFramework.ConfigData.Generated;
using GameFramework.ConfigSystem;
using UnityEngine;

public sealed class Test : MonoBehaviour
{
    private enum TestEvent
    {
        AAA,
        BBBBBB
    }

    void Start()
    {
        ConfigMgr.Instance.OnInit();
        // 直接通过 行名 和 ID 读取
        if (ConfigMgr.Instance.TryGetDataById(1, out TestOneData row1))
        {
            int id = row1.ID;
            string testString = row1.TestString;
            GameFramework.Logger.Instance.LogInfo($"测试读取配置TestOneData Id: {id}， TestString: {testString}");
        }
        // 先读取整张表，再根据 ID 读取指定行
        if (ConfigMgr.Instance.TryGetTable(out TestOneDataSO table))
        {
            if (table.TryGetDataById(2, out TestOneData row2))
            {
                int id = row2.ID;
                string testString = row2.TestString;
                GameFramework.Logger.Instance.LogInfo($"测试读取配置TestOneData Id: {id}， TestString: {testString}");
            }
        }
        // EventCenter.Instance.AddEventListener(TestEvent.AAA, 无参事件);
        // EventCenter.Instance.AddEventListener<int, int>(TestEvent.AAA, 有参事件);
        // EventCenter.Instance.EventTrigger(TestEvent.AAA);
        // EventCenter.Instance.EventTrigger<int>(TestEvent.AAA, 100);

    }


    private void 无参事件()
    {
        Debug.Log("无参事件触发");
    }

    private void 有参事件(int param, int param2)
    {
        Debug.Log($"有参事件触发，参数：{@param}");
    }
}
