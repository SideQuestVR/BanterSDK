using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /*
     * Fetches snippet markup from the SideQuest API. The response is the raw markup fragment
     * (server-controlled, XML-well-formed), NOT JSON. Snippets are fetched ONCE — after injection
     * the copy in index.html is the cache; only the inspector's explicit Refresh re-fetches.
     *
     * Same shape as SqEditorAppApi: a static HttpClient, task-pumped editor coroutines, and
     * Action callbacks. A throw inside an editor coroutine can't be caught by the caller and
     * lands in the console as an unhandled exception, so everything reports through onError.
     */
    public static class SnippetApi
    {
        public const string BaseUrl = "https://altvr.app/api/snippets/";

        // Interactive inspector action, not an upload — short timeout.
        static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static void Fetch(string slug, Action<XElement> onCompleted, Action<Exception> onError)
        {
            // Ownerless so an inspector rebuild mid-fetch doesn't kill the request; callers
            // null-check their target before applying the result.
            EditorCoroutineUtility.StartCoroutineOwnerless(FetchCo(slug, onCompleted, onError));
        }

        static IEnumerator FetchCo(string slug, Action<XElement> onCompleted, Action<Exception> onError)
        {
            slug = (slug ?? "").Trim();
            if (slug.Length == 0)
            {
                onError(new Exception("No slug entered."));
                yield break;
            }

            var task = _http.GetAsync(BaseUrl + Uri.EscapeDataString(slug));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted || task.IsCanceled)
            {
                onError(new Exception("Network error: " + Root(task.Exception).Message));
                yield break;
            }
            var response = task.Result;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                onError(new Exception($"No snippet named '{slug}' on altvr.app."));
                yield break;
            }
            if (!response.IsSuccessStatusCode)
            {
                onError(new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"));
                yield break;
            }

            var readTask = response.Content.ReadAsStringAsync();
            while (!readTask.IsCompleted) yield return null;
            if (readTask.IsFaulted)
            {
                onError(new Exception("Network error: " + Root(readTask.Exception).Message));
                yield break;
            }

            XElement element;
            try
            {
                element = XElement.Parse(readTask.Result);
                Normalize(element);
            }
            catch (Exception e)
            {
                onError(new Exception("Server returned an unparseable snippet: " + e.Message));
                yield break;
            }

            var validationError = Validate(element, slug);
            if (validationError != null)
                onError(validationError);
            else
                onCompleted(element);
        }

        /*
         * Shared post-fetch pipeline used by the inspector, the reconciler's heal path and
         * Refresh: pair the element to the component and inject it. Caching title/description on
         * the component keeps the inspector meaningful (and the scene file self-documenting) even
         * when the HTML element can't be read.
         */
        public static void ApplyFetched(BSSnippet component, XElement element)
        {
            if (component == null || element == null) return;
            SnippetReconciler.EnsureInstanceId(component);
            // The slug IS the snippet's id: the reconciler pairs element name <-> component slug,
            // so a server returning a display name here (observed in the wild) would otherwise
            // trip the mismatch branch into an endless refetch loop.
            element.SetAttributeValue("name", component.Slug);
            SnippetHtmlSync.Upsert(component.InstanceId, element);

            var serialized = new SerializedObject(component);
            serialized.FindProperty("cachedTitle").stringValue = (string)element.Attribute("title") ?? "";
            serialized.FindProperty("cachedDescription").stringValue = (string)element.Attribute("description") ?? "";
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        // Tolerate a server still returning the unprefixed element names; the browser requires
        // hyphenated custom-element names, so bs-snippet/bs-gizmo are canonical everywhere.
        static void Normalize(XElement element)
        {
            if (element.Name.LocalName == "snippet")
                element.Name = SnippetHtmlSync.ElementName;
            foreach (var gizmo in element.Descendants("gizmo").ToList())
                gizmo.Name = SnippetHtmlSync.GizmoElementName;
        }

        static Exception Validate(XElement element, string slug)
        {
            if (element.Name.LocalName != SnippetHtmlSync.ElementName)
                return new Exception($"Snippet '{slug}' has root <{element.Name.LocalName}>, expected <{SnippetHtmlSync.ElementName}>.");
            // "name" is not required from the server (the slug is the id and ApplyFetched stamps
            // it over whatever the server sent) and "description" is optional — only the title
            // is mandatory.
            if (string.IsNullOrEmpty((string)element.Attribute("title")))
                return new Exception($"Snippet '{slug}' is missing the required 'title' attribute.");
            var hasScript = !string.IsNullOrEmpty((string)element.Attribute("script"));
            var hasAsset = !string.IsNullOrEmpty((string)element.Attribute("asset"));
            if (!hasScript && !hasAsset)
                return new Exception($"Snippet '{slug}' has neither a 'script' nor an 'asset' attribute.");
            return null;
        }

        static Exception Root(Exception e)
        {
            while (e is AggregateException agg && agg.InnerException != null) e = agg.InnerException;
            return e ?? new Exception("Unknown error");
        }
    }
}
