using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS.SDKEditor.Tests
{
    public class InteractionStereoShaderTests
    {
        static IEnumerable StereoCases()
        {
            foreach (var material in new[] { "GripHologram", "SeatMarker", "TeleporterPortal" })
                foreach (var backend in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.GLES3x })
                    foreach (var keyword in new[] { "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON" })
                    {
                        if (backend == ShaderCompilerPlatform.D3D && keyword == "STEREO_MULTIVIEW_ON") continue;
                        yield return new TestCaseData(material, backend, keyword);
                    }
        }

        [TestCaseSource(nameof(StereoCases))]
        public void InteractionShader_CompilesStereoVariantForPCAndAndroid(string name, ShaderCompilerPlatform backend, string keyword)
        {
            var material = Resources.Load<Material>("CreatorConveniences/Materials/" + name);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.enableInstancing, Is.True);
            var pass = ShaderUtil.GetShaderData(material.shader).GetSubshader(0).GetPass(0);
            var target = backend == ShaderCompilerPlatform.D3D ? BuildTarget.StandaloneWindows64 : BuildTarget.Android;
            foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
            {
                var result = pass.CompileVariant(stage, new[] { keyword }, backend, target, Array.Empty<BuiltinShaderDefine>());
                Assert.That(result.Success, Is.True, name + ": " + backend + "/" + keyword + "/" + stage);
                // GLES/Vulkan return the linked program from the vertex compilation.
                if (stage == UnityEditor.Rendering.ShaderType.Vertex || backend == ShaderCompilerPlatform.D3D)
                    Assert.That(result.ShaderData, Is.Not.Empty);
            }
        }
    }
}
