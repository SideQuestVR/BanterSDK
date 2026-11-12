using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// When the page's pose-update fires: only once the head or a hand moved by more than 0.5 mm or 0.05 degrees since
    /// the last one (BanterSceneEventHandler.PoseMoved).
    /// </summary>
    public class AvatarPagePoseTests
    {
        GameObject _head;
        GameObject _left;
        GameObject _right;

        [SetUp]
        public void SetUp()
        {
            _head = new GameObject("Head");
            _left = new GameObject("LeftHand");
            _right = new GameObject("RightHand");
            _head.transform.position = new Vector3(0f, 1.6f, 0f);
            _left.transform.position = new Vector3(-0.2f, 1f, 0.2f);
            _right.transform.position = new Vector3(0.2f, 1f, 0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_head);
            Object.DestroyImmediate(_left);
            Object.DestroyImmediate(_right);
        }

        [Test]
        public void TheFirstPoseCounts_ThenAStillOneDoesNot()
        {
            var emitter = new PagePoseEmitter();
            Assert.That(Moved(emitter), Is.True);
            Assert.That(Moved(emitter), Is.False);
        }

        [TestCase(0.0004f, false)]
        [TestCase(0.0006f, true)]
        public void AHandMove_CountsPastHalfAMillimetre(float move, bool counts)
        {
            var emitter = new PagePoseEmitter();
            Moved(emitter);
            _right.transform.position += new Vector3(move, 0f, 0f);
            Assert.That(Moved(emitter), Is.EqualTo(counts));
        }

        [Test]
        public void AHeadTurn_Counts()
        {
            var emitter = new PagePoseEmitter();
            Moved(emitter);
            _head.transform.rotation = Quaternion.Euler(0f, 1f, 0f);
            Assert.That(Moved(emitter), Is.True);
        }

        [Test]
        public void SmallMoves_AreMeasuredFromTheLastPoseSent()
        {
            var emitter = new PagePoseEmitter();
            Moved(emitter);
            _left.transform.position += new Vector3(0f, 0.0003f, 0f);
            Assert.That(Moved(emitter), Is.False);
            _left.transform.position += new Vector3(0f, 0.0003f, 0f);
            Assert.That(Moved(emitter), Is.True);
        }

        [Test]
        public void Rate_IsThirtyHertz()
        {
            Assert.That(PagePoseEmitter.PosePublishHz, Is.EqualTo(30f));
        }

        bool Moved(PagePoseEmitter emitter) => emitter.PoseMoved(_head.transform, _left.transform, _right.transform);
    }
}
