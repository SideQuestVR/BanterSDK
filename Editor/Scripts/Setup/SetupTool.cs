namespace BS.SDKEditor.Setup
{
    /// <summary>
    /// One row in the Setup panel's Tools section: a window to open, or an action that's run now and then.
    /// The Creator SDK menu only has Setup and Builder, so this is where everything else lives. Like
    /// <see cref="SetupCheck"/>, subclasses with a parameterless constructor are found by type, in any editor assembly.
    /// </summary>
    public abstract class SetupTool
    {
        public abstract string Title { get; }

        /// <summary>One or two sentences under the title: what it's for.</summary>
        public abstract string Description { get; }

        /// <summary>Rows are listed in ascending order.</summary>
        public virtual int Order => 100;

        public virtual string ButtonLabel => "Open";

        /// <summary>Whether the button works right now. When it doesn't, <see cref="UnavailableReason"/> says why.</summary>
        public virtual bool Available => true;

        public virtual string UnavailableReason => null;

        public abstract void Run();
    }
}
