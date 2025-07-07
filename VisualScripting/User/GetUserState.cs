#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Data.Common;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Get User State")]
    [UnitShortTitle("User State")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
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

            userPosition = ValueOutput("Position", (f) => GetUserDataTransform(f).position);
            userRotation = ValueOutput("Rotation", (f) => GetUserDataTransform(f).rotation);
        }

        private Transform GetUserDataTransform(Flow f)
        {
            var value = f.GetValue<string>(idOrName);
            var data = BanterScene.Instance().users.FirstOrDefault(user => user.id == value || user.name == value || user.uid == value);

            if (data == null)
            {
                LogLine.Err("User not found: " + value);
            }
            return (data != null) ? data.Head.transform : null;
        }
    }

    [UnitTitle("Get Local User State")]
    [UnitShortTitle("Local User State")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
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
                var data = BanterScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return (data != null) ? data.Head.transform.position : Vector3.zero;
            });

            userRotation = ValueOutput("Rotation", (f) =>
            {
                var data = BanterScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return (data != null) ? data.Head.transform.rotation : Quaternion.identity;
            });
        }
    }

    [UnitTitle("Get User Saved Value")]
    [UnitShortTitle("GetUserSavedValue")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
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
             inputTrigger = ControlInput("", (flow) => {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    BanterScene.Instance().events.OnGetUserState.Invoke(_key, _uid);
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
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
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
             inputTrigger = ControlInput("", (flow) => {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                var _value = flow.GetValue<string>(value);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    BanterScene.Instance().events.OnSetUserState.Invoke(_key, _uid, _value);
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
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
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
             inputTrigger = ControlInput("", (flow) => {
                var _key = flow.GetValue<string>(key);
                var _uid = flow.GetValue<string>(uid);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    BanterScene.Instance().events.OnRemoveUserState.Invoke(_key, _uid);
                }, $"{nameof(RemoveUserSavedValue)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);

            uid = ValueInput("UserId Or Me", "me");
        }

    }
}
#endif
