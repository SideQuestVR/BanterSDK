using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class GripHologramResourceTests
    {
        [Test]
        public void DynamicHand_HasAuthoredUV2AndBones_WithSharedTextureFreeDepthAndGhostMaterials()
        {
            var source = Resources.Load<GameObject>("CreatorConveniences/Models/GripHand");
            Assert.That(source, Is.Not.Null);
            var skin = source.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.bones.Length, Is.GreaterThanOrEqualTo(16));
            var mesh = skin.sharedMesh;
            Assert.That(mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.boneWeights.Length, Is.EqualTo(mesh.vertexCount));
            foreach (var weight in mesh.boneWeights)
                Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1f).Within(.001f));
            Assert.That(mesh.triangles.Length / 3, Is.InRange(200, 1200));
            foreach (var uv in mesh.uv2)
                Assert.That(uv.y, Is.InRange(-.0001f, 1.0001f));
            Assert.That(BSGripHologram.SharedMaterial.shader.name, Is.EqualTo("Banter/Grip Hologram"));
            Assert.That(BSGripHologram.SharedMaterial.passCount, Is.EqualTo(1));
            Assert.That(BSGripHologram.SharedMaterial.GetTag("RenderType", false), Is.EqualTo("Transparent"));
            Assert.That(BSGripHologram.SharedMaterial.renderQueue, Is.EqualTo(3101));
            var depth = BSGripHologram.SharedDepthMaterial;
            Assert.That(depth, Is.Not.Null);
            Assert.That(depth.shader, Is.SameAs(BSGripHologram.SharedMaterial.shader));
            Assert.That(depth.enableInstancing, Is.True);
            Assert.That(BSGripHologram.SharedMaterial.enableInstancing, Is.True);
            Assert.That(depth.renderQueue, Is.EqualTo(3100));
            Assert.That(depth.GetFloat("_ColorMask"), Is.Zero);
            Assert.That(depth.GetFloat("_ZWrite"), Is.EqualTo(1));
            Assert.That(BSGripHologram.SharedMaterial.GetFloat("_ZTest"), Is.EqualTo((float)UnityEngine.Rendering.CompareFunction.Equal));
            Assert.That(BSGripHologram.SharedMaterial.GetTexturePropertyNames(), Is.Empty);
            Assert.That(ShaderUtil.ShaderHasError(BSGripHologram.SharedMaterial.shader), Is.False);
            var seat = Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker");
            Assert.That(BSGripHologram.SharedMaterial.GetColor("_Color"), Is.EqualTo(seat.GetColor("_SeatColor")));
        }
    }
}
