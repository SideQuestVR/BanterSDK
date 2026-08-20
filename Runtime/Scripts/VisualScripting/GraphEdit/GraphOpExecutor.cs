#if BANTER_VISUAL_SCRIPTING
using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Applies one GraphOp to a staging FlowGraph. Every mutation validates before touching the
    /// graph and throws on anything invalid - the session wraps a batch in a snapshot/restore,
    /// so a throw leaves the staging graph exactly as it was.
    /// </summary>
    public static class GraphOpExecutor
    {
        public static void Apply(ScriptGraphSession session, GraphOp op)
        {
            var graph = session.staging.graph;
            switch (op.op)
            {
                case "addUnit": AddUnit(graph, op); break;
                case "addMemberUnit": AddMemberUnit(graph, op); break;
                case "removeUnit": RemoveUnit(graph, op); break;
                case "setPosition": FindUnit(graph, op.unitId).position = ToVector2(op.pos); break;
                case "connect": Connect(graph, op); break;
                case "disconnect": Disconnect(graph, op); break;
                case "setPortDefault": SetPortDefault(graph, op); break;
                case "clearPortDefault": FindUnit(graph, op.unitId).defaultValues.Remove(RequirePort(op)); break;
                case "setObjectRef": SetObjectRef(session, graph, op); break;
                case "setGraphTitle": graph.title = op.title ?? ""; break;
                case "setGraphVariable": SetGraphVariable(graph, op); break;
                case "removeGraphVariable": RemoveGraphVariable(graph, op); break;
                default:
                    throw new ArgumentException($"Unknown op '{op.op}'");
            }
        }

        static Vector2 ToVector2(float[] pos)
        {
            if (pos == null || pos.Length < 2) throw new ArgumentException("Op is missing pos");
            return new Vector2(pos[0], pos[1]);
        }

        static string RequirePort(GraphOp op)
        {
            if (string.IsNullOrEmpty(op.port)) throw new ArgumentException($"Op '{op.op}' is missing port");
            return op.port;
        }

        public static IUnit FindUnit(FlowGraph graph, string unitId)
        {
            var guid = Guid.Parse(unitId);
            var unit = graph.units.FirstOrDefault(u => u.guid == guid);
            if (unit == null) throw new ArgumentException($"No unit with id {unitId}");
            return unit;
        }

        static void AddUnit(FlowGraph graph, GraphOp op)
        {
            var type = RuntimeCodebase.DeserializeType(op.type);
            if (!typeof(IUnit).IsAssignableFrom(type) || type.IsAbstract || type.IsGenericTypeDefinition)
            {
                throw new ArgumentException($"'{op.type}' is not a concrete unit type");
            }
            if (!IsAllowedUnitType(type))
            {
                throw new ArgumentException($"Unit type '{op.type}' is not on the allow-list");
            }

            var unit = (IUnit)Activator.CreateInstance(type);
            unit.guid = Guid.Parse(op.unitId);
            unit.position = ToVector2(op.pos);
            graph.units.Add(unit);
            EnsureDefinedQuietly(unit);
        }

        static void AddMemberUnit(FlowGraph graph, GraphOp op)
        {
            var declaringType = RuntimeCodebase.DeserializeType(op.declaringType);
            var identifier = $"{declaringType.FullName}.{op.member}";
            if (!BanterStubsAllowed.IsAllowedIdentifier(identifier))
            {
                throw new ArgumentException($"Member '{identifier}' is not on the allow-list");
            }

            Type[] paramTypes = null;
            if (op.paramTypes != null)
            {
                paramTypes = op.paramTypes.Select(RuntimeCodebase.DeserializeType).ToArray();
            }

            MemberUnit unit;
            switch (op.memberKind)
            {
                case "get":
                    unit = new GetMember { member = new Member(declaringType, op.member) };
                    break;
                case "set":
                    unit = new SetMember { member = new Member(declaringType, op.member) };
                    break;
                case "invoke":
                case "ctor":
                    unit = new InvokeMember { member = new Member(declaringType, op.member, paramTypes ?? Type.EmptyTypes) };
                    break;
                default:
                    throw new ArgumentException($"Unknown memberKind '{op.memberKind}'");
            }

            unit.guid = Guid.Parse(op.unitId);
            unit.position = ToVector2(op.pos);
            graph.units.Add(unit);
            EnsureDefinedQuietly(unit);
        }

        static void RemoveUnit(FlowGraph graph, GraphOp op)
        {
            var unit = FindUnit(graph, op.unitId);

            foreach (var connection in graph.controlConnections
                .Where(c => c.source.unit == unit || c.destination.unit == unit).ToList())
            {
                graph.controlConnections.Remove(connection);
            }
            foreach (var connection in graph.valueConnections
                .Where(c => c.source.unit == unit || c.destination.unit == unit).ToList())
            {
                graph.valueConnections.Remove(connection);
            }
            foreach (var connection in graph.invalidConnections
                .Where(c => c.source.unit == unit || c.destination.unit == unit).ToList())
            {
                graph.invalidConnections.Remove(connection);
            }

            graph.units.Remove(unit);
        }

        static void Connect(FlowGraph graph, GraphOp op)
        {
            var sourceUnit = FindUnit(graph, op.src.unitId);
            var destinationUnit = FindUnit(graph, op.dst.unitId);

            if (op.kind == "control")
            {
                var source = sourceUnit.controlOutputs.FirstOrDefault(p => p.key == op.src.port)
                    ?? throw new ArgumentException($"No control output '{op.src.port}'");
                var destination = destinationUnit.controlInputs.FirstOrDefault(p => p.key == op.dst.port)
                    ?? throw new ArgumentException($"No control input '{op.dst.port}'");
                if (!source.CanValidlyConnectTo(destination))
                {
                    throw new ArgumentException($"Cannot connect {op.src.port} to {op.dst.port}");
                }
                source.ValidlyConnectTo(destination);
            }
            else
            {
                var source = sourceUnit.valueOutputs.FirstOrDefault(p => p.key == op.src.port)
                    ?? throw new ArgumentException($"No value output '{op.src.port}'");
                var destination = destinationUnit.valueInputs.FirstOrDefault(p => p.key == op.dst.port)
                    ?? throw new ArgumentException($"No value input '{op.dst.port}'");
                if (!source.CanValidlyConnectTo(destination))
                {
                    throw new ArgumentException($"Cannot connect {op.src.port} to {op.dst.port}");
                }
                source.ValidlyConnectTo(destination);
            }
        }

        static void Disconnect(FlowGraph graph, GraphOp op)
        {
            // Guid comparison, not string: clients may send uppercase or otherwise
            // differently formatted UUIDs, and FindUnit is already format-tolerant.
            var srcId = Guid.Parse(op.src.unitId);
            var dstId = Guid.Parse(op.dst.unitId);
            if (op.kind == "control")
            {
                var connection = graph.controlConnections.FirstOrDefault(c =>
                    c.source.unit.guid == srcId && c.source.key == op.src.port
                    && c.destination.unit.guid == dstId && c.destination.key == op.dst.port);
                if (connection == null) throw new ArgumentException("Control connection not found");
                graph.controlConnections.Remove(connection);
            }
            else
            {
                var connection = graph.valueConnections.FirstOrDefault(c =>
                    c.source.unit.guid == srcId && c.source.key == op.src.port
                    && c.destination.unit.guid == dstId && c.destination.key == op.dst.port);
                if (connection == null) throw new ArgumentException("Value connection not found");
                graph.valueConnections.Remove(connection);
            }
        }

        static void SetPortDefault(FlowGraph graph, GraphOp op)
        {
            var unit = FindUnit(graph, op.unitId);
            var port = unit.valueInputs.FirstOrDefault(p => p.key == RequirePort(op))
                ?? throw new ArgumentException($"No value input '{op.port}'");
            var value = op.value?.ToObject();
            if (value != null && !port.type.IsInstanceOfType(value))
            {
                value = ConversionUtility.Convert(value, port.type);
            }
            port.SetDefaultValue(value);
        }

        static void SetObjectRef(ScriptGraphSession session, FlowGraph graph, GraphOp op)
        {
            var unit = FindUnit(graph, op.unitId);
            var port = unit.valueInputs.FirstOrDefault(p => p.key == RequirePort(op))
                ?? throw new ArgumentException($"No value input '{op.port}'");
            if (!typeof(UnityObject).IsAssignableFrom(port.type))
            {
                throw new ArgumentException($"Port '{op.port}' does not take a Unity object");
            }

            var resolved = ObjectRefResolver.ResolveSingle(op.@ref, out var failed);
            var slotKey = $"{op.unitId}:{op.port}";
            if (failed && op.@ref != null && op.@ref.kind != "null")
            {
                session.unresolvedPortRefs[slotKey] = op.@ref;
            }
            else
            {
                session.unresolvedPortRefs.Remove(slotKey);
            }
            port.SetDefaultValue(resolved);
        }

        static void SetGraphVariable(FlowGraph graph, GraphOp op)
        {
            if (string.IsNullOrEmpty(op.name)) throw new ArgumentException("Variable name required");
            graph.variables.Set(op.name, op.value?.ToObject());
        }

        static void RemoveGraphVariable(FlowGraph graph, GraphOp op)
        {
            // VariableDeclarations has no Remove; rebuild without the removed name.
            var kept = graph.variables.Where(d => d.name != op.name).ToList();
            graph.variables.Clear();
            foreach (var declaration in kept)
            {
                graph.variables.Set(declaration.name, declaration.value);
            }
        }

        /// <summary>
        /// Mirrors BanterStubsAllowed.IsBlocked's per-element policy for standalone unit types:
        /// the VS assemblies and the Banter/PicaVoxel node packs are allowed wholesale.
        /// </summary>
        static bool IsAllowedUnitType(Type type)
        {
            var fullName = type.FullName ?? "";
            return fullName.StartsWith("Unity.VisualScripting.")
                || fullName.StartsWith("BS.VisualScripting.")
                || fullName.StartsWith("Banter.VisualScripting.")
                || fullName.StartsWith("PicaVoxel.VisualScripting.")
                || BanterStubsAllowed.IsAllowedIdentifier(fullName);
        }

        static void EnsureDefinedQuietly(IUnit unit)
        {
            try
            {
                unit.EnsureDefined();
            }
            catch
            {
                // Definition failures surface through the view-model (defined/error), not as
                // op failures - an undefined unit is a valid editing state.
            }
        }
    }
}
#endif
