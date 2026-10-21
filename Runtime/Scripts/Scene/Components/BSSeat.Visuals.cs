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

        public bool ShowRuntimeVisual { get => showRuntimeVisual; set => showRuntimeVisual = value; }

        readonly Dictionary<int, PointerEventData> pointers = new Dictionary<int, PointerEventData>();
        static Mesh markerMesh;
        static Material markerMaterial;
        static readonly int HoverProperty = Shader.PropertyToID("_Hover");
        static readonly Matrix4x4 MarkerOffset = Matrix4x4.Translate(Vector3.up * .015f);
        MaterialPropertyBlock markerProperties;
        float hoverPress;

        public bool IsHovered
        {
            get
            {
                foreach (var pointer in pointers.Values)
                    if (PointerIsInRange(pointer)) return true;
                return false;
            }
        }
        public bool IsVisible => showRuntimeVisual && isActiveAndEnabled && IsHovered && !seated;

        void OnDisable()
        {
            pointers.Clear();
            hoverPress = 0f;
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
            if (!IsVisible) return;
            hoverPress = Mathf.MoveTowards(hoverPress, 1f, Time.unscaledDeltaTime * 4f);
            if (markerProperties == null) markerProperties = new MaterialPropertyBlock();
            markerProperties.SetFloat(HoverProperty, hoverPress);
            DrawMarker(transform, null, markerProperties);
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
