using System;
using System.Collections.Generic;

namespace Banter
{
    [Serializable]
    class ObjectUpdate
    {
        public int reqId;
        public string name;
        public string linkId;
        public int id;
        public int parent;
        public bool active;
    }
    [Serializable]
    class ObjectDeleted
    {
        public int reqId;
    }
    [Serializable]
    class ObjectReturn
    {
        public ObjectUpdate _object;
    }
    [Serializable]
    class ComponentUpdate
    {
        public int reqId;
        public int type;
        public string linkId;
        public int id;
        public int oid;
        public int parent;
        public bool active;
    }
    [Serializable]
    class ComponentReturn
    {
        public ComponentUpdate _component;
    }
}