# The BS Browser

A BS Browser shows a web page on a flat panel in your world. It's a full Chromium page: players click, scroll and type into it, its sound plays from where it hangs, and it can talk to your space.

## Adding one

In Unity, use **GameObject > BS > Objects > Browser**, or **Add Component > BS Browser** on any object. From your space's script, add `BS.Browser` to an object:

<div class="docs-tabs">

```js
const screen = new BS.GameObject({ name: "Screen", localPosition: new BS.Vector3(0, 1.5, 2) });
const browser = screen.AddComponent(new BS.Browser({
    url: "https://example.com",
    pageWidth: 1280,
    pageHeight: 720
}));
```

![BS Browser in the Unity Inspector](../images/components/browser.png)

</div>

## Settings

| Setting | Default (Unity / script) | What it does |
|---|---|---|
| `url` | empty / empty | The page to show, as a full address (`https://…`). Changing it loads the new page, and setting the same address again reloads it. A page that fails to load is replaced by an error page. The **GameObject > BS** browser starts on `https://sidequestvr.com`. |
| `pageWidth`, `pageHeight` | 1280 × 720 / 1024 × 576 | The page's size in pixels, which also sets how big the browser is in the world. Change them any time. |
| `actions` | empty | [Actions](#actions) to run once, when the browser starts. |
| `mipMaps`, `pixelsPerUnit` | | Not used. |

## Size and placement

The page is drawn at **1300 pixels per metre**, centred on the object:

| Page size | In the world |
|---|---|
| 1024 × 576 | 0.79 × 0.44 m |
| 1280 × 720 | 0.98 × 0.55 m |
| 1920 × 1080 | 1.48 × 0.83 m |

- **It's seen from the object's back (−Z)**, the opposite of Unity's blue axis. Point the blue axis away from where players stand. In the Scene view a blue outline shows the panel, with an arrow on the side it's seen from.
- **Scale the object** to make the panel bigger or smaller without changing the page; the page's text and layout stay the same.
- **Change the page size** to give the page more room: a wider page fits more content, at the same pixels per metre.

## Interaction

- **Pointing:** players click (left button) and scroll the page. Hovering works too, so menus and tooltips behave as they do on a desktop.
- **Typing** goes to the page whose text field has focus. In SDK Play Mode, type on your keyboard (not while flying).
- **View-only:** `browser.ToggleInteraction(false)` stops clicks and scrolling from reaching the page; `true` turns them back on.
- **One window:** popups and new windows (`window.open`, links with `target="_blank"`) are blocked, so keep links in the same page.
- **Sound** plays from the browser's position, as 3D sound.

A BS Browser page is an ordinary web page: it doesn't get your space's `window.BS`. To work with your space, it sends and receives [messages](messages.md).

## Actions

Actions drive a page from your space: wait, go back or forward, click, press a key, run JavaScript in the page, or post it a message. Run them with `RunActions`, or put them in the `actions` setting to run when the browser starts. They're JSON:

```js
browser.RunActions(JSON.stringify({
    actions: [
        { actionType: "postmessage", strParam1: "hello" },
        { actionType: "delayseconds", numParam1: 2 },
        { actionType: "runscript", strParam1: "document.body.style.background = 'black'" }
    ]
}));
```

| `actionType` | Parameters | What it does |
|---|---|---|
| `delayseconds` | `numParam1`: seconds | Waits before the next action. |
| `goback`, `goforward` | | The page's history, like the browser buttons. |
| `click2d` | `numParam1`, `numParam2`: x and y in page pixels | Presses the mouse at that point. It's a press only, so a page that waits for a full click may not react. |
| `keypress` | `strParam1`: one character, or a key name such as `Return`, `Backspace` or `Space` | Types the key into the page. |
| `runscript` | `strParam1`: JavaScript | Runs the code in the page. Its result only appears in the Unity Console. |
| `postmessage` | `strParam1`: the message | Raises a `bsmessage` event in the page; see [Messages](messages.md#from-your-space-to-a-page). |

- **Startup actions run before the page has loaded**, so begin them with a `delayseconds` long enough for it to load. There's no "page loaded" event; have the page [send a message](messages.md) when it's ready instead.
- **Avoid the `§` character** in actions and messages: it's used to split messages on the way through and cuts them short.
- **In Visual Scripting**, call the BS Browser's `_RunActions` method with the same JSON text.

## Events

The component raises two events, listed in the Inspector under **Available Properties**:

| Event | When | In JavaScript | In Visual Scripting |
|---|---|---|---|
| **On Receive Browser Message** (string) | The page sent a message | `obj.On("browser-message", e => …)` on the browser's GameObject; `e.detail` is the message | **On Receive Browser Message** (`Events > BS > Browser`). It fires for every browser in the space, so put something in your messages that says which page sent them |
| **On Browser Texture** (Texture2D) | The page first draws, and again after each resize | | |

For the page's pixels in scripts and graphs, use its [texture reference](textures.md) instead of the texture event.

## Debugging

The page's `console.log` lines appear in the Unity Console, starting `[Ora JS]`. The Setup panel's **Page developer tools** opens DevTools for [the space browser](space-browser.md), not for a BS Browser.

## Performance

Each BS Browser is a whole browser, and its page is copied into a texture every frame: a 1280 × 720 page is about 3.7 MB a frame. A few browsers are fine; dozens are not. Keep pages no bigger than they need to be, and remove a browser you're finished with. Turning interaction off doesn't stop a page drawing.
