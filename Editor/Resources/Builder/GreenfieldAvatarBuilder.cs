using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    /// <summary>
    /// Greenfield avatar builder — a standalone editor window that builds a single encrypted Basis
    /// <c>.BEE</c> avatar bundle from a Humanoid rig, using the one shared Greenfield key. Deliberately
    /// minimal vs the legacy <see cref="BuilderWindow"/>: no login, no CDN upload, no FlexaBody rig
    /// prep — a Humanoid rig binds straight through <c>HumanoidAvatarLoader</c> at load time, so the
    /// builder only needs to package it. You host the resulting <c>.BEE</c> yourself.
    ///
    /// Styling mirrors the scene builder (dark chrome, banner, drag-and-drop target) by reusing the
    /// SDK's shared <c>EditorComponents.uss</c> and resource images.
    /// </summary>
    public class GreenfieldAvatarBuilder : EditorWindow
    {
        // The one shared Greenfield bundle key, centralised in the SDK so build and runtime can't
        // drift. Seeds the editable key field below; a mismatch would fail decryption silently.
        private const string DefaultEncryptionKey = BS.GreenfieldBundleCrypto.Password;

        private const string OutputFolderPrefKey = "Greenfield_AvatarBuilder_OutputFolder";

        // Base component theming, then the builder's alt-* classes + AltspaceVR font. Both are what
        // the scene builder loads, so reusing them makes this window match it.
        private const string BaseStyleSheetPath = "Packages/com.sidequest.banter/Editor/Resources/UI/EditorComponents.uss";
        private const string BuilderStyleSheetPath = "Packages/com.sidequest.banter/Editor/Resources/Builder/BuilderWindow.uss";

        private GameObject _avatarGo;
        private string _avatarName = "";

        // UI
        private VisualElement _dropContainer;
        private VisualElement _infoCard;
        private Label _selectedLabel;
        private Label _validationLabel;
        private TextField _nameField;
        private Toggle _windowsToggle;
        private Toggle _androidToggle;
        private Label _outputPathLabel;
        private VisualElement _buildPill;
        private Label _statusBar;

        private string _outputFolder;
        private bool _isBuilding;

        [MenuItem("Greenfield/Avatar Builder")]
        public static void ShowWindow()
        {
            var window = GetWindow<GreenfieldAvatarBuilder>();
            window.titleContent = new GUIContent("Avatar Builder");
            window.minSize = new Vector2(360, 520);
        }

        private void CreateGUI()
        {
            _outputFolder = EditorPrefs.GetString(OutputFolderPrefKey, DefaultOutputFolder());

            VisualElement root = rootVisualElement;
            root.style.backgroundColor = new Color(26f / 255f, 26f / 255f, 26f / 255f);
            root.style.flexGrow = 1;

            foreach (string path in new[] { BaseStyleSheetPath, BuilderStyleSheetPath })
            {
                StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null)
                    root.styleSheets.Add(sheet);
            }

            root.Add(BuildBanner());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginBottom = 30; // leave room for the status bar
            root.Add(scroll);

            _dropContainer = BuildDropArea();
            _infoCard = BuildInfoCard();
            scroll.Add(_dropContainer);
            scroll.Add(_infoCard);

            _statusBar = new Label("STATUS: Idle");
            _statusBar.style.position = Position.Absolute;
            _statusBar.style.bottom = 0;
            _statusBar.style.width = Length.Percent(100);
            _statusBar.style.backgroundColor = new Color(51f / 255f, 51f / 255f, 51f / 255f);
            _statusBar.style.paddingLeft = 12;
            _statusBar.style.paddingTop = 5;
            _statusBar.style.paddingBottom = 5;
            _statusBar.style.fontSize = 12;
            root.Add(_statusBar);

            // Pick up a Humanoid avatar already selected in the hierarchy for convenience.
            if (Selection.activeGameObject != null)
                TrySetAvatar(Selection.activeGameObject, quiet: true);

            RefreshState();
        }

        // ---- UI construction -------------------------------------------------

        private VisualElement BuildBanner()
        {
            var banner = new VisualElement();
            banner.style.height = 60;
            banner.style.minHeight = 60;
            banner.style.maxHeight = 60;
            banner.style.flexDirection = FlexDirection.Row;
            banner.style.alignItems = Align.Center;

            var logo = MakeImage("UI/Images/Banter_Logo_No_BG_V2copy 1", ScaleMode.ScaleToFit);
            logo.style.marginLeft = 14;
            logo.style.width = 180;
            logo.style.height = 40;
            logo.style.flexGrow = 0;
            logo.style.flexShrink = 0;
            banner.Add(logo);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            banner.Add(spacer);

            var title = new Label("AVATAR BUILDER");
            title.style.marginRight = 20;
            title.style.fontSize = 12;
            title.style.letterSpacing = 1;
            title.style.color = new Color(1f, 1f, 1f, 0.5f);
            banner.Add(title);

            return banner;
        }

        private VisualElement BuildDropArea()
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;

            var icon = MakeImage("UI/Images/CapsuleMan_2D", ScaleMode.ScaleToFit);
            icon.style.position = Position.Absolute;
            icon.style.width = 93;
            icon.style.height = 93;
            icon.style.left = Length.Percent(50);
            icon.style.marginLeft = -46.5f;
            container.Add(icon);

            var dropArea = new VisualElement { name = "dropAvatarArea" };
            dropArea.tooltip = "Drop a Humanoid avatar GameObject from the hierarchy here.";
            dropArea.style.marginTop = 60;
            dropArea.style.marginLeft = Length.Percent(5);
            dropArea.style.marginRight = Length.Percent(5);
            dropArea.style.marginBottom = 20;
            dropArea.style.paddingTop = 45;
            dropArea.style.paddingBottom = 45;
            SetBackgroundImage(dropArea, "UI/Images/Frame 3");
            dropArea.style.unitySliceLeft = 48;
            dropArea.style.unitySliceTop = 48;
            dropArea.style.unitySliceRight = 48;
            dropArea.style.unitySliceBottom = 48;
            dropArea.style.unitySliceScale = 0.5f;

            var label = new Label("Drop a Humanoid avatar GameObject\nfrom the hierarchy to start.");
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = Color.white;
            label.style.fontSize = 14;
            dropArea.Add(label);

            RegisterDropTarget(dropArea);
            container.Add(dropArea);
            return container;
        }

        private VisualElement BuildInfoCard()
        {
            var card = new VisualElement();
            card.style.paddingLeft = 30;
            card.style.paddingRight = 30;
            card.style.paddingTop = 8;

            // Title row + reset
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.style.alignItems = Align.Center;
            var title = new Label("Avatar Build");
            title.style.fontSize = 18;
            titleRow.Add(title);
            var reset = MakeIconButton("RESET", "UI/Images/icon-refresh", () =>
            {
                // Clear the name too, so the next dropped prefab re-seeds it (TrySetAvatar only fills a blank name).
                _avatarGo = null;
                _avatarName = "";
                if (_nameField != null) _nameField.value = "";
                RefreshState();
                SetStatus("Idle");
            });
            titleRow.Add(reset);
            card.Add(titleRow);

            _selectedLabel = new Label("No avatar selected");
            _selectedLabel.AddToClassList("alt-muted");
            _selectedLabel.style.marginTop = 4;
            _selectedLabel.style.marginBottom = 2;
            card.Add(_selectedLabel);

            _validationLabel = new Label("");
            _validationLabel.AddToClassList("alt-negative");
            _validationLabel.style.whiteSpace = WhiteSpace.Normal;
            _validationLabel.style.fontSize = 12;
            _validationLabel.style.marginBottom = 8;
            card.Add(_validationLabel);

            // Bundle name
            card.Add(MakeSectionLabel("Bundle Name"));
            _nameField = new TextField { value = _avatarName };
            _nameField.RegisterValueChangedCallback(e => { _avatarName = e.newValue; RefreshBuildEnabled(); });
            card.Add(_nameField);

            // Build targets
            card.Add(MakeSectionLabel("Build Targets"));
            _windowsToggle = new Toggle("Windows (Standalone)") { value = true };
            _windowsToggle.AddToClassList("alt-toggle");
            _androidToggle = new Toggle("Android") { value = true };
            _androidToggle.AddToClassList("alt-toggle");
            _windowsToggle.RegisterValueChangedCallback(_ => RefreshBuildEnabled());
            _androidToggle.RegisterValueChangedCallback(_ => RefreshBuildEnabled());
            card.Add(_windowsToggle);
            card.Add(_androidToggle);

            // Output folder
            card.Add(MakeSectionLabel("Output Folder"));
            var outRow = new VisualElement();
            outRow.style.flexDirection = FlexDirection.Row;
            outRow.style.alignItems = Align.Center;
            _outputPathLabel = new Label(_outputFolder);
            _outputPathLabel.AddToClassList("alt-muted");
            _outputPathLabel.style.flexGrow = 1;
            _outputPathLabel.style.fontSize = 12;
            _outputPathLabel.style.whiteSpace = WhiteSpace.Normal;
            outRow.Add(_outputPathLabel);
            outRow.Add(MakeIconButton("BROWSE", "UI/Images/icon-folder", ChooseOutputFolder));
            card.Add(outRow);

            // Build button (pill)
            _buildPill = new VisualElement();
            _buildPill.AddToClassList("alt-pill");
            _buildPill.style.width = 215;
            _buildPill.style.height = 40;
            _buildPill.style.marginTop = 24;
            _buildPill.style.marginBottom = 20;
            var buildLabel = new Label("BUILD .BEE");
            buildLabel.AddToClassList("alt-pill-label");
            SetBackgroundImage(buildLabel, "UI/Images/icon-build");
            _buildPill.Add(buildLabel);
            _buildPill.AddManipulator(new Clickable(() => { _ = BuildAsync(); }));
            card.Add(_buildPill);

            return card;
        }

        // ---- Behaviour -------------------------------------------------------

        private void RegisterDropTarget(VisualElement dropArea)
        {
            dropArea.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                bool ok = DragAndDrop.objectReferences.OfType<GameObject>().Any();
                DragAndDrop.visualMode = ok ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            });
            dropArea.RegisterCallback<DragPerformEvent>(_ =>
            {
                DragAndDrop.AcceptDrag();
                var go = DragAndDrop.objectReferences.OfType<GameObject>().FirstOrDefault();
                if (go != null)
                    TrySetAvatar(go, quiet: false);
            });
        }

        private void TrySetAvatar(GameObject go, bool quiet)
        {
            if (go != null && !go.scene.IsValid())
            {
                // A project prefab, not a scene instance. Adding build components would mutate the
                // asset, so ask for a hierarchy instance instead.
                if (!quiet)
                    SetStatus("Drag the avatar from the Hierarchy (scene instance), not a prefab asset.");
                return;
            }

            _avatarGo = go;
            if (go != null && string.IsNullOrWhiteSpace(_avatarName))
            {
                _avatarName = go.name;
                if (_nameField != null) _nameField.value = _avatarName;
            }
            RefreshState();
        }

        private void RefreshState()
        {
            bool hasAvatar = _avatarGo != null;

            // Drop target first; once something is dropped, swap it for the build form (as the scene
            // builder does). RESET clears the selection and brings the drop target back.
            if (_dropContainer != null)
                _dropContainer.style.display = hasAvatar ? DisplayStyle.None : DisplayStyle.Flex;
            if (_infoCard != null)
                _infoCard.style.display = hasAvatar ? DisplayStyle.Flex : DisplayStyle.None;

            _selectedLabel.text = hasAvatar ? $"Selected: {_avatarGo.name}" : "No avatar selected";

            string reason = null;
            bool humanoid = hasAvatar && IsHumanoid(_avatarGo, out reason);
            _validationLabel.text = (hasAvatar && !humanoid) ? reason : "";
            _validationLabel.style.display = string.IsNullOrEmpty(_validationLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshBuildEnabled();
        }

        private void RefreshBuildEnabled()
        {
            bool ready = _avatarGo != null
                         && IsHumanoid(_avatarGo, out _)
                         && !string.IsNullOrWhiteSpace(_avatarName)
                         && (_windowsToggle == null || _windowsToggle.value || _androidToggle.value)
                         && !_isBuilding;

            if (_buildPill != null)
            {
                _buildPill.SetEnabled(ready);
                _buildPill.style.opacity = ready ? 1f : 0.4f;
            }
        }

        /// <summary>A rig binds through the Humanoid loader only if imported as Humanoid (not Generic).</summary>
        private static bool IsHumanoid(GameObject go, out string reason)
        {
            Animator animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                reason = "No Animator found. The avatar must be an imported Humanoid rig.";
                return false;
            }
            if (animator.avatar == null || !animator.avatar.isValid)
            {
                reason = "The Animator has no valid Avatar. Re-import the model with a Humanoid rig.";
                return false;
            }
            if (!animator.avatar.isHuman)
            {
                reason = "Rig is Generic. Re-import the model with Rig → Animation Type = Humanoid.";
                return false;
            }
            reason = null;
            return true;
        }

        private void ChooseOutputFolder()
        {
            string start = Directory.Exists(_outputFolder) ? _outputFolder : DefaultOutputFolder();
            string chosen = EditorUtility.OpenFolderPanel("Choose .BEE output folder", start, "");
            if (string.IsNullOrEmpty(chosen))
                return;
            _outputFolder = chosen;
            EditorPrefs.SetString(OutputFolderPrefKey, _outputFolder);
            _outputPathLabel.text = _outputFolder;
        }

        private async Task BuildAsync()
        {
            if (_isBuilding)
                return;

            string why = null;
            if (_avatarGo == null || !IsHumanoid(_avatarGo, out why))
            {
                SetStatus(why ?? "Select a Humanoid avatar first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_avatarName))
            {
                SetStatus("Enter a bundle name.");
                return;
            }

            var targets = new List<BuildTarget>();
            if (_windowsToggle.value) targets.Add(BuildTarget.StandaloneWindows64);
            if (_androidToggle.value) targets.Add(BuildTarget.Android);
            if (targets.Count == 0)
            {
                SetStatus("Select at least one build target.");
                return;
            }

            if (string.IsNullOrEmpty(_outputFolder))
            {
                SetStatus("Choose an output folder.");
                return;
            }
            Directory.CreateDirectory(_outputFolder);

            // The bundle key is the single shared Greenfield one now (centralised in GreenfieldBundleCrypto);
            // there's no longer a per-build override field.
            string key = DefaultEncryptionKey;

            // BasisProp is Greenfield's content component (no far-LOD / no glTF fallback path).
            BasisProp prop = _avatarGo.GetComponent<BasisProp>();
            if (prop == null)
                prop = Undo.AddComponent<BasisProp>(_avatarGo);
            prop.BasisBundleDescription = new BasisBundleDescription
            {
                AssetBundleName = _avatarName,
                AssetBundleDescription = _avatarName,
            };

            // The build reads its output directory from the shared settings asset. Override it in
            // memory for the duration of this build, then restore — the scene builder shares this asset.
            BasisAssetBundleObject settings =
                AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
            if (settings == null)
            {
                SetStatus("Basis build settings asset not found. Is Basis installed?");
                return;
            }
            string prevDir = settings.AssetBundleDirectory;
            bool prevOpen = settings.OpenFolderOnDisc;

            _isBuilding = true;
            RefreshBuildEnabled();
            SetStatus($"Building '{_avatarName}' for {string.Join(", ", targets)}…");

            bool ok = false;
            string message;
            try
            {
                settings.AssetBundleDirectory = _outputFolder;
                settings.OpenFolderOnDisc = false; // we reveal the exact bundle folder ourselves below

                (ok, message) = await BasisBundleBuild.GameObjectBundleBuild(
                    Image: "",
                    BasisContentBase: prop,
                    Targets: targets,
                    useProvidedPassword: true,
                    OverriddenPassword: key);
            }
            catch (System.Exception e)
            {
                ok = false;
                message = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                settings.AssetBundleDirectory = prevDir;
                settings.OpenFolderOnDisc = prevOpen;
                _isBuilding = false;
                RefreshBuildEnabled();
            }

            if (ok)
            {
                string bundleFolder = Path.Combine(_outputFolder, BasisBundleBuild.MakeSafeFolderName(_avatarName));
                CleanPasswordSidecar(bundleFolder, settings.ProtectedPasswordFileName);
                SetStatus($"Built '{_avatarName}'.");
                if (Directory.Exists(bundleFolder))
                    EditorUtility.RevealInFinder(bundleFolder);
            }
            else
            {
                SetStatus($"Build failed: {message}");
            }
        }

        // Drop the plaintext password sidecar the pipeline writes next to the .BEE — the key is the
        // shared Greenfield one and only the .BEE gets hosted.
        private static void CleanPasswordSidecar(string bundleFolder, string passwordFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(passwordFileName) || !Directory.Exists(bundleFolder))
                    return;
                foreach (string f in Directory.GetFiles(bundleFolder, passwordFileName + "*.txt", SearchOption.TopDirectoryOnly))
                    File.Delete(f);
            }
            catch { /* tidy-up only; never fail the build over it */ }
        }

        // ---- helpers ---------------------------------------------------------

        private static string DefaultOutputFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "AvatarBundles");
        }

        private void SetStatus(string message)
        {
            if (_statusBar != null)
                _statusBar.text = "STATUS: " + message;
        }

        private static Label MakeSectionLabel(string text)
        {
            var label = new Label(text);
            label.style.marginTop = 12;
            label.style.marginBottom = 2;
            label.style.fontSize = 12;
            return label;
        }

        private static Button MakeIconButton(string text, string iconResource, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("alt-btn-icon");
            SetBackgroundImage(button, iconResource);
            return button;
        }

        private static void SetBackgroundImage(VisualElement element, string resourcePath)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex != null)
                element.style.backgroundImage = new StyleBackground(tex);
        }

        // Prefer an Image element over a background image where the texture must scale to fit — its
        // scaleMode is stable, unlike IStyle.unityBackgroundScaleMode (deprecated in Unity 6).
        private static Image MakeImage(string resourcePath, ScaleMode scaleMode)
        {
            return new Image { image = Resources.Load<Texture2D>(resourcePath), scaleMode = scaleMode };
        }
    }
}
