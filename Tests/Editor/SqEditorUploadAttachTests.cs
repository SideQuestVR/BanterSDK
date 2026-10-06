using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A world, community or plain upload must hand out the CDN file only after the storage PUT succeeded.
    /// /create-upload makes the files row before any bytes exist, and the attach repoints the world's live
    /// slot at that row while the API deletes the file it replaces, so attaching after a failed PUT used to
    /// wipe the world's live asset.world / index.html / script.js (and PersistedWorldFiles' runtime
    /// overrides), and UploadFile handed avatars a file id with nothing behind it.
    /// These run the real SqEditorAppApi coroutines against a fake transport: no network, and the saved
    /// login lives in a throwaway folder, never the real one.
    /// </summary>
    public class SqEditorUploadAttachTests
    {
        const long FileId = 4242;
        const string StorageHost = "storage.test.invalid";

        public enum Method { World, Community, File }

        sealed class FakeTransport : HttpMessageHandler
        {
            public HttpStatusCode CreateUploadStatus = HttpStatusCode.OK;
            public HttpStatusCode PutStatus = HttpStatusCode.OK;

            readonly object _gate = new object();
            readonly List<(HttpMethod Method, Uri Uri, string Body)> _requests = new List<(HttpMethod, Uri, string)>();

            public List<(HttpMethod Method, Uri Uri, string Body)> Requests
            {
                get { lock (_gate) return _requests.ToList(); }
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var host = request.RequestUri.Host;
                var path = request.RequestUri.AbsolutePath;
                // API bodies are JSON worth asserting on; the storage PUT body is just the file bytes.
                var body = request.Content != null && host != StorageHost ? await request.Content.ReadAsStringAsync() : null;
                lock (_gate) _requests.Add((request.Method, request.RequestUri, body));

                if (host == "cdn.sidetestvr.com" && path == "/create-upload")
                    return CreateUploadStatus == HttpStatusCode.OK
                        ? Json($"{{\"fileId\":{FileId},\"path\":\"file/{FileId}/test.bin\",\"upload_uri\":\"https://{StorageHost}/put/{FileId}\",\"contentType\":\"application/octet-stream\",\"communities_id\":77}}")
                        : new HttpResponseMessage(CreateUploadStatus) { Content = new StringContent("{}") };
                if (host == StorageHost)
                    return new HttpResponseMessage(PutStatus) { Content = new StringContent("") };
                if (host == "api.sidetestvr.com" && IsAttach(request.Method, path))
                    return Json($"{{\"fileId\":{FileId}}}");
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
            }

            static HttpResponseMessage Json(string json) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        sealed class Outcome
        {
            public int Completed;
            public SqEditorCreateUpload Result;
            public readonly List<Exception> Errors = new List<Exception>();
        }

        string _dataDir;
        FakeTransport _transport;
        SqEditorAppApi _api;

        [SetUp]
        public void SetUp()
        {
            _dataDir = Path.Combine(Path.GetTempPath(), "SqEditorUploadAttachTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dataDir);
            _transport = new FakeTransport();
            _api = new SqEditorAppApi(new SqEditorAppApiConfig("test-client", _dataDir, testMode: true), new HttpClient(_transport));
            _api.UploadRetryDelay = _ => TimeSpan.Zero;
            // A token that is valid for an hour, so no refresh request is made.
            _api.Data.Token = new SqEditorTokenInfo
            {
                AccessToken = "test-access-token",
                AccessTokenExpiresAt = DateTimeOffset.Now.AddHours(1),
                RefreshToken = "test-refresh-token",
                ClientId = "test-client",
                UserId = 1,
            };
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dataDir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [TestCase(Method.World, HttpStatusCode.Forbidden, 1)]
        [TestCase(Method.World, HttpStatusCode.InternalServerError, 3)]
        [TestCase(Method.Community, HttpStatusCode.Forbidden, 1)]
        [TestCase(Method.Community, HttpStatusCode.InternalServerError, 3)]
        [TestCase(Method.File, HttpStatusCode.Forbidden, 1)]
        [TestCase(Method.File, HttpStatusCode.InternalServerError, 3)]
        public void FailedPut_NeverAttachesOrCompletes(Method method, HttpStatusCode putStatus, int expectedPutAttempts)
        {
            _transport.PutStatus = putStatus;

            var outcome = RunUpload(method);

            Assert.AreEqual(0, outcome.Completed, "OnCompleted fired although the bytes never reached storage");
            Assert.AreEqual(1, outcome.Errors.Count, "OnError should fire exactly once");
            Assert.AreEqual(expectedPutAttempts, _transport.Requests.Count(r => r.Uri.Host == StorageHost), "storage PUT attempts");
            Assert.AreEqual(0, AttachCount(), "an attach was sent for a file whose bytes never arrived");
        }

        [TestCase(Method.World)]
        [TestCase(Method.Community)]
        [TestCase(Method.File)]
        public void SuccessfulPut_CompletesOnce_AndAttachesTheUploadedFile(Method method)
        {
            var outcome = RunUpload(method);

            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors.Select(e => e.Message)));
            Assert.AreEqual(1, outcome.Completed);
            Assert.AreEqual(FileId, outcome.Result?.FileId);
            var attaches = _transport.Requests.Where(r => r.Uri.Host == "api.sidetestvr.com" && IsAttach(r.Method, r.Uri.AbsolutePath)).ToList();
            if (method == Method.File)
            {
                Assert.AreEqual(0, attaches.Count, "UploadFile only uploads; it never attaches");
            }
            else
            {
                Assert.AreEqual(1, attaches.Count);
                StringAssert.Contains("\"files_id\":" + FileId, attaches[0].Body);
            }
        }

        [TestCase(Method.World)]
        [TestCase(Method.Community)]
        [TestCase(Method.File)]
        public void FailedCreateUpload_ReportsOneError_AndSendsNothingElse(Method method)
        {
            _transport.CreateUploadStatus = HttpStatusCode.InternalServerError;

            var outcome = RunUpload(method);

            Assert.AreEqual(0, outcome.Completed);
            Assert.AreEqual(1, outcome.Errors.Count, "OnError should fire once, with the create-upload error");
            Assert.AreEqual(0, _transport.Requests.Count(r => r.Uri.Host == StorageHost), "nothing to PUT without an upload URL");
            Assert.AreEqual(0, AttachCount());
        }

        Outcome RunUpload(Method method)
        {
            var outcome = new Outcome();
            var data = Encoding.UTF8.GetBytes("test bytes");
            Action<SqEditorCreateUpload> done = u => { outcome.Completed++; outcome.Result = u; };
            Action<Exception> failed = e => outcome.Errors.Add(e);
            IEnumerator routine;
            switch (method)
            {
                case Method.World:
                    routine = _api.UploadFileToWorld("asset.world", data, "123", "test-world", done, failed, UploadAssetType.WorldAsset, UploadAssetTypePlatform.Any);
                    break;
                case Method.Community:
                    routine = _api.UploadFileToCommunity("index.html", data, "test-space", done, failed, UploadAssetType.Index, UploadAssetTypePlatform.Any);
                    break;
                default:
                    routine = _api.UploadFile("avatar.bin", data, "", done, failed);
                    break;
            }

            Pump(routine);

            var hosts = _transport.Requests.Select(r => r.Uri.Host).Distinct().ToList();
            Assert.That(hosts.All(h => h == "cdn.sidetestvr.com" || h == "api.sidetestvr.com" || h == StorageHost),
                "unexpected host: " + string.Join(", ", hosts));
            return outcome;
        }

        int AttachCount() => _transport.Requests.Count(r => IsAttach(r.Method, r.Uri.AbsolutePath));

        static bool IsAttach(HttpMethod method, string path) =>
            method == HttpMethod.Put && (path.StartsWith("/v2/worlds/") || path.StartsWith("/v2/communities/"));

        /// <summary>
        /// Drives a coroutine the way EditorCoroutines does (a finished nested routine resumes its parent at
        /// once; a null yield waits a "frame"). The synchronization context is cleared so the upload's awaits
        /// continue on the thread pool instead of waiting for an editor frame that a blocking test never gives.
        /// </summary>
        static void Pump(IEnumerator routine)
        {
            var saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                var stack = new Stack<IEnumerator>();
                stack.Push(routine);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (stack.Count > 0)
                {
                    if (DateTime.UtcNow > deadline)
                        Assert.Fail("The upload coroutine did not finish within 30 s");
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    Thread.Sleep(1);
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }
    }
}
