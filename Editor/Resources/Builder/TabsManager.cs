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
        Build,
        Upload,
        Tools,
        Logs
    }

    ActiveTab activeTabName = ActiveTab.Build;
    float activeTabPosition = 0;
    private void SetupTabs()
    {

        var activeTab = rootVisualElement.Q<VisualElement>("ActiveTab");

        var tabSections = rootVisualElement.Q<VisualElement>("TabSections");

        var buildTab = rootVisualElement.Q<Label>("BuildTab");
        var uploadTab = rootVisualElement.Q<Label>("UploadTab");
        var toolsTab = rootVisualElement.Q<Label>("ToolsTab");
        var logsTab = rootVisualElement.Q<Label>("LogsTab");

        var buildTabSection = rootVisualElement.Q<VisualElement>("BuildSection");
        var uploadSection = rootVisualElement.Q<VisualElement>("UploadSection");
        var toolsSection = rootVisualElement.Q<VisualElement>("ToolsSection");
        var logsSection = rootVisualElement.Q<VisualElement>("LogsSection");

        buildTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Build;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        uploadTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Upload;
            SetActivePosition();
            activeTab.style.left = activeTabPosition;
            MoveTabSections(tabSections);
        });

        toolsTab.RegisterCallback<MouseUpEvent>((e) =>
        {
            activeTabName = ActiveTab.Tools;
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
            case ActiveTab.Build:
                tabSections.style.left = 0;
                break;
            case ActiveTab.Upload:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width;
                break;
            case ActiveTab.Tools:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width * 2;
                break;
            case ActiveTab.Logs:
                tabSections.style.left = -rootVisualElement.resolvedStyle.width * 3;
                break;
        }
    }

    void SetActivePosition()
    {
        switch (activeTabName)
        {
            case ActiveTab.Build:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - (rootVisualElement.resolvedStyle.width * 0.3375f) - 45;
                break;
            case ActiveTab.Upload:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 - (rootVisualElement.resolvedStyle.width * 0.1125f) - 45;
                break;
            case ActiveTab.Tools:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 + (rootVisualElement.resolvedStyle.width * 0.1125f) - 45;
                break;
            case ActiveTab.Logs:
                activeTabPosition = rootVisualElement.resolvedStyle.width / 2 + (rootVisualElement.resolvedStyle.width * 0.3375f) - 45;
                break;
        }
    }
}