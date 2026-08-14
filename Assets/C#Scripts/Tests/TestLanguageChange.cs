using System;
using System.Collections.Generic;
using GameFramework.EventSystem;
using GameFramework.LocalizationSystem;
using GameFramework.LocalizationSystem.Generated;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public sealed class TestLanguageChange : MonoBehaviour
{
    private readonly List<LocalizationLanguage> languages = new();
    private TMP_Dropdown languageDropdown;

    private void Awake()
    {
        languageDropdown = GetComponent<TMP_Dropdown>();
        LocalizationMgr.Instance.OnInit();
        BuildLanguageOptions();
        SyncSelectedLanguage();
    }

    private void OnEnable()
    {
        languageDropdown.onValueChanged.AddListener(OnLanguageSelected);
        EventCenter.Instance.AddEventListener(E_GlobalEventName.OnLanguageChanged, SyncSelectedLanguage);
    }

    private void OnDisable()
    {
        if (languageDropdown != null)
        {
            languageDropdown.onValueChanged.RemoveListener(OnLanguageSelected);
        }

        if (EventCenter.TryGetInstance(out EventCenter eventCenter))
        {
            eventCenter.RemoveEventListener(E_GlobalEventName.OnLanguageChanged, SyncSelectedLanguage);
        }
    }

    private void BuildLanguageOptions()
    {
        languages.Clear();
        List<TMP_Dropdown.OptionData> options = new();

        foreach (LocalizationLanguage language in Enum.GetValues(typeof(LocalizationLanguage)))
        {
            languages.Add(language);
            options.Add(new TMP_Dropdown.OptionData(language.ToLanguageId()));
        }

        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(options);
    }

    private void OnLanguageSelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= languages.Count)
        {
            GameFramework.Logger.Instance.LogError($"语言下拉框索引 {optionIndex} 超出有效范围。", this);
            return;
        }

        LocalizationMgr.Instance.ChangeLanguage(languages[optionIndex]);
    }

    private void SyncSelectedLanguage()
    {
        LocalizationLanguage currentLanguage = LocalizationMgr.Instance.CurrentLanguage;
        int optionIndex = languages.IndexOf(currentLanguage);
        if (optionIndex < 0)
        {
            GameFramework.Logger.Instance.LogWarning($"当前语言“{currentLanguage}”不在语言下拉框选项中。", this);
            return;
        }

        languageDropdown.SetValueWithoutNotify(optionIndex);
        languageDropdown.RefreshShownValue();
    }
}
