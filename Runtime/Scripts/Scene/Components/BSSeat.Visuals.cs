using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace BS
{
    public partial class BSSeat : IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField, Tooltip("Show the hover marker and seat hint during play. Editor placement previews remain visible.")]
        internal bool showRuntimeVisual = true;

        [SerializeField, Tooltip("Show the posed Robot pelvis and legs when targeting this empty seat. Editor pose previews remain visible.")]
        internal bool showLegHologram;

        public bool ShowRuntimeVisual { get => showRuntimeVisual; set => showRuntimeVisual = value; }
        public bool ShowLegHologram { get => showLegHologram; set { showLegHologram = value; if (!value) ClearLegPreview(); } }

        readonly Dictionary<int, PointerEventData> pointers = new Dictionary<int, PointerEventData>();
        static Mesh markerMesh;
        static Material markerMaterial;
        static readonly int HoverProperty = Shader.PropertyToID("_Hover");
        static readonly Matrix4x4 MarkerOffset = Matrix4x4.Translate(Vector3.up * .015f);
        MaterialPropertyBlock markerProperties;
        float hoverPress;
        SeatPosePreview legPreview;

        public bool IsHovered
        {
            get
            {
                foreach (var pointer in pointers.Values)
                    if (PointerIsInRange(pointer)) return true;
                return false;
            }
        }
        bool CanShowHolograms => isActiveAndEnabled && IsHovered && !seated && !IsOccupiedByOther;
        public bool IsVisible => showRuntimeVisual && CanShowHolograms;
        public bool IsLegHologramVisible => showLegHologram && CanShowHolograms;

        void OnDisable()
        {
            pointers.Clear();
            hoverPress = 0f;
            ClearLegPreview();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData.pointerEnter != null && eventData.pointerEnter.GetComponentInParent<BSSeat>() != this) return;
            if (!isActiveAndEnabled || !PointerIsInRange(eventData))
            {
                pointers.Remove(eventData.pointerId);
                return;
            }
            if (pointers.Count == 0) hoverPress = 0f;
            pointers[eventData.pointerId] = eventData;
        }

        bool PointerIsInRange(PointerEventData pointer)
        {
            var hit = pointer.pointerCurrentRaycast;
            var target = hit.gameObject != null ? hit.gameObject : pointer.pointerEnter;
            if (target == null) target = gameObject;
            var point = hit.gameObject != null ? hit.worldPosition : target.transform.position;
            return target.GetComponentInParent<BSSeat>() == this && BSPointerRange.IsInRange(target, point);
        }

        public void OnPointerExit(PointerEventData eventData) => pointers.Remove(eventData.pointerId);

        static void LoadMarker()
        {
            if (markerMesh == null)
            {
                var model = Resources.Load<GameObject>("CreatorConveniences/Models/Seat");
                var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
                markerMesh = filter != null ? filter.sharedMesh : null;
            }
            if (markerMaterial == null)
                markerMaterial = Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker");
        }

        void Update()
        {
            var showLegs = IsLegHologramVisible;
            if (!showLegs) ClearLegPreview();
            if (!IsVisible && !showLegs) return;
            hoverPress = Mathf.MoveTowards(hoverPress, 1f, Time.unscaledDeltaTime * 4f);
            if (IsVisible)
            {
                if (markerProperties == null) markerProperties = new MaterialPropertyBlock();
                markerProperties.SetFloat(HoverProperty, hoverPress);
                DrawMarker(transform, null, markerProperties);
            }
            if (showLegs)
            {
                if (legPreview == null) legPreview = new SeatPosePreview();
                legPreview.Draw(transform, Pose, false, null, hoverPress);
            }
        }

        void ClearLegPreview()
        {
            legPreview?.Dispose();
            legPreview = null;
        }

        static void DrawMarker(Transform anchor, Camera camera, MaterialPropertyBlock properties)
        {
            LoadMarker();
            if (markerMesh == null || markerMaterial == null) return;
            var parameters = new RenderParams(markerMaterial)
            {
                // UI/Menu render layers can draw through walls; use ordinary world depth for the marker.
                layer = 0,
                camera = camera,
                matProps = properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };
#if UNITY_EDITOR
            if (camera != null)
            {
                parameters.layer = anchor.gameObject.layer;
                parameters.instanceID = anchor.gameObject.GetInstanceID();
                parameters.overrideSceneCullingMask = true;
                parameters.sceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(anchor.gameObject.scene);
            }
#endif
            Graphics.RenderMesh(parameters, markerMesh, 0, anchor.localToWorldMatrix * MarkerOffset);
        }

#if UNITY_EDITOR
        public static void DrawPlacementPreview(Transform anchor, Camera camera) => DrawMarker(anchor, camera, null);
#endif
    }
}
