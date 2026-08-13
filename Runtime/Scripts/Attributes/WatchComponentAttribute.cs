using System;

namespace Banter.SDK
{
    // Inherited = false so the deprecated Banter-prefixed subclasses are not picked up by the
    // generator as components in their own right, which would add bogus ComponentType members.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
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
