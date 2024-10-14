
#if BANTER_VISUAL_SCRIPTING
using Banter.SDK;
using Unity.VisualScripting;
using Unity.XR.OpenVR;
using UnityEngine.InputSystem.XR;
using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System;
using UnityEngine.XR.OpenXR.Input;



namespace Banter.VisualScripting
{
    [UnitTitle("Vibrate")]
    [UnitShortTitle("Vibrate XR Controller")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class VibrateController : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [SerializeField] private InputDevice x;


        [DoNotSerialize]
        public ValueInput hand;




        void Start()
        {   
            
           
        }
        protected override void Definition()
        { 
            inputTrigger = ControlInput("", (flow) => {
               var side = flow.GetValue<bool>(hand);
               List<UnityEngine.XR.InputDevice> devices = new List<UnityEngine.XR.InputDevice>();
                InputDevices.GetDevices(devices);
               Debug.Log(side);
               
                if(side)
                {
                    devices[1].SendHapticImpulse(0, .5f, .1f);
                }
                else
                {
                    devices[2].SendHapticImpulse(0, .5f, .1f);
                }
              
                  
               
                


                return outputTrigger;
            });
            hand = ValueInput("Left", false);

        }
     
    }
}
#endif

//namespace Banter.VisualScripting
//{

//    [UnitTitle("Vibrate")]
//    [UnitShortTitle("Vibrate XR Controller")]
//    [UnitCategory("Banter")]
//    [TypeIcon(typeof(BanterObjectId))]
//    public class VibrateController : Unit
//    {
//        [SerializeField] private InputActionReference _rightHandHapticAction;
//        [DoNotSerialize]
//        public ControlInput inputTrigger;

//        [DoNotSerialize]
//        public ControlOutput outputTrigger;








//        protected override void Definition()
//        {
//            inputTrigger = ControlInput("Vibes", (flow) => {

//                //x = devices[0];


//                OpenXRInput.SendHapticImpulse(_rightHandHapticAction, 1f, 0.5f, UnityEngine.InputSystem.XR.XRController.rightHand);
//                return outputTrigger;
//            });


//        }
//    }
//}