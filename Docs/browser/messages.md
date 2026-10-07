# Messages

A page in a [BS Browser](bs-browser.md) and your space can send each other text messages. That turns a web page into part of your world: a quiz page can tell the space who answered, and the space can tell a scoreboard page what to show.

Messages are strings. Send JSON and parse it at the other end.

## From a page to your space

The page calls `window.O.sendMessage(type, data)`. The browser adds `window.O` to every page it loads, but only once the page has started loading, so wait for it:

```html
<script>
  function toSpace(message) {
    if (window.O && typeof window.O.sendMessage === "function") {
      window.O.sendMessage("app", JSON.stringify(message));
    } else {
      setTimeout(() => toSpace(message), 100);
    }
  }
  toSpace({ from: "quiz", event: "ready" });
</script>
```

- **`data`** is what your space receives, as a string. **`type`** is a name of your own; the space never sees it. Don't use a type the browser uses itself, such as `LoadStarted`, `DomReady`, `TitleChanged`, `KeyboardFocus` or `ConsoleLog`; a name like `app` is safe.
- **Only the top page** gets `window.O`, not frames inside it.

Your space receives it as the browser's message event:

```js
// On the GameObject that has the BS Browser
screen.On("browser-message", (e) => {
    const message = JSON.parse(e.detail);
    if (message.from === "quiz" && message.event === "ready") {
        console.log("The quiz page is ready");
    }
});
```

In Visual Scripting, use **On Receive Browser Message** (`Events > BS > Browser`), which outputs the `Message`. It fires for messages from every browser in the space, so say who sent each message, as `from` does above.

## From your space to a page

Run a `postmessage` [action](bs-browser.md#actions) on the browser. The page receives it as a `bsmessage` event:

```js
// Your space
browser.RunActions(JSON.stringify({
    actions: [{ actionType: "postmessage", strParam1: JSON.stringify({ show: "Round 2" }) }]
}));
```

```html
<!-- The page -->
<script>
  window.addEventListener("bsmessage", (e) => {
    const message = JSON.parse(e.detail.message);
    document.getElementById("title").textContent = message.show;
  });
</script>
```

In Visual Scripting, call the BS Browser's `_RunActions` method with the same JSON text.

To call a function in the page directly, use a `runscript` action instead, for example `{ actionType: "runscript", strParam1: "nextRound()" }`. Its return value only reaches the Unity Console, so have the page send a message back if your space needs an answer.

## A complete example

A page with a button counts clicks in your space and shows the total. The space keeps the count, so it could share it with everyone in the room.

```js
// index.html: your space's script
const screen = new BS.GameObject({ name: "Counter", localPosition: new BS.Vector3(0, 1.5, 2) });
const browser = screen.AddComponent(new BS.Browser({ url: "https://example.com/counter.html", pageWidth: 800, pageHeight: 450 }));
let count = 0;

screen.On("browser-message", (e) => {
    const message = JSON.parse(e.detail);
    if (message.event === "clicked") {
        count++;
        browser.RunActions(JSON.stringify({
            actions: [{ actionType: "postmessage", strParam1: JSON.stringify({ count }) }]
        }));
    }
});
```

```html
<!-- counter.html, on your own web server -->
<button id="add">Click me</button>
<p>Clicks: <span id="count">0</span></p>
<script>
  function toSpace(message) {
    if (window.O && typeof window.O.sendMessage === "function") window.O.sendMessage("app", JSON.stringify(message));
    else setTimeout(() => toSpace(message), 100);
  }
  document.getElementById("add").addEventListener("click", () => toSpace({ event: "clicked" }));
  window.addEventListener("bsmessage", (e) => {
    document.getElementById("count").textContent = JSON.parse(e.detail.message).count;
  });
</script>
```

## Tips

- **Keep messages small.** Messages pass through the browser on their way, and a very large one slows things down.
- **Avoid the `§` and `¶` characters**: they split messages on the way through.
- **Say who's talking.** With several browsers, include a field such as `from` in every message.
- **Ready before you send.** A message posted before the page has loaded its listener is lost. Have the page say it's ready first, as in the examples.
