namespace Banter.SDK
{
    public static class UICommands
    {
        // Element lifecycle
        public const string CREATE_UI_ELEMENT = "!cui!";
        public const string DESTROY_UI_ELEMENT = "!dui!";
        public const string ATTACH_UI_CHILD = "!auc!";
        public const string DETACH_UI_CHILD = "!duc!";
        
        // Properties and styles
        public const string SET_UI_PROPERTY = "!sup!";
        public const string GET_UI_PROPERTY = "!gup!";
        public const string SET_UI_STYLE = "!sus!";
        public const string GET_UI_STYLE = "!gus!";
        public const string BATCH_UI_UPDATE = "!buu!";
        
        // Events
        public const string UI_EVENT = "!uie!";
        public const string REGISTER_UI_EVENT = "!rue!";
        public const string UNREGISTER_UI_EVENT = "!uue!";
        
        // Methods
        public const string CALL_UI_METHOD = "!cum!";
        public const string UI_METHOD_RETURN = "!umr!";
        
        // Templates
        public const string INSTANTIATE_UXML = "!iux!";
        public const string QUERY_UI_ELEMENT = "!que!";
        public const string GET_UI_SLOT = "!gsl!";
        
        // Data binding
        public const string BIND_UI_DATA = "!bud!";
        public const string UNBIND_UI_DATA = "!ubd!";
        public const string UPDATE_UI_BINDING = "!uub!";
        
        // Hierarchy
        public const string SET_UI_PARENT = "!spr!";
        public const string GET_UI_CHILDREN = "!gch!";
        public const string SET_UI_VISIBLE = "!svs!";
        public const string SET_UI_ENABLED = "!sen!";
        
        // Focus and input
        public const string SET_UI_FOCUS = "!sfc!";
        public const string CLEAR_UI_FOCUS = "!cfc!";
        
        // Layout
        public const string FORCE_UI_LAYOUT = "!ful!";
        public const string MEASURE_UI_ELEMENT = "!mue!";
    }
}