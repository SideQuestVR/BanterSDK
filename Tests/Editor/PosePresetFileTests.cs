using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class PosePresetFileTests
    {
        Scene scene;
        [SetUp] public void Setup() => scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void Cleanup() => EditorSceneManager.ClosePreviewScene(scene);

        T New<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go.AddComponent<T>();
        }

        static BSPointHandPose Hand(float x) => new BSPointHandPose
        {
            position = new Vector3(x, .02f, -.03f),
            rotation = new Vector3(10f, 20f, 30f),
            thumb = new Vector4(-.2f, .1f, .2f, .5f),
            index = new Vector4(.4f, .3f, .2f, .1f)
        };

        static BSPosePresetFile.HandPreset HandPreset(bool separate, float radius) => new BSPosePresetFile.HandPreset
        {
            name = "Test", separateLeft = separate, sourceGrabRadius = radius, right = Hand(.05f), left = Hand(-.07f)
        };

        static BSPosePresetFile.SeatPreset SeatPreset()
        {
            var pose = SeatPose.Default;
            pose.pelvisRotation = Quaternion.Euler(25f, 35f, 15f);
            pose.leftFootPosition = new Vector3(-.2f, -.3f, .6f);
            pose.rightKneeHint = new Vector3(.3f, .2f, .5f);
            return new BSPosePresetFile.SeatPreset { name = "Test", pose = pose };
        }

        static void SetRadius(BSGrabHandle handle, float radius)
        {
            var fields = new SerializedObject(handle);
            fields.FindProperty("grabRadius").floatValue = radius;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void SeatPreset_RoundTripsThroughFileText()
        {
            var saved = SeatPreset();
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(saved), out BSPosePresetFile.SeatPreset loaded, out var error), Is.True, error);
            Assert.That(loaded.name, Is.EqualTo("Test"));
            Assert.That(loaded.pose.leftFootPosition, Is.EqualTo(saved.pose.leftFootPosition));
            Assert.That(loaded.pose.rightKneeHint, Is.EqualTo(saved.pose.rightKneeHint));
            Assert.That(Quaternion.Angle(loaded.pose.pelvisRotation, saved.pose.pelvisRotation), Is.LessThan(.01f));
        }

        [TestCase(false)] [TestCase(true)]
        public void HandPreset_RoundTripsThroughComponents(bool separate)
        {
            var source = New<BSGrabHandle>();
            var pose = source.CustomPointPose;
            pose.enabled = true;
            pose.separateLeft = separate;
            pose.right = Hand(.05f);
            pose.left = Hand(-.07f);

            var json = BSPosePresetFile.ToJson(BSPosePresetFile.Capture(source, "Test"));
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.HandPreset loaded, out var error), Is.True, error);
            var target = New<BSGrabHandle>();
            target.CustomPointPose.enabled = true;
            BSPosePresetFile.Apply(target, loaded);

            var result = target.CustomPointPose;
            Assert.That(result.separateLeft, Is.EqualTo(separate));
            for (var i = 0; i < 2; i++)
            {
                var left = i == 1;
                Assert.That(Vector3.Distance(result.GetOffset(left).position, pose.GetOffset(left).position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(result.GetOffset(left).rotation, pose.GetOffset(left).rotation), Is.LessThan(.01f));
                for (var finger = 0; finger < 5; finger++)
                    Assert.That(result.GetHand(left).GetFinger(finger), Is.EqualTo(pose.GetHand(left).GetFinger(finger)));
            }
            Undo.ClearUndo(target);
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
        public void HandPreset_KeepsTheWristInPlaceOnAnotherGrabRadius(bool separate, bool left)
        {
            const float source = .03f, target = .08f;
            var handle = New<BSGrabHandle>();
            var collider = handle.gameObject.AddComponent<BoxCollider>();
            handle.CustomPointPose.enabled = true;
            SetRadius(handle, target);
            var preset = HandPreset(separate, source);
            var saved = new BSPointGrabPose { enabled = true, separateLeft = separate, right = preset.right.Copy(), left = preset.left.Copy() };

            BSPosePresetFile.Apply(handle, preset);

            using (var solver = new EditorGrabPosePreview(collider))
            {
                var approach = new Pose(new Vector3(.2f, 0f, 0f), Quaternion.identity);
                var before = solver.Evaluate(BSGrabType.Point, source, left, approach, saved);
                var after = solver.Evaluate(BSGrabType.Point, target, left, approach, handle.CustomPointPose);
                Assert.That(Vector3.Distance(before.position, after.position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(before.rotation, after.rotation), Is.LessThan(.01f));
            }
            Assert.That(new SerializedObject(handle).FindProperty("grabRadius").floatValue, Is.EqualTo(target));
            Undo.ClearUndo(handle);
        }

        [Test]
        public void HandPreset_ClampsHandEditedFingers()
        {
            var preset = HandPreset(false, .01f);
            preset.right.index = new Vector4(9f, -9f, 9f, 9f);
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(preset), out BSPosePresetFile.HandPreset loaded, out var error), Is.True, error);
            Assert.That(loaded.right.index, Is.EqualTo(new Vector4(100f / 170f, 0f, 100f / 170f, .5f)));
        }

        [Test]
        public void HandPreset_KeepsEveryValueTheControlsCanProduce()
        {
            var preset = HandPreset(false, .01f);
            preset.right.thumb = new Vector4(-60f / 170f, 65f / 170f, 65f / 170f, 60f / 50f);
            preset.right.little = new Vector4(100f / 170f, 100f / 170f, 100f / 170f, -25f / 50f);
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(preset), out BSPosePresetFile.HandPreset loaded, out var error), Is.True, error);
            Assert.That(loaded.right.thumb, Is.EqualTo(preset.right.thumb));
            Assert.That(loaded.right.little, Is.EqualTo(preset.right.little));
        }

        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{\"format\":\"something-else\",\"version\":1,\"kind\":\"seat\"}")]
        public void BadFiles_AreRefused(string json)
        {
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.SeatPreset seat, out var seatError), Is.False);
            Assert.That(seat, Is.Null);
            Assert.That(seatError, Is.Not.Empty);
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.HandPreset hand, out var handError), Is.False);
            Assert.That(hand, Is.Null);
            Assert.That(handError, Is.Not.Empty);
        }

        [Test]
        public void WrongKind_IsRefused()
        {
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(SeatPreset()), out BSPosePresetFile.HandPreset _, out var error), Is.False);
            Assert.That(error, Does.Contain("seat preset"));
        }

        [Test]
        public void NewerVersion_IsRefused()
        {
            var preset = SeatPreset();
            preset.version = 2;
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(preset), out BSPosePresetFile.SeatPreset _, out var error), Is.False);
            Assert.That(error, Does.Contain("newer"));
        }

        [TestCase("\"right\"")]
        [TestCase("\"thumb\"")]
        [TestCase("\"sourceGrabRadius\"")]
        public void IncompleteHandPreset_IsRefused(string field)
        {
            var json = BSPosePresetFile.ToJson(HandPreset(false, .01f)).Replace(field, "\"removed\"");
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.HandPreset preset, out _), Is.False);
            Assert.That(preset, Is.Null);
        }

        [Test]
        public void SeparateHandPreset_NeedsItsLeftHand()
        {
            var json = BSPosePresetFile.ToJson(HandPreset(true, .01f)).Replace("\"left\"", "\"removed\"");
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.HandPreset _, out _), Is.False);
        }

        [Test]
        public void IncompleteSeatPreset_IsRefusedInsteadOfBecomingTheDefault()
        {
            var json = BSPosePresetFile.ToJson(SeatPreset()).Replace("\"leftFootRotation\"", "\"removed\"");
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.SeatPreset preset, out var error), Is.False);
            Assert.That(preset, Is.Null);
            Assert.That(error, Does.Contain("incomplete"));
        }

        [Test]
        public void ZeroRotation_IsRefusedInsteadOfBecomingTheDefault()
        {
            var preset = SeatPreset();
            preset.pose.pelvisRotation = new Quaternion(0f, 0f, 0f, 0f);
            Assert.That(BSPosePresetFile.TryParse(BSPosePresetFile.ToJson(preset), out BSPosePresetFile.SeatPreset loaded, out var error), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(error, Does.Contain("invalid"));
        }

        [TestCase("1e999")] [TestCase("NaN")]
        public void NonFiniteHandValue_IsRefused(string value)
        {
            var preset = HandPreset(false, .01f);
            preset.right.index = new Vector4(.123456f, 0f, 0f, 0f);
            var json = BSPosePresetFile.ToJson(preset).Replace("0.123456", value);
            Assert.That(BSPosePresetFile.TryParse(json, out BSPosePresetFile.HandPreset loaded, out _), Is.False);
            Assert.That(loaded, Is.Null);
        }

        [Test]
        public void PartialVectors_AreRefused()
        {
            var seat = System.Text.RegularExpressions.Regex.Replace(BSPosePresetFile.ToJson(SeatPreset()),
                "\"pelvisRotation\":\\s*\\{[^}]*\\}", "\"pelvisRotation\": {\"x\": 0.2}");
            Assert.That(BSPosePresetFile.TryParse(seat, out BSPosePresetFile.SeatPreset _, out var error), Is.False);
            Assert.That(error, Does.Contain("incomplete"));

            var hand = System.Text.RegularExpressions.Regex.Replace(BSPosePresetFile.ToJson(HandPreset(false, .01f)),
                "\"thumb\":\\s*\\{[^}]*\\}", "\"thumb\": null");
            Assert.That(BSPosePresetFile.TryParse(hand, out BSPosePresetFile.HandPreset _, out error), Is.False);
            Assert.That(error, Does.Contain("incomplete"));
        }

        [TestCase("C:/Poses/Stool.json", "seat", "C:/Poses/Stool.seatpose.json")]
        [TestCase("C:/Poses/Stool.seatpose.json", "seat", "C:/Poses/Stool.seatpose.json")]
        [TestCase("C:/Poses/Mug.json", "hand", "C:/Poses/Mug.handpose.json")]
        public void SavedFiles_AlwaysGetTheSuffixTheFolderListNeeds(string typed, string kind, string expected)
        {
            Assert.That(BSPosePresetFile.WithSuffix(typed, kind), Is.EqualTo(expected));
        }

        [Test]
        public void Folder_ListsOnlyPresetsOfTheRequestedKind_ByName()
        {
            var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            try
            {
                foreach (var file in new[] { "Stool.seatpose.json", "armchair.seatpose.json", "Mug.handpose.json", "notes.json" })
                    File.WriteAllText(Path.Combine(folder, file), "{}");
                var seats = BSPosePresetFile.FindPresets(folder, BSPosePresetFile.SeatKind);
                Assert.That(seats, Has.Length.EqualTo(2));
                Assert.That(BSPosePresetFile.PresetName(seats[0], BSPosePresetFile.SeatKind), Is.EqualTo("armchair"));
                Assert.That(BSPosePresetFile.PresetName(seats[1], BSPosePresetFile.SeatKind), Is.EqualTo("Stool"));
                var hands = BSPosePresetFile.FindPresets(folder, BSPosePresetFile.HandKind);
                Assert.That(hands, Has.Length.EqualTo(1));
                Assert.That(BSPosePresetFile.PresetName(hands[0], BSPosePresetFile.HandKind), Is.EqualTo("Mug"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [TestCase("")] [TestCase(null)] [TestCase("Z:/this/folder/does/not/exist")]
        public void MissingFolder_ListsNothing(string folder)
        {
            Assert.That(BSPosePresetFile.FindPresets(folder, BSPosePresetFile.SeatKind), Is.Empty);
        }

        [Test]
        public void LoadingSeatPreset_SupportsUndoAndRedo()
        {
            var seat = New<BSSeat>();
            var before = seat.Pose;
            var preset = SeatPreset();
            Undo.IncrementCurrentGroup();
            BSPosePresetFile.Apply(seat, preset);
            Undo.FlushUndoRecordObjects();
            Assert.That(seat.Pose.leftFootPosition, Is.EqualTo(preset.pose.leftFootPosition));
            Undo.PerformUndo();
            Assert.That(seat.Pose.leftFootPosition, Is.EqualTo(before.leftFootPosition));
            Undo.PerformRedo();
            Assert.That(seat.Pose.leftFootPosition, Is.EqualTo(preset.pose.leftFootPosition));
            Undo.ClearUndo(seat);
        }

        [Test]
        public void LoadingHandPreset_SupportsUndoAndRedo()
        {
            var handle = New<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            var before = handle.CustomPointPose.right.index;
            var preset = HandPreset(true, new SerializedObject(handle).FindProperty("grabRadius").floatValue);
            Undo.IncrementCurrentGroup();
            BSPosePresetFile.Apply(handle, preset);
            Undo.FlushUndoRecordObjects();
            Assert.That(handle.CustomPointPose.separateLeft, Is.True);
            Undo.PerformUndo();
            Assert.That(handle.CustomPointPose.separateLeft, Is.False);
            Assert.That(handle.CustomPointPose.right.index, Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(handle.CustomPointPose.right.index, Is.EqualTo(preset.right.index));
            Undo.ClearUndo(handle);
        }
    }
}
