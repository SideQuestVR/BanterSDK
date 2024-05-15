using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
namespace Banter{
    public class AframeCallback : AndroidJavaProxy{
        Action<string> callback;
        public AframeCallback() : base("quest.side.wtf.IAframeCallback") { }
        public void data(string data) {
            if(this.callback != null) {
                this.callback(data);
            }
        }

        public void SetCallback(Action<string> callback) {
            this.callback = callback;
        }
    }

}