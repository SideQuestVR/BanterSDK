using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
namespace Banter{
    public class SDKManagerWindow : EditorWindow
    {
        public static UnityEvent OnCompileAll = new UnityEvent();
        public static UnityEvent OnClearAll = new UnityEvent();
        public static UnityEvent OnCompileInjection = new UnityEvent();
        public static UnityEvent OnCompileElectron = new UnityEvent();
        public static UnityEvent OnCompileAllComponents = new UnityEvent();

        [MenuItem("Banter/SDK Manager")]
        public static void ShowMainWindow() {
            SDKManagerWindow window = GetWindow<SDKManagerWindow>();
            window.minSize = new Vector2(450, 200);
            window.titleContent = new GUIContent("Banter SDK Manager");
        }

        public void OnEnable(){
            VisualElement content = Resources.Load<VisualTreeAsset>("SDKManager/SDKManagerWindow").CloneTree();
            content.style.height = new StyleLength(Length.Percent(100));
            rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("SDKManager/SDKManagerWindow"));
            rootVisualElement.Add(content);
#if BANTER_EDITOR
            rootVisualElement.Q<Button>("allAndInjection").clicked += () =>{
                OnCompileAll.Invoke();
                OnCompileInjection.Invoke();
                // SDKCodeGen.CompileAllComponents();
                // SDKCodeGen.CompileInjection();
            };
            rootVisualElement.Q<Button>("allOnly").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAllComponents();
            rootVisualElement.Q<Button>("clearAll").clicked += () => OnClearAll.Invoke();// SDKCodeGen.ClearAllComponents();
            rootVisualElement.Q<Button>("compileElectron").clicked += () => OnCompileElectron.Invoke();// SDKCodeGen.CompileElectron();
            rootVisualElement.Q<Button>("compileInjection").clicked += () => OnCompileInjection.Invoke();// SDKCodeGen.CompileInjection();
            rootVisualElement.Q<Button>("kitchenSink").clicked += () => OnCompileAll.Invoke();// SDKCodeGen.CompileAll();
            Remove(rootVisualElement.Q<Button>("setupVisualScripting"));
            Remove(rootVisualElement.Q<Button>("setupLayers"));
#else
            Remove(rootVisualElement.Q<Button>("allAndInjection"));
            Remove(rootVisualElement.Q<Button>("allOnly"));
            Remove(rootVisualElement.Q<Button>("clearAll"));
            Remove(rootVisualElement.Q<Button>("compileElectron"));
            Remove(rootVisualElement.Q<Button>("compileInjection"));
            Remove(rootVisualElement.Q<Button>("kitchenSink"));
            rootVisualElement.Q<Button>("setupVisualScripting").clicked += () => _ = InitialiseOnLoad.InstallVisualScripting();
            rootVisualElement.Q<Button>("setupLayers").clicked += () => InitialiseOnLoad.SetupLayers();
#endif
            rootVisualElement.Q<Button>("openDevTools").clicked += () => BanterStarterUpper.ToggleDevTools();
            
        }

        public void Remove(VisualElement element){
            element.parent.Remove(element);
        }
    }
}