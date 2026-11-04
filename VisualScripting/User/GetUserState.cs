using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Data.Common;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Get User State")]
    [UnitShortTitle("User State")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUserState : Unit
    {
        [DoNotSerialize]
        public ValueInput idOrName;

        [DoNotSerialize]
        public ValueOutput userPosition;

        [DoNotSerialize]
        public ValueOutput userRotation;

        protected override void Definition()
        {
            idOrName = ValueInput("id, uid, or name", string.Empty);

            // Zero/identity for a user who isn't there (yet), as Get Local User State does.
            userPosition = ValueOutput("Position", (f) => { var t = GetUserDataTransform(f); return t != null ? t.position : Vector3.zero; });
            userRotation = ValueOutput("Rotation", (f) => { var t = GetUserDataTransform(f); return t != null ? t.rotation : Quaternion.identity; });
        }

        // Names already reported, so a node pulled every frame logs a missing user once, not 72 times a second.
        [DoNotSerialize]
        readonly System.Collections.Generic.HashSet<string> reportedMissing = new System.Collections.Generic.HashSet<string>();

        private Transform GetUserDataTransform(Flow f)
        {
            var value = f.GetValue<string>(idOrName);
            var data = BSScene.Instance().users.FirstOrDefault(user => user.id == value || user.name == value || user.uid == value);

            if (data == null)
            {
                if (reportedMissing.Add(value ?? "")) LogLine.Err("User not found: " + value);
                return null;
            }
            reportedMissing.Remove(value ?? "");
            return data.Head; // null until the user's body exists
        }
    }

    [UnitTitle("Get Local User State")]
    [UnitShortTitle("Local User State")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetLocalUserState : Unit
    {
        [DoNotSerialize]
        public ValueOutput userPosition;

        [DoNotSerialize]
        public ValueOutput userRotation;

        protected override void Definition()
        {
            userPosition = ValueOutput("Position", (f) =>
            {
                var data = BSScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return data != null && data.Head != null ? data.Head.position : Vector3.zero;
            });

            userRotation = ValueOutput("Rotation", (f) =>
            {
                var data = BSScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return data != null && data.Head != null ? data.Head.rotation : Quaternion.identity;
            });
        }
    }

    [UnitTitle("Get User Saved Value")]
    [UnitShortTitle("GetUserSavedValue")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUserSavedValue : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput key;
        [DoNotSerialize]
        public ValueInput uid;


        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnGetUserState.Invoke(_key, _uid);
                }, $"{nameof(GetUserSavedValue)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);

            uid = ValueInput("UserId Or Me", "me");
        }

    }


    [UnitTitle("Set User Saved Value")]
    [UnitShortTitle("SetUserSavedValue")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetUserSavedValue : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput key;
        [DoNotSerialize]
        public ValueInput uid;
        [DoNotSerialize]
        public ValueInput value;


        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                var _value = flow.GetValue<string>(value);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnSetUserState.Invoke(_key, _uid, _value);
                }, $"{nameof(SetUserSavedValue)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);

            uid = ValueInput("UserId Or Me", "me");

            value = ValueInput("Value", string.Empty);
        }

    }

    [UnitTitle("Remove User Saved Value")]
    [UnitShortTitle("RemoveUserSavedValue")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class RemoveUserSavedValue : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput key;
        [DoNotSerialize]
        public ValueInput uid;


        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnRemoveUserState.Invoke(_key, _uid);
                }, $"{nameof(RemoveUserSavedValue)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);

            uid = ValueInput("UserId Or Me", "me");
        }

    }
}
