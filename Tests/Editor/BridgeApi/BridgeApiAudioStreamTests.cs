using BS.Bridge;
using NUnit.Framework;
using UnityEngine;
using static BS.BridgeApi.Tests.BridgeApiFixture;

namespace BS.BridgeApi.Tests
{
    /// <summary>A BS Audio Stream as a page sees it: <c>BS.Unity.AudioStream</c>, through the real dispatcher.</summary>
    public class BridgeApiAudioStreamTests
    {
        const string S = "BS.BSAudioStream.";
        const string Role = "BS.BSAudioStreamRole";
        BridgeApiFixture f;
        BSAudioStream stream;
        int h;

        [SetUp]
        public void SetUp()
        {
            f = new BridgeApiFixture();
            stream = f.Add("stream", typeof(BSAudioStream)).GetComponent<BSAudioStream>();
            h = f.Handle(stream);
        }

        [TearDown] public void TearDown() => f.Dispose();

        static int TypeIdOf(System.Type t) => System.Array.IndexOf(BridgeApiThunks.BuildTypes(), t);

        AudioSource Speaker(string name) => f.Add(name, typeof(AudioSource)).GetComponent<AudioSource>();

        [Test]
        public void APageFindsItByItsClass()
        {
            int type = TypeIdOf(typeof(BSAudioStream));
            Assert.Greater(type, 0);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("GameObject_GetComponent/1"), f.Handle(stream.gameObject), Int(type)), f.Host.FailMessage);
            var v = f.Result();
            Assert.AreEqual(ValueTag.Ref, v.Tag);
            Assert.AreEqual(type, v.TypeId);
        }

        [Test]
        public void TheUrlTakesWebAddressesOnly()
        {
            const string good = "https://example.com/live/master.m3u8";
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Url.set"), h, Str(good)), f.Host.FailMessage);
            Assert.AreEqual(good, stream.Url);
            foreach (var bad in new[] { "file:///C:/audio/song.wav", "javascript:void(0)", "ftp://example.com/a.mp3", "not an address", "C:/audio/song.wav" })
            {
                Assert.AreEqual(ReplyStatus.Type, f.Call(Id(S + "Url.set"), h, Str(bad)), bad);
                Assert.AreEqual(good, stream.Url, bad);
            }
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Url.get"), h), f.Host.FailMessage);
            Assert.AreEqual(good, f.Result().S);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Url.set"), h, Str("")), "empty clears it");
            Assert.AreEqual("", stream.Url);
        }

        [Test]
        public void AVeryLongUrlIsRefused()
        {
            const string start = "https://example.com/";
            Assert.AreEqual(ReplyStatus.Limit, f.Call(Id(S + "Url.set"), h, Str(start + new string('a', 4097 - start.Length))), "past what the stream reads");
            Assert.AreEqual("", stream.Url);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Url.set"), h, Str(start + new string('a', 4096 - start.Length))), f.Host.FailMessage);
        }

        [Test]
        public void SettingsWriteAndStayInRange()
        {
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "PrebufferSeconds.set"), h, Float(2.5f)), f.Host.FailMessage);
            Assert.AreEqual(2.5f, stream.PrebufferSeconds);
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(S + "PrebufferSeconds.set"), h, Float(0)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(S + "PrebufferSeconds.set"), h, Float(11)));
            Assert.AreEqual(2.5f, stream.PrebufferSeconds);
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(S + "SpeakerRadius.set"), h, Float(-1)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "SpeakerRadius.set"), h, Float(4)), f.Host.FailMessage);
            Assert.AreEqual(4f, stream.SpeakerRadius);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Loop.set"), h, Bool(true)), f.Host.FailMessage);
            Assert.IsTrue(stream.Loop);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "PlayOnEnable.set"), h, Bool(false)), f.Host.FailMessage);
            Assert.IsFalse(stream.PlayOnEnable);
        }

        [Test]
        public void TheLayoutIsOneOfItsValues()
        {
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "MaxChannels.set"), h, Int(6)), f.Host.FailMessage);
            Assert.AreEqual(BSAudioStreamLayout.Surround51, stream.MaxChannels);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(S + "MaxChannels.set"), h, Int(3)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(S + "MaxChannels.set"), h, Int(-1)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(S + "MaxChannels.set"), h, Int(8)), "7.1 is not offered");
            Assert.AreEqual(BSAudioStreamLayout.Surround51, stream.MaxChannels);
        }

        [Test]
        public void AChannelPlaysThroughAnAudioSourceThePageHandsIt()
        {
            var speaker = Speaker("centre speaker");
            int set = Id(S + "SetChannelSource(" + Role + ",UnityEngine.AudioSource)");
            int get = Id(S + "GetChannelSource(" + Role + ")");
            Assert.AreEqual(ReplyStatus.Ok, f.Call(set, h, Int((int)BSAudioStreamRole.Center), Ref(f.Handle(speaker))), f.Host.FailMessage);
            Assert.AreSame(speaker, stream.GetChannelSource(BSAudioStreamRole.Center));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(get, h, Int((int)BSAudioStreamRole.Center)), f.Host.FailMessage);
            var v = f.Result();
            Assert.AreEqual(ValueTag.Ref, v.Tag);
            Assert.AreEqual(TypeIdOf(typeof(AudioSource)), v.TypeId);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(set, h, Int((int)BSAudioStreamRole.Center), Null), "null goes back to a created source");
            Assert.IsNull(stream.GetChannelSource(BSAudioStreamRole.Center));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(get, h, Int((int)BSAudioStreamRole.Center)), f.Host.FailMessage);
            Assert.AreEqual(ValueTag.Null, f.Result().Tag);
        }

        [Test]
        public void AnAudioSourceThatLeftTheWorldIsRefused()
        {
            var speaker = Speaker("speaker");
            int handle = f.Handle(speaker);
            speaker.transform.SetParent(f.Outside.transform);
            int set = Id(S + "SetChannelSource(" + Role + ",UnityEngine.AudioSource)");
            Assert.AreEqual(ReplyStatus.Denied, f.Call(set, h, Int((int)BSAudioStreamRole.FrontLeft), Ref(handle)));
            Assert.IsNull(stream.GetChannelSource(BSAudioStreamRole.FrontLeft));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(S + "SourceTemplate.set"), h, Ref(handle)));
            Assert.IsNull(stream.SourceTemplate);
        }

        [Test]
        public void AChannelOutsideTheWorldReadsAsNull()
        {
            var speaker = Speaker("speaker");
            stream.SetChannelSource(BSAudioStreamRole.FrontRight, speaker);
            speaker.transform.SetParent(f.Outside.transform);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "GetChannelSource(" + Role + ")"), h, Int((int)BSAudioStreamRole.FrontRight)), f.Host.FailMessage);
            Assert.AreEqual(ValueTag.Null, f.Result().Tag);
        }

        [Test]
        public void ARoleThatIsNoChannelIsRefused()
        {
            int set = Id(S + "SetChannelEnabled(" + Role + ",System.Boolean)");
            Assert.AreEqual(ReplyStatus.Type, f.Call(set, h, Int(8), Bool(false)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(set, h, Int(-1), Bool(false)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(set, h, Int((int)BSAudioStreamRole.Lfe), Bool(false)), f.Host.FailMessage);
            Assert.IsFalse(stream.GetChannelEnabled(BSAudioStreamRole.Lfe));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "GetChannelEnabled(" + Role + ")"), h, Int((int)BSAudioStreamRole.Lfe)), f.Host.FailMessage);
            Assert.AreEqual(0, f.Result().I);
        }

        [Test]
        public void TheTemplateIsAReferenceToo()
        {
            var template = Speaker("template");
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "SourceTemplate.set"), h, Ref(f.Handle(template))), f.Host.FailMessage);
            Assert.AreSame(template, stream.SourceTemplate);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "SourceTemplate.set"), h, Null), f.Host.FailMessage);
            Assert.IsNull(stream.SourceTemplate);
        }

        [Test]
        public void ItsStatusReadsBeforeAnythingHasPlayed()
        {
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "State.get"), h), f.Host.FailMessage);
            Assert.AreEqual((int)BSAudioStreamState.Stopped, f.Result().I);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "Codec.get"), h), f.Host.FailMessage);
            Assert.AreEqual("", f.Result().S);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("AudioStream_GetUnderruns/0"), h), f.Host.FailMessage);
            Assert.AreEqual(0, f.Result().I);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(S + "BufferedSeconds.get"), h), f.Host.FailMessage);
            Assert.AreEqual(0d, f.Result().D);
        }

        [Test]
        public void AnotherClassOfObjectIsNotAStream()
        {
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(S + "Play()"), f.Handle(Speaker("not a stream"))));
        }
    }
}
