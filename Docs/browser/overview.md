# Browsers

The web browser isn't an add-on here: it's what the platform is built on. Every space is a website, and every world you publish is a web page with a URL. On top of that, you can put real, interactive web pages anywhere in your world.

## Every space is a website

A space lives at a URL. Each world you publish gets its own address (the Builder shows it under the **World** dropdown, for example `https://my-world.worldspace.host`), and visiting a space means loading that page.

The page is your world's `Assets/WebRoot/index.html`. When someone joins:

1. The client opens the space's URL in a real Chromium browser, built into the app.
2. The page loads your Unity scene, the world you built in the Editor.
3. Your JavaScript runs in that page and drives the scene through the `BS` API: creating objects, reacting to players, syncing state.

So moving between spaces is moving between URLs: a [portal](../building-in-unity/easy-prefabs.md#portal-bsportal) is a link to another space's address. And anything a web page can do, your space's script can do too: fetch data from your own server, open a WebSocket, use any JavaScript library.

This page is the **space browser**. Nobody sees it directly: it runs your script behind the scenes, though you can show its pixels on a surface if you like. See [The Space Browser](space-browser.md).

## Browsers you place around the world

A **BS Browser** puts a web page on a surface in your world: a real Chromium page that players can click, scroll and type into, with its sound coming from where it hangs. Use them for:

- **Screens and signs** that update themselves: dashboards, schedules, live scores, social feeds.
- **Media**: video sites, music players, slideshows.
- **Tools and games** built as web apps: whiteboards, quizzes, leaderboards, mini-games.
- **Anything on the web**: documentation, a shop, your own site.

They aren't just pictures of pages, either:

- **Your space and the page can talk.** The page sends messages to your script or graph, and your space can send messages back, or run JavaScript in the page. See [Messages](messages.md).
- **Every page is a live texture.** Put a browser's pixels on any material, so a page can wrap a screen, a billboard or a sphere. See [Textures](textures.md).
- **You control it from your space**: change its URL, size, and whether players can click it.

| | Space browser | BS Browser |
|---|---|---|
| What it shows | Your world's own page, `index.html` | Any URL you set |
| Seen by players | Not unless you show its texture | Yes, as a panel in the world |
| Runs your space script (`window.BS`) | Yes | No: it's an ordinary web page |
| How many | One per space | As many as you place (each is a full browser, so keep it to a few) |

## Getting started

### 1. Place a browser

In Unity, use **GameObject > BS > Objects > Browser**. You get an object with a BS Browser showing `https://sidequestvr.com`; change its **Url** in the Inspector. The Scene view outlines it in blue: about 0.98 × 0.55 m, seen from the object's back (−Z), the side the arrow points to.

Or create one from your space's script:

```js
const screen = new BS.GameObject({ name: "Screen", localPosition: new BS.Vector3(0, 1.5, 2) });
const browser = screen.AddComponent(new BS.Browser({
    url: "https://example.com",
    pageWidth: 1280,
    pageHeight: 720
}));
```

### 2. Press Play

The page loads in Play mode. Click it and scroll it with the mouse, and type once a text field has focus. In a headset, players point and click with their controllers.

### 3. Make it yours

- **Size:** a page is `pageWidth` × `pageHeight` pixels, drawn at 1300 pixels per metre. See [The BS Browser](bs-browser.md#size-and-placement).
- **Talk to it:** send messages between the page and your space. See [Messages](messages.md).
- **Reuse its pixels:** show the page on other surfaces. See [Textures](textures.md).

## In this section

- [The BS Browser](bs-browser.md): the component, its settings, input, actions and events.
- [Messages](messages.md): pages and your space talking to each other.
- [Textures](textures.md): browser pages on materials.
- [The Space Browser](space-browser.md): your world's own page, its size and texture, and running JavaScript in it from Visual Scripting.
