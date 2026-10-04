# Snippets (BSSnippet)

A snippet is a ready-made feature — a video player, a leaderboard, a jukebox — that you drop into a
world without writing any JavaScript. Each one is a small piece of HTML hosted on altvr.app that
names a script (or an asset bundle) plus the settings it accepts. You add it in Unity by typing its
slug; the settings then appear in the Inspector as ordinary fields, and some of them can be dragged
around in the Scene view.

There are two halves, and they meet in `Assets/WebRoot/index.html`:

- **`BSSnippet`**, a Unity Editor component. It fetches the snippet, writes a `<bs-snippet>` element
  into your `index.html`, and gives you an inspector and gizmos for editing that element's
  attributes. It is an authoring-time component only — it is never part of the built world and is
  invisible to the JS API.
- **`<bs-snippet>`**, a custom element in the runtime. When the space loads, it reads its own
  attributes and loads the snippet's payload. This is what actually does the work in-world.

Because the element in `index.html` is the real artifact, a snippet keeps working even if the
`BSSnippet` component is later removed — and equally, you can hand-write a `<bs-snippet>` element
yourself and skip Unity entirely.

## Adding a Snippet

1. Select the GameObject that should own the snippet. Its transform is the reference frame for the
   snippet's gizmos, so put it where the feature belongs in the world.
2. Click **Add Component** and search for *Snippet*.
3. Type the snippet's slug (for example `video-player`) into the **Slug** field and press Enter.

The snippet is fetched from `https://altvr.app/api/snippets/<slug>`, given a unique `instance` id,
and appended to the bottom of `<body>` in `Assets/WebRoot/index.html` inside a marked section. The
Inspector fills in with the snippet's settings.

