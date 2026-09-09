namespace BS
{
    /// <summary>
    /// Why (or whether) a load should be abandoned in favour of the missing-world fallback.
    /// Ordered: everything above <see cref="PendingGrace"/> is a decision to fall back.
    /// </summary>
    public enum MissingWorldReason
    {
        /// <summary>Keep going — the load is healthy, finished, disabled, or is the fallback itself.</summary>
        None,
        /// <summary>The load failed with nothing registered, but the post-Cancel grace has not elapsed. Keep waiting; this WILL become <see cref="LoadFailed"/>.</summary>
        PendingGrace,
        /// <summary>The page failed to load (browser error, JS gave up) and registered nothing.</summary>
        LoadFailed,
        /// <summary>The page declared itself ready and the empty-scene grace ran out with nothing registered.</summary>
        LoadedEmpty,
        /// <summary>The page never even constructed the SDK scene (404, plain web page) within the timeout.</summary>
        NoSceneScriptTimeout,
    }

    /// <summary>
    /// The pure decision behind "this URL is not a world, take the player somewhere safe". Kept
    /// free of Unity state so it can be unit-tested; <c>BSScene</c> feeds it live values every frame
    /// while a load is waiting.
    /// </summary>
    public static class MissingWorldFallback
    {
        /// <summary>
        /// How long to sit on a failed load before falling back. When the browser reports a
        /// navigation failure, OraView redirects itself to its error page in the same event
        /// dispatch; issuing our own navigation a beat later guarantees ours is the last LoadUrl
        /// the browser sees, so the fallback page cannot be replaced by error.html.
        /// </summary>
        public const float CancelGraceSeconds = 1f;

        public static bool IsTriggered(MissingWorldReason reason)
        {
            return reason > MissingWorldReason.PendingGrace;
        }

        /// <param name="enabled">Feature switch (<c>BSScene.EnableMissingWorldFallback</c>).</param>
        /// <param name="isFallbackWorld">The current load IS the fallback page — never fall back from the fallback.</param>
        /// <param name="hasRegisteredContent">Any object or component registered for this load. Content means it is a world, whatever else happens.</param>
        /// <param name="loadFailed"><c>BSScene.HasLoadFailed()</c>. Checked before <paramref name="loaded"/> because Cancel() also sets loaded.</param>
        /// <param name="lastCancelWasUser">A deliberate user cancel is never redirected.</param>
        /// <param name="secondsSinceCancel">Real seconds since Cancel(); pass +infinity when unknown.</param>
        /// <param name="loaded"><c>BSScene.loaded</c> — for an empty scene this only turns true after the empty-scene grace.</param>
        /// <param name="fallbackOnEmptyScene">Whether a ready-but-empty page counts as missing.</param>
        /// <param name="state">Current <see cref="SceneState"/>. Anything at or past SCENE_START proves the page runs the SDK and is exempt from the timeout.</param>
        /// <param name="elapsedSeconds">Real seconds since the navigation was issued.</param>
        /// <param name="timeoutSeconds">How long a page may go without constructing the SDK scene.</param>
        public static MissingWorldReason Evaluate(
            bool enabled,
            bool isFallbackWorld,
            bool hasRegisteredContent,
            bool loadFailed,
            bool lastCancelWasUser,
            float secondsSinceCancel,
            bool loaded,
            bool fallbackOnEmptyScene,
            SceneState state,
            float elapsedSeconds,
            float timeoutSeconds)
        {
            if (!enabled || isFallbackWorld || hasRegisteredContent)
            {
                return MissingWorldReason.None;
            }
            if (loadFailed)
            {
                if (lastCancelWasUser)
                {
                    return MissingWorldReason.None;
                }
                return secondsSinceCancel >= CancelGraceSeconds
                    ? MissingWorldReason.LoadFailed
                    : MissingWorldReason.PendingGrace;
            }
            if (loaded)
            {
                return fallbackOnEmptyScene ? MissingWorldReason.LoadedEmpty : MissingWorldReason.None;
            }
            if (elapsedSeconds >= timeoutSeconds && state < SceneState.SCENE_START)
            {
                return MissingWorldReason.NoSceneScriptTimeout;
            }
            return MissingWorldReason.None;
        }
    }
}
