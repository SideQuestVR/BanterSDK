using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Banter.SDKEditor
{
    [ScriptedImporter(1, new[] { "js", "bs" }, importQueueOffset: -100)]
    class JsImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var fileContents = File.ReadAllText(ctx.assetPath);
            var textAsset = new TextAsset(fileContents);

            var bsIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.sidequest.banter/Gizmos/BanterObjectId Icon.png");

            ctx.AddObjectToAsset("Script", textAsset, bsIcon);
            ctx.SetMainObject(textAsset);
        }
    }
}
