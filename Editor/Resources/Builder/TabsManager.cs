using UnityEditor;
using UnityEngine.UIElements;

public class TabsManager
{
    VisualElement rootVisualElement;
    public TabsManager(VisualElement rootVisualElement)
    {
        this.rootVisualElement = rootVisualElement;
        SetupTabs();
    }
     public enum ActiveTab
    {
        Space,
        Avatar,
        Logs
    }

    ActiveTab activeTabName = ActiveTab.Space;
    float activeTabPosition = 0;
    private void SetupTabs()
    {

        var activeTab = rootVisualElement.Q<VisualElement>("ActiveTab");

        var tabSections = rootVisualElement.Q<VisualElement>("TabSections");

        var spaceTab = rootVisualElement.Q<Label>("SpaceTab");
        var avatarTab = rootVisualElement.Q<Label>("AvatarTab");
        var logsTab = rootVisualElement.Q<Label>("LogsTab");

        var spaceSection = rootVisualElement.Q<VisualElement>("SpaceSection");
        var avatarSection = rootVisualElement.Q<VisualElement>("AvatarSection");
        var logsSection = rootVisualElement.Q<VisualElement>("LogsSection");

        if (EditorPrefs.HasKey("BanterBuilder_BanterActiveTab"))
        {
            activeTabName = (ActiveTab)System.Enum.Parse(typeof(ActiveTab), EditorPrefs.GetString("BanterBuilder_BanterActiveTab"));
        }

        spaceTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Space;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        avatarTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Avatar;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        logsTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Logs;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        rootVisualElement.RegisterCallback<GeometryChangedEvent>((e) =>
        {
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });
    }

    void MoveTabSections(VisualElement tabSections)
    {

        switch (activeTabName)
        {
            case ActiveTab.Space:
                tabSections.style.left = 0;
                break;
            case ActiveTab.Avatar:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width;
                break;
            case ActiveTab.Logs:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width * 2;
                break;
        }
    }

    void SetActivePosition()
    {
        
        EditorPrefs.SetString("BanterBuilder_BanterActiveTab", activeTabName.ToString());
        switch (activeTabName)
        {
            case ActiveTab.Space:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - (rootVisualElement.resolvedStyle.width * 0.3f) - 45;
                break;
            case ActiveTab.Avatar:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - 45;
                break;
            case ActiveTab.Logs:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 + (rootVisualElement.resolvedStyle.width * 0.3f) - 45;
                break;
        }
    }
}