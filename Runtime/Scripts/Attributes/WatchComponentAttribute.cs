using System;

namespace Banter.SDK
{
    public class WatchComponentAttribute : Attribute
    {
        public Type m_Type;

        public WatchComponentAttribute() { }

        public WatchComponentAttribute(Type requiredComponent)
        {
            m_Type = requiredComponent;
        }
    }
}