Snippets are fetched **once**. After that the copy in `index.html` is the source of truth, so your
edits are never overwritten by a later fetch and the world builds fine offline. Use **Refresh from
server** when you want the current definition back (it discards this instance's local edits).

The same snippet can be placed as many times as you like — each `BSSnippet` component owns exactly
one element, paired by the `instance` attribute. Duplicating a GameObject (Ctrl+D) copies the
element locally, including any settings you had changed, without going back to the server.

## The Inspector

| Row | Meaning |
|-----|---------|
| Slug | The snippet to use. Changing it fetches the new snippet and replaces the element, keeping the same instance id. |
| Status | Fetch progress, or an error (unknown slug, network failure, malformed snippet). Errors never throw — the world still builds. |
| Title / description | From the snippet definition, read-only. Description is optional; when the snippet has none the row shows a dim "(no description)". |
| name / instance | The snippet's id and this instance's unique id, for cross-referencing `index.html`. |
| Settings | One field per attribute on the element. |
| Refresh from server | Re-fetches the definition (asks first — local edits to this instance are lost). |
| Open index.html | Opens the file in your default editor. |

Field types are inferred from each value's current shape, so a snippet needs no schema:

| Value looks like | Field |
|------------------|-------|
| `true` / `false` | Toggle |
| `1.6` | Float |
| `1.2 1` | Vector2 |
| `0 1.5 0` | Vector3 |
| `0 0 0 1` | Vector4 |
| anything else | Text |

Edits are written to `index.html` as you make them. `script` and `asset` are editable like any other
setting — handy for pointing a snippet at a local build — while `name`, `title`, `description` and
`instance` are managed for you and not shown as fields.

## Gizmos

A snippet can declare `<bs-gizmo>` children that draw in the Scene view while the object carrying
the component is selected. They exist to make invisible settings visible: where a screen will hang,
how big a panel is, which way it faces.

```html
<bs-gizmo type="position" attribute="position"/>
<bs-gizmo type="plane" attribute="position" size="1.6 0.9" rotation="0 0 0"/>
```

| `type` | Draws | Sized by |
|--------|-------|----------|
| `position` | A drag handle. Moving it writes the new value straight back into the bound attribute. | — |
| `plane` | A filled rectangle in local XY, facing +Z. | `size="width height"` (default `1 1`) |
| `box` | A wireframe box. | `size="x y z"` (default `1 1 1`) |
| `sphere` | Three wireframe circles. | `radius="r"` (default `0.5`) |

| Attribute | Applies to | Meaning |
|-----------|------------|---------|
| `attribute` | all | Names a snippet setting holding an `"x y z"` value; the gizmo is drawn there. **Required** for `position` — that setting is what the handle edits. |
| `position` | all | A fixed local offset, used when `attribute` is absent or unparseable. |
| `rotation` | `plane`, `box`, `sphere` | Euler angles in degrees. |

All values are in the owning GameObject's local space. Dragging a `position` handle updates the
Inspector field live, and vice versa. A gizmo with an unknown type, or a `position` gizmo with no
`attribute`, is skipped with one console warning.

## How the HTML Stays in Sync

Edits flow both ways, so you can work in whichever tool suits the moment:

- **Inspector or gizmo → file.** Writes are debounced (about ¾ of a second) so dragging a handle
  doesn't hammer the disk, and flushed on domain reload, on quitting, and when the Inspector closes.
- **File → Inspector.** Editing `index.html` in an external editor updates the Inspector, both when
  Unity regains focus and, while a snippet Inspector is open, within about a second.

Only the marked section is ever rewritten; everything else in the file is preserved byte for byte,
including comments you leave inside the section. If the section is ever left unparseable, the SDK
reports it and refuses to write anything at all until it is fixed, so a half-finished hand edit
cannot be clobbered.

Component fields (slug and the cached title/description) participate in Undo. The contents of
`index.html` do not — it is an ordinary text file, and pretending otherwise would desynchronise the
moment you edited it outside Unity. Undo still behaves sensibly at the seams: undoing a component
deletion or a slug change restores the element, with your edited values intact.

## The Snippet Section in index.html

Elements live between two markers, which the SDK creates the first time it needs them:

```html
<body>
  <!-- snippet section -->
  <bs-snippet name="video-player" instance="a1b2c3…" title="Video Player"
    description="Synced player for YouTube, Twitch and direct video links."
    script="https://example.com/video-player.js"
    position="0 1.5 0" width="1.6" volume="80" autoplay="true">
    <bs-gizmo type="position" attribute="position"/>
    <bs-gizmo type="plane" attribute="position" size="1.6 0.9"/>
  </bs-snippet>
  <!-- end snippet section -->
</body>
```

| Attribute | Required | Meaning |
|-----------|----------|---------|
| `name` | yes | The snippet's id — the same slug you typed in Unity. |
| `title` | yes | Human-readable name, shown in the Inspector. |
| `description` | no | What the snippet does; shown in the Inspector. |
| `script` | either | URL of the snippet's JavaScript. |
| `asset` | either | URL of an asset bundle; the first prefab in it is instantiated. |
| `single` | no | `single="true"` allows only one of this snippet per world; further copies are ignored at runtime. Set by the snippet author, so it is not an editable field. |
| `instance` | added by Unity | Pairs the element with one `BSSnippet` component. Hand-written elements can omit it. |
| anything else | no | The snippet's own settings, editable in the Inspector. |

`position`, `rotation` and `scale` are plain Unity local values in the object's own space, written as
space-separated numbers (`scale` also accepts a single number for uniform scale).

A snippet must have `script` or `asset`. If it somehow has both, `script` wins and the runtime logs
a warning.

The element names are hyphenated (`bs-snippet`, `bs-gizmo`) because the HTML custom-element standard
requires a hyphen in the name — a bare `<snippet>` cannot be registered as a custom element.

## Runtime Behaviour

`<bs-snippet>` is a real custom element, so it works the same whether it was parsed from
`index.html` at load or created on the fly:

```js
const el = document.createElement("bs-snippet");
el.setAttribute("name", "video-player");
el.setAttribute("script", "https://example.com/video-player.js");
el.setAttribute("position", "0 1.5 0");
document.body.appendChild(el); // loads immediately
```

**Script snippets** get their JavaScript added to the page as a `<script src>` tag. Each distinct URL
is loaded exactly once no matter how many instances share it — the script serves them all — and a
script that fails to load logs an error without disturbing anything else.

**Asset snippets** create a GameObject named after the snippet, apply the `position`, `rotation` and
`scale` attributes to it, load the bundle, and instantiate the first prefab it contains. Removing the
element from the DOM destroys that object. (Script snippets can't be unloaded — JavaScript, once run,
stays run — so removing one of those elements is up to the snippet to notice.)

**Single-instance snippets** declare `single="true"`, for features where a second copy would do
harm rather than good — a synced video player whose copies would fight over one playback channel, a
world leaderboard, anything owning a shared resource. The first element to load claims the snippet's
name; every later copy is skipped and logs a warning naming the snippet. Copies are left alone
otherwise: the element stays in `index.html`, the component keeps its settings and gizmos, and if
the first copy is ever removed the name is released for another to claim. The Inspector flags the
surplus copies so this is visible while authoring, rather than only in-world.

## Authoring a Snippet

A snippet script runs once per page and serves every instance of itself, so it should find its own
elements and set each one up:

```js
window.addEventListener("bs-loaded", () => {
    document.querySelectorAll('bs-snippet[name="video-player"]').forEach(el => {
        const scene = BS.Scene.GetInstance();
        const width = parseFloat(el.getAttribute("width") || "1.6");
        const [x, y, z] = (el.getAttribute("position") || "0 0 0").split(/\s+/).map(Number);
        // ...build the feature at that position, at that size
    });
});
```

`bs-loaded` is latched, so a snippet script that arrives after the SDK has started still gets the
callback immediately — load order is never a race.

Design guidance:

- Give every setting a sensible default and read it with a fallback. Creators will delete values.
- Keep settings flat and stringy: numbers, `"x y z"` triples, `true`/`false`, comma-separated lists.
  That is what makes the Inspector able to show good fields without a schema.
- Add a `<bs-gizmo>` for anything positional. A creator who can see the screen rectangle will place
  it correctly the first time.
- Treat attribute values as untrusted creator input — validate before use.
- Declare `single="true"` if two placements would fight over one resource. It is far kinder than
  letting a creator discover the clash in-world.

## Housekeeping & Gotchas

- **Removing the component removes its element.** Closing a scene does not: elements owned by scenes
  that are merely unloaded are left alone, because they still belong to that scene. If elements do
  get stranded (an object deleted while scripts were reloading, say), `Creator SDK > Snippets > Recover
  Orphaned Snippets...` lists the ones no scene or prefab uses. **Add to Scene** gives each a new object
  at the origin, linked to its element, so every setting you made is kept; **Remove** deletes them from
  `index.html`. Elements a closed scene or a prefab still uses are listed but left alone.
- **`index.html` is a project file.** It ships with your world and belongs in version control; the
  snippet section is a normal part of its diff.
- **Hand-written elements are first-class.** An element with no `instance` attribute is never touched
  by the Unity side — useful for pasting a local copy of a snippet definition to test against without
  a round trip. A new `BSSnippet` whose slug matches an existing element clones that element instead
  of fetching, so a local copy is what gets used.
- **A failed fetch does not retry in a loop.** The error stays on the component until you change the
  slug or press Refresh.
