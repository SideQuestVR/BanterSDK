using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [Serializable]
    public class QuickMenuItem : INotifyPropertyChanged {
        public enum RadialMenuItemType
        {
            Basic,
            Toggle
        }

        public RadialMenuItemType Type;
        public string Label;
        public Texture2D Icon;
        public Texture2D Image;
        public string SceneVariableName;
        public UnityEvent<QuickMenuItem> Click;
        public QuickMenuItem[] Children;
        public QuickMenuItem Parent;

        public object Value
        {
            get => _value;
            set 
            { 
                _value = value; 
                OnPropertyChanged(); 
            }
        }
    
        private object _value;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}