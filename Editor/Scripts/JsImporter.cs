using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

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
