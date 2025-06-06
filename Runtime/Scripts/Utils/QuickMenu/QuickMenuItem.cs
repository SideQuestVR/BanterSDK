using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [Serializable]
    public class QuickMenuItem : INotifyPropertyChanged
    {
        public enum RadialMenuItemType
        {
            Basic,
            Toggle,
            Slider
        }

        public RadialMenuItemType Type;
        public Texture2D Icon;
        public Sprite SpriteIcon;
        public string Label;
        public Texture2D Image;
        public AudioClip Sound;
        public string SceneVariableName;
        public Vector2 SliderMinMaxValue;
        public UnityEvent<QuickMenuItem> Click;
        public UnityEvent<QuickMenuItem, float> SliderValueChanged;
        public QuickMenuItem[] Children;
        public QuickMenuItem Parent;

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _isVisible = true;
        
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

        public void SetValueWithoutNotify(object obj)
        {
            _value = obj;
        }
    }
}