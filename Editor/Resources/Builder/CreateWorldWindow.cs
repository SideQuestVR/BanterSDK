using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    /// <summary>
    /// Tiny modal for naming a new world. It only collects the name and hands it back to the caller (the
    /// builder), which calls the create-world API, refreshes the world list and selects the new world — so
    /// this window owns no API/state itself. Enter submits, Escape cancels.
    /// </summary>
    public class CreateWorldWindow : EditorWindow
    {
        private Action<string> _onSubmit;
        private TextField _nameField;

        /// <summary>Opens the modal. <paramref name="onSubmit"/> is invoked with the trimmed name on Create.</summary>
        public static void Open(Action<string> onSubmit)
        {
            var window = CreateInstance<CreateWorldWindow>();
            window.titleContent = new GUIContent("Create World");
            window._onSubmit = onSubmit;
            window.minSize = new Vector2(340, 128);
            window.maxSize = new Vector2(520, 128);
            // A utility window floats above the builder without the modal input-lock, which can be flaky
            // in the editor; the builder disables its own controls while the create call runs.
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 12;

            var title = new Label("New world name");
            title.style.marginBottom = 4;
            title.style.fontSize = 12;
            root.Add(title);

            _nameField = new TextField();
            _nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.StopPropagation();
                }
            });
            root.Add(_nameField);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 12;

            var cancel = new Button(Close) { text = "Cancel" };
            var create = new Button(Submit) { text = "Create" };
            buttons.Add(cancel);
            buttons.Add(create);
            root.Add(buttons);

            // Focus the field once the panel is attached so the user can type immediately.
            _nameField.schedule.Execute(() => _nameField.Focus());
        }

        private void Submit()
        {
            string name = _nameField?.value?.Trim();
            if (string.IsNullOrEmpty(name))
                return;
            var callback = _onSubmit;
            _onSubmit = null; // guard against a double-fire (Enter + button)
            Close();
            callback?.Invoke(name);
        }
    }
}
