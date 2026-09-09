namespace BS
{
    /// <summary>
    /// The pure decision behind "this URL is not a world, take the player somewhere safe". Kept
    /// free of Unity state so it can be unit-tested; <c>BSScene</c> feeds it live values every frame
    /// while a load is waiting.
    ///
    /// Deliberately narrow: it only answers the SDK's own NOTHING scenario - the injected page
    /// script reporting that no objects have been created (<see cref="SceneState.NOTHING_20S"/>
    /// after 30 s, <see cref="SceneState.NOTHING"/> after 4:20). Every other way a load can go
    /// wrong - the browser failing to load the page, the page reporting a load failure, a load
    /// cancelled by the user or by the app - is left alone so it still surfaces as an error on
    /// the loading cage exactly as before. A page that reaches SCENE_READY with nothing in it is
    /// also left alone; the page said it was done.
    /// </summary>
    public static class MissingWorldFallback
    {
        /// <param name="enabled">Feature switch (<c>BSScene.EnableMissingWorldFallback</c>).</param>
        /// <param name="isFallbackWorld">The current load IS the fallback page - never fall back from the fallback.</param>
        /// <param name="hasRegisteredContent">Any object or component registered for this load. Content means it is a world, whatever the page reports.</param>
        /// <param name="loadFailed"><c>BSScene.HasLoadFailed()</c>. A failed or cancelled load keeps its failure screen.</param>
        /// <param name="state">Current <see cref="SceneState"/>.</param>
        public static bool ShouldFallBack(
            bool enabled,
            bool isFallbackWorld,
            bool hasRegisteredContent,
            bool loadFailed,
            SceneState state)
        {
            if (!enabled || isFallbackWorld || hasRegisteredContent || loadFailed)
            {
                return false;
            }
            return state == SceneState.NOTHING_20S || state == SceneState.NOTHING;
        }
    }
}
