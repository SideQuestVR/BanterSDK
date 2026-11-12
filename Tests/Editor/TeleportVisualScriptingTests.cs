using System;
using BS;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class TeleportVisualScriptingTests
    {
        [TestCase("Destination", typeof(Transform))]
        [TestCase("DestinationPosition", typeof(Vector3))]
        [TestCase("DestinationYaw", typeof(float))]
        [TestCase("UseDestinationPosition", typeof(bool))]
        public void DestinationProperties_ExposeTypedGetAndSetNodesAllowedBySdkAndClient(string property, Type valueType)
        {
            var graph = new FlowGraph();
            var setter = new SetMember(new Member(typeof(BSTeleporter), property));
            var getter = new GetMember(new Member(typeof(BSTeleporter), property));
            graph.units.Add(setter);
            graph.units.Add(getter);
            setter.EnsureDefined();
            getter.EnsureDefined();

            Assert.That(setter.input, Is.Not.Null);
            Assert.That(setter.input.type, Is.EqualTo(valueType));
            Assert.That(getter.value, Is.Not.Null);
            Assert.That(getter.value.type, Is.EqualTo(valueType));
            foreach (var unit in new MemberUnit[] { setter, getter })
            {
                var id = unit.GetAnalyticsIdentifier().Identifier.Split('(')[0].Trim();
                Assert.That(id, Is.EqualTo("BS.BSTeleporter." + property));
                Assert.That(ValidateVisualScripting.IsDisallowed(id), Is.False);
            }
#if GREENFIELD_PROJECT
            // The client-side allowlist lives in Greenfield's visual scripting fork.
            Assert.That(BanterStubsAllowed.IsBlocked(graph), Is.False);
#endif
        }

        [Test]
        public void RefreshPreview_ExposesAnAllowedInvokeNode()
        {
            var graph = new FlowGraph();
            var invoke = new InvokeMember(new Member(typeof(BSTeleporter), "RefreshPortalPreview", Type.EmptyTypes));
            graph.units.Add(invoke);
            invoke.EnsureDefined();
            Assert.That(invoke.isDefined, Is.True);
            var id = invoke.GetAnalyticsIdentifier().Identifier.Split('(')[0].Trim();
            Assert.That(id, Is.EqualTo("BS.BSTeleporter.RefreshPortalPreview"));
            Assert.That(ValidateVisualScripting.IsDisallowed(id), Is.False);
#if GREENFIELD_PROJECT
            Assert.That(BanterStubsAllowed.IsBlocked(graph), Is.False);
#endif
        }
    }
}
