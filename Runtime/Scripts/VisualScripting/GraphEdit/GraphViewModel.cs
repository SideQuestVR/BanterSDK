#if BANTER_VISUAL_SCRIPTING
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Builds the JSON view-model the page renders from. The page never parses FullSerializer
    /// JSON - this is its entire window into a graph.
    /// </summary>
    public static class GraphViewModel
    {
        public static JObject Build(ScriptGraphSession session)
        {
            var graph = session.staging.graph;
            var units = new JArray();
            foreach (var unit in graph.units)
            {
                units.Add(BuildUnit(session, unit));
            }

            var control = new JArray();
            foreach (var connection in graph.controlConnections)
            {
                control.Add(new JArray(
                    new JArray(connection.source.unit.guid.ToString(), connection.source.key),
                    new JArray(connection.destination.unit.guid.ToString(), connection.destination.key)));
            }

            var value = new JArray();
            foreach (var connection in graph.valueConnections)
            {
                value.Add(new JArray(
                    new JArray(connection.source.unit.guid.ToString(), connection.source.key),
                    new JArray(connection.destination.unit.guid.ToString(), connection.destination.key)));
            }

            var variables = new JArray();
            foreach (var declaration in graph.variables)
            {
                var portValue = PortValue.FromObject(declaration.value);
                variables.Add(new JObject
                {
                    ["name"] = declaration.name,
                    ["value"] = portValue != null ? JObject.FromObject(portValue) : null,
                });
            }

            var warnings = new JArray();
            foreach (var pair in session.unresolvedPortRefs)
            {
                var keyParts = pair.Key.Split(':');
                warnings.Add(new JObject
                {
                    ["code"] = "unresolvedRef",
                    ["unitId"] = keyParts[0],
                    ["port"] = keyParts.Length > 1 ? keyParts[1] : "",
                    ["descriptor"] = JObject.FromObject(pair.Value),
                });
            }

            return new JObject
            {
                ["sessionId"] = session.sessionId,
                ["rev"] = session.rev,
                ["title"] = graph.title ?? "",
                ["baseGraphRef"] = session.baseGraphRef,
                ["units"] = units,
                ["connections"] = new JObject { ["control"] = control, ["value"] = value },
                ["variables"] = variables,
                ["warnings"] = warnings,
            };
        }

        static JObject BuildUnit(ScriptGraphSession session, IUnit unit)
        {
            EnsureDefinedQuietly(unit);

            var controlIn = new JArray();
            foreach (var port in unit.controlInputs)
            {
                controlIn.Add(new JObject { ["key"] = port.key, ["label"] = Prettify(port.key) });
            }

            var controlOut = new JArray();
            foreach (var port in unit.controlOutputs)
            {
                controlOut.Add(new JObject { ["key"] = port.key, ["label"] = Prettify(port.key) });
            }

            var valueIn = new JArray();
            foreach (var port in unit.valueInputs)
            {
                var entry = new JObject
                {
                    ["key"] = port.key,
                    ["label"] = Prettify(port.key),
                    ["type"] = DisplayType(port.type),
                    ["hasDefault"] = port.hasDefaultValue,
                    ["nullMeansSelf"] = port.nullMeansSelf,
                    ["isReference"] = typeof(UnityObject).IsAssignableFrom(port.type),
                };
                if (port.hasDefaultValue && unit.defaultValues.TryGetValue(port.key, out var defaultValue))
                {
                    if (defaultValue is UnityObject unityObject)
                    {
                        var slotKey = $"{unit.guid}:{port.key}";
                        entry["objectRef"] = JObject.FromObject(
                            session != null && session.unresolvedPortRefs.TryGetValue(slotKey, out var original)
                                ? original
                                : ObjectRefResolver.DescribeSingle(unityObject));
                    }
                    else
                    {
                        var portValue = PortValue.FromObject(defaultValue);
                        entry["default"] = portValue != null ? JObject.FromObject(portValue) : null;
                    }
                }
                valueIn.Add(entry);
            }

            var valueOut = new JArray();
            foreach (var port in unit.valueOutputs)
            {
                valueOut.Add(new JObject
                {
                    ["key"] = port.key,
                    ["label"] = Prettify(port.key),
                    ["type"] = DisplayType(port.type),
                });
            }

            return new JObject
            {
                ["id"] = unit.guid.ToString(),
                ["type"] = unit.GetType().FullName,
                ["title"] = TitleFor(unit),
                ["surtitle"] = SurtitleFor(unit),
                ["pos"] = new JArray(unit.position.x, unit.position.y),
                ["defined"] = unit.isDefined && !unit.failedToDefine,
                ["error"] = unit.failedToDefine ? unit.definitionException?.Message : null,
                ["controlIn"] = controlIn,
                ["controlOut"] = controlOut,
                ["valueIn"] = valueIn,
                ["valueOut"] = valueOut,
            };
        }

        public static string TitleFor(IUnit unit)
        {
            switch (unit)
            {
                case GetMember get when get.member != null:
                    return $"Get {Prettify(get.member.name)}";
                case SetMember set when set.member != null:
                    return $"Set {Prettify(set.member.name)}";
                case InvokeMember invoke when invoke.member != null:
                    return Prettify(invoke.member.name);
            }

            var attribute = unit.GetType().GetCustomAttribute<UnitTitleAttribute>();
            if (attribute != null && !string.IsNullOrEmpty(attribute.title))
            {
                return attribute.title;
            }
            return Prettify(unit.GetType().Name);
        }

        static string SurtitleFor(IUnit unit)
        {
            if (unit is MemberUnit memberUnit && memberUnit.member != null)
            {
                return memberUnit.member.targetType?.Name;
            }
            var attribute = unit.GetType().GetCustomAttribute<UnitSurtitleAttribute>();
            return attribute?.surtitle;
        }

        /// <summary>C#-ish short display name: float, Vector3, List&lt;int&gt;.</summary>
        public static string DisplayType(Type type)
        {
            if (type == null) return "object";
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";
            if (type.IsGenericType)
            {
                var arguments = string.Join(", ", type.GetGenericArguments().Select(DisplayType));
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                return $"{name}<{arguments}>";
            }
            return type.Name;
        }

        /// <summary>"%speed" -> "Speed", "targetVelocity" -> "Target Velocity".</summary>
        public static string Prettify(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var trimmed = key.TrimStart('%', '_');
            if (trimmed.Length == 0) return key;
            var builder = new StringBuilder(trimmed.Length + 4);
            builder.Append(char.ToUpperInvariant(trimmed[0]));
            for (var i = 1; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (char.IsUpper(c) && !char.IsUpper(trimmed[i - 1]))
                {
                    builder.Append(' ');
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        static void EnsureDefinedQuietly(IUnit unit)
        {
            try
            {
                unit.EnsureDefined();
            }
            catch
            {
                // Surfaced via defined/error fields.
            }
        }
    }
}
#endif
