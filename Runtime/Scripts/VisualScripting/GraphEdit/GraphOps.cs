#if BANTER_VISUAL_SCRIPTING
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS
{
    public class PortRef
    {
        public string unitId;
        public string port;
    }

    /// <summary>
    /// One edit operation from the page. Flat DTO with an <c>op</c> discriminator; unused
    /// fields stay null. The vocabulary is append-only: existing names never change meaning.
    /// </summary>
    public class GraphOp
    {
        public string op;
        public string unitId;
        public string type;
        public float[] pos;
        public string kind; // control | value
        public PortRef src;
        public PortRef dst;
        public string port;
        public PortValue value;
        public ObjectRefDescriptor @ref;
        public string title;
        public string name;
        // Member units
        public string memberKind; // get | set | invoke | ctor
        public string declaringType;
        public string member;
        public string[] paramTypes;
    }

    public class OpBatch
    {
        public string sessionId;
        public int baseRev;
        public List<GraphOp> ops;
    }

    /// <summary>
    /// Wire encoding of a port default: {type, v[, enumType]}. Covers the
    /// ValueInput.SupportsDefaultValue set minus the exotic ones (AnimationCurve, Ray,
    /// InputAction), which stay editable only through Unity.
    /// </summary>
    public class PortValue
    {
        public string type;
        public JToken v;
        public string enumType;

        public static PortValue FromObject(object value)
        {
            switch (value)
            {
                case null: return new PortValue { type = "null" };
                case bool b: return new PortValue { type = "bool", v = b };
                case int i: return new PortValue { type = "int", v = i };
                case float f: return new PortValue { type = "float", v = f };
                case string s: return new PortValue { type = "string", v = s };
                case Vector2 v2: return new PortValue { type = "Vector2", v = new JArray(v2.x, v2.y) };
                case Vector3 v3: return new PortValue { type = "Vector3", v = new JArray(v3.x, v3.y, v3.z) };
                case Vector4 v4: return new PortValue { type = "Vector4", v = new JArray(v4.x, v4.y, v4.z, v4.w) };
                case Color c: return new PortValue { type = "Color", v = new JArray(c.r, c.g, c.b, c.a) };
                case Rect r: return new PortValue { type = "Rect", v = new JArray(r.x, r.y, r.width, r.height) };
                case Enum e: return new PortValue
                {
                    type = "enum",
                    enumType = e.GetType().FullName,
                    v = e.ToString(),
                };
                default: return null; // not representable on the wire (UnityObject refs travel as descriptors)
            }
        }

        public object ToObject()
        {
            switch (type)
            {
                case "null": return null;
                case "bool": return v.Value<bool>();
                case "int": return v.Value<int>();
                case "float": return v.Value<float>();
                case "string": return v.Value<string>();
                case "Vector2":
                {
                    var a = (JArray)v;
                    return new Vector2(a[0].Value<float>(), a[1].Value<float>());
                }
                case "Vector3":
                {
                    var a = (JArray)v;
                    return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
                }
                case "Vector4":
                {
                    var a = (JArray)v;
                    return new Vector4(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>());
                }
                case "Color":
                {
                    var a = (JArray)v;
                    return new Color(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(),
                        a.Count > 3 ? a[3].Value<float>() : 1f);
                }
                case "Rect":
                {
                    var a = (JArray)v;
                    return new Rect(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>());
                }
                case "enum":
                {
                    var enumClrType = RuntimeCodebase.DeserializeType(enumType);
                    return Enum.Parse(enumClrType, v.Value<string>());
                }
                case "type":
                    return RuntimeCodebase.DeserializeType(v.Value<string>());
                default:
                    throw new ArgumentException($"Unsupported port value type '{type}'");
            }
        }
    }

    public class GraphEnvelope
    {
        public int v = 1;
        public string uvsJson;
        public List<ObjectRefDescriptor> objectRefDescriptors;
        // "unitId:port" -> the descriptor that failed to resolve while editing. Null defaults
        // serialize as plain JSON null (no slot in objectReferences), so without this the
        // original reference would be lost on the first save after a failed resolve.
        public Dictionary<string, ObjectRefDescriptor> unresolvedPortRefs;
        public GraphEnvelopeMeta meta;
    }

    public class GraphEnvelopeMeta
    {
        public TargetRef target;
        public string baseGraphTitle;
        public string baseGraphRef;
        public string savedAt;
        public string sdkVersion;
        public int editorRev;
        public int nodeCount;
    }
}
#endif
