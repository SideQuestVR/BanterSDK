using System.Threading.Tasks;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Optional replacement for the loading cage's visuals.
    ///
    /// When an app assigns a presenter with <see cref="LoadingBarManager.SetPresenter"/>, the manager keeps its
    /// state machine, colliders, events (onLoadOutStarted etc.) and public API exactly as they are, hides the
    /// legacy cage renderers, and hands every visual step to the presenter. With no presenter assigned the
    /// manager behaves exactly as before, so creators and other apps are unaffected.
    ///
    /// All calls arrive on the main thread.
    /// </summary>
    public interface ILoadingPresenter
    {
        /// <summary>App start: be fully opaque now, so the empty startup scene is never visible.</summary>
        void ShowOpaqueImmediately();

        /// <summary>A new load is about to start: reset progress, thumbnail and failure state.</summary>
        void Preload();

        /// <summary>Close over the current world. The task completes once fully opaque.</summary>
        Task LoadIn();

        /// <summary>
        /// Open onto the loaded world. Called right after onLoadOutStarted has fired (spawn teleport and physics
        /// unfreeze have happened), so anything the presenter shows before revealing hides that settle.
        /// The task completes once fully open.
        /// </summary>
        Task LoadOut();

        /// <summary>Progress 0-1 (0 while nothing has registered yet) and the texts the cage would show.</summary>
        void SetProgress(string title, float percentage, string detail, bool canCancel);

        /// <summary>The space thumbnail. Owned by Get's texture cache: do not destroy it.</summary>
        void SetThumbnail(Texture2D image);

        /// <summary>Load failed or player kicked (true), or back to normal (false), with the hint the cage shows.</summary>
        void SetFailed(bool failed, string hint);
    }
}
