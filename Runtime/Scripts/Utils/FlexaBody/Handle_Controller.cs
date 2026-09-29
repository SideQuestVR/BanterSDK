// SDK port of FlexaBody's Handle_Controller, compiled when the FlexaBody package is not available.
// Same gun-trigger, button-edge and input-blocking logic as the client; BSHeldEvents configures it.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Custom HandleFunction that makes the GrabHandle act as an in-world Gamepad </summary>
    [AddComponentMenu("")]
    public class Handle_Controller : HandleFunction
    {
        public Controllable Controllable;

        [Header("Settings")]

        public float Sensitivity = 0.5f;
        public float FireTime = 0.1f;
        public bool AutoFire = false;
        public InputBlockList[] InputBlocks = { new InputBlockList(), new InputBlockList() };

        [Header("Outputs")]

        public bool IsActive = false;

        // Temp

        float _trigger = 0f;

        bool _leftPrimary = false;
        bool _leftSecondary = false;
        bool _leftThumbstickClick = false;

        bool _rightPrimary = false;
        bool _rightSecondary = false;
        bool _rightThumbstickClick = false;

        float _lastFireTime = 0f;


        public override void OnGrab(HandData handData)
        {
            base.OnGrab(handData);
            if (!Controllable)
                return;

            Controllable.handID = handData.HandID;
            SetInputBlocks(handData, true);

            try
            {
                Controllable.OnGrab();
            }
            catch (System.Exception e) { Debug.Log("OnGrab Failed: " + e); }
        }

        public override void OnHeld(HandData handData)
        {
            base.OnHeld(handData);
            if (!Controllable)
                return;

            Controllable.handID = handData.HandID;
            UpdateGunTrigger(handData);
            SetControls(handData);
        }

        public override void OnRelease(HandData handData)
        {
            base.OnRelease(handData);
            if (!Controllable)
                return;

            Controllable.handID = handData.HandID;
            try
            {
                Controllable.OnRelease();
            }
            catch (System.Exception e) { Debug.Log("OnRelease Failed: " + e); }

            SetInputBlocks(handData, false);
            ReleaseButtons(handData);
        }


        void SetInputBlocks(HandData handData, bool block)
        {
            bool trigger = false, stick = false, click = false, primary = false, secondary = false;

            if(block)
            {
                InputBlockList blockList = InputBlocks[handData.HandID == HandID.Left ? 0 : 1];
                trigger = blockList.Trigger;
                stick = blockList.Thumbstick;
                click = blockList.ThumbstickClick;
                primary = blockList.PrimaryButton;
                secondary = blockList.SecondaryButton;
            }

            if (handData.HandID == HandID.Left)
            {
                ActionsSystem.Blocker_LeftTrigger.Global = trigger;
                ActionsSystem.Blocker_LeftThumbstick.Global = stick;
                ActionsSystem.Blocker_LeftThumbstickClick.Global = click;
                ActionsSystem.Blocker_LeftPrimary.Global = primary;
                ActionsSystem.Blocker_LeftSecondary.Global = secondary;
            }
            else
            {
                ActionsSystem.Blocker_RightTrigger.Global = trigger;
                ActionsSystem.Blocker_RightThumbstick.Global = stick;
                ActionsSystem.Blocker_RightThumbstickClick.Global = click;
                ActionsSystem.Blocker_RightPrimary.Global = primary;
                ActionsSystem.Blocker_RightSecondary.Global = secondary;
            }
        }

        void UpdateGunTrigger(HandData handData)
        {
            if (AutoFire)
            {
                _trigger = handData.Input_Trigger;
                IsActive = _trigger > Sensitivity;
            }
            else
            {
                if (_trigger < Sensitivity && handData.Input_Trigger > Sensitivity)
                    IsActive = true;
                else
                    IsActive = false;

                _trigger = handData.Input_Trigger;
            }

            if (IsActive && _lastFireTime < Time.time)
            {
                try
                {
                    Controllable.OnGunTrigger();
                }
                catch (System.Exception e) { Debug.Log("UpdateGunTrigger Failed: " + e); }

                _lastFireTime = Time.time + FireTime;
            }
        }

        void SetControls(HandData handData)
        {
            try
            {
                Controllable.OnThumbstick(handData.Input_Joystick);
                Controllable.OnTrigger(handData.Input_Trigger);
            }
            catch (System.Exception e) { Debug.Log("SetControls Failed: " + e); }

            SetButtons(handData);
        }

        void SetButtons(HandData handData)
        {
            if (handData.HandID == HandID.Left)
            {
                SetButton(handData.Input_Primary, ref _leftPrimary, 0);
                SetButton(handData.Input_Secondary, ref _leftSecondary, 1);
                SetButton(handData.Input_ThumbClick, ref _leftThumbstickClick, 2);
            }
            else
            {
                SetButton(handData.Input_Primary, ref _rightPrimary, 0);
                SetButton(handData.Input_Secondary, ref _rightSecondary, 1);
                SetButton(handData.Input_ThumbClick, ref _rightThumbstickClick, 2);
            }
        }

        void SetButton(bool input, ref bool output, int setId)
        {
            if (input != output)
            {
                output = input;
                if (input)
                    Set(setId, true);
                else
                    Set(setId, false);
            }

            void Set(int id, bool down)
            {
                try
                {
                    switch (id)
                    {
                        case 0: if (down) Controllable.OnPrimaryDown(); else Controllable.OnPrimaryUp(); break;
                        case 1: if (down) Controllable.OnSecondaryDown(); else Controllable.OnSecondaryUp(); break;
                        case 2: if (down) Controllable.OnThumbClickDown(); else Controllable.OnThumbClickUp(); break;
                    }
                }
                catch (System.Exception e) { Debug.Log("SetButton Failed: " + e); }
            }
        }

        void ReleaseButtons(HandData handData)
        {
            if (handData.HandID == HandID.Left)
            {
                try
                {
                    if (_leftPrimary) Controllable.OnPrimaryUp();
                    if (_leftSecondary) Controllable.OnSecondaryUp();
                    if (_leftThumbstickClick) Controllable.OnThumbClickUp();
                }
                catch (System.Exception e) { Debug.Log("ReleaseButtons Failed: " + e); }

                _leftPrimary = false;
                _leftSecondary = false;
                _leftThumbstickClick = false;
            }
            else
            {
                try
                {
                    if (_rightPrimary) Controllable.OnPrimaryUp();
                    if (_rightSecondary) Controllable.OnSecondaryUp();
                    if (_rightThumbstickClick) Controllable.OnThumbClickUp();
                }
                catch (System.Exception e) { Debug.Log("ReleaseButtons Failed: " + e); }

                _rightPrimary = false;
                _rightSecondary = false;
                _rightThumbstickClick = false;
            }

            _trigger = 0f;
            IsActive = false;
        }
    }
}

#endif
