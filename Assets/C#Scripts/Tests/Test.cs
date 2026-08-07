using System;
using GameFramework.ConfigData.Generated;
using GameFramework.ConfigSystem;
using GameFramework.EventSystem;
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
        TestOneData config = ConfigMgr.Instance.GetConfig<TestOneData>(1);
        Debug.Log(config);
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
