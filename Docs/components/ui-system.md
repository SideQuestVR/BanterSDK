# UI System

Create 2D user interfaces in VR with the UI system: a **UI Panel** is a flat surface in the world, and your script fills it with buttons, labels, sliders and other elements, laid out and styled much like HTML and CSS (it's Unity's UI Toolkit underneath).

## UIPanel

The surface the elements are drawn on. Add it to a GameObject, then create elements for it. A panel is `resolution` pixels at 100 pixels per metre (512 × 512 pixels is a 5.12 m square), read from the object's back (−Z). It puts its object on the UI layer, so players can point at it and click.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `resolution` | Vector2 | 512, 512 | Size in pixels |
| `meshInput` | boolean | false | Draw onto the mesh already on the object instead of a flat panel, and take pointer input from that mesh's UVs, so the panel can be any shape |
| `enableHaptics` | boolean | false | Buzz the controller on click, enter and exit |
| `clickHaptic`, `enterHaptic`, `exitHaptic` | Vector2 | 0.5, 0.1 / 0.3, 0.05 / 0.2, 0.05 | Each haptic's amplitude (0 to 1) and duration (seconds) |
| `enableSounds` | boolean | false | Play sounds on click, enter and exit |
| `clickSoundUrl`, `enterSoundUrl`, `exitSoundUrl` | string | "" | The sounds, as URLs ending in `.mp3`, `.wav` or `.ogg` |

<div class="docs-tabs">

```js
const panelObj = new BS.GameObject({ name: "UIPanel", localPosition: new BS.Vector3(0, 1.5, 3) });
const panel = await panelObj.AddComponent(new BS.UIPanel({
    resolution: new BS.Vector2(800, 600),
    enableHaptics: true,
    clickHaptic: new BS.Vector2(0.1, 0.05),   // amplitude, duration
    enterHaptic: new BS.Vector2(0.05, 0.02),
    exitHaptic: new BS.Vector2(0.05, 0.02),
    enableSounds: false
}));
```

![BS UI Panel in the Unity Inspector](../images/components/ui-panel.png)

</div>

**Methods:**

```js
panel.SetBackgroundColor(new BS.Vector4(0, 0, 0, 0.5));   // RGBA, 0-1
```

The examples below use this `panel`.

## UIElement (Base Class)

Every element is created for one panel, with an optional parent element: `new BS.UIButton(panel, parent?)`. Without a parent it goes straight onto the panel. You can create elements as soon as you have the panel; anything you set before the panel is ready is sent once it is.

**Properties:**

```js
element.id;           // Unique ID, given at creation (don't change it)
element.type;         // A BS.UIElementType
element.panel;        // The UIPanel it belongs to
element.parent;       // Parent element, or null on the panel itself
element.children;     // Child elements
element.enabled;      // false greys it out and stops input
element.visible;      // false hides it and takes it out of the layout
```

**Hierarchy Methods:**

```js
parent.AppendChild(child);
parent.RemoveChild(child);
parent.InsertBefore(child, referenceChild);
element.Destroy();    // Remove it and its children
```

**Property Methods:** each element type has its own properties (`button.text`, `toggle.checked`, …), listed below. `SetProperty` is the generic form, taking a `BS.UIPropertyName` (`BS.PN` is for components, not UI elements):

```js
element.SetProperty(BS.UIPropertyName.Text, "Hello");   // same as element.text = "Hello"
element.GetProperty(BS.UIPropertyName.Text);            // the value your script last set
```

**Style Methods:**

```js
// The style helper, with CSS property names in camelCase
element.style.backgroundColor = "#4CAF50";
element.style.width = "100px";
element.style.height = "50%";

// Several at once
element.SetStyles({
    backgroundColor: "#FF0000",
    padding: "10px",
    borderRadius: "5px"
});

// One by name: kebab-case or camelCase both work
element.SetStyle("background-color", "rgba(0, 0, 0, 0.5)");
element.GetStyle("background-color");   // the value your script last set under that name
```

- **Lengths:** a number (pixels), `"10px"`, `"50%"` or `"auto"`. `em` and `rem` count as 16 px.
- **Colours:** `#RGB`, `#RRGGBB`, `rgb()`, `rgba()`, `hsl()`, `hsla()` or a basic colour name (`red`, `white`, `transparent`, …). Use `rgba()` for see-through colours: the style helper and `SetStyles` reject `#RRGGBBAA`.
- A value the page can tell is invalid logs a warning in the browser console and isn't sent. A style name the app doesn't know logs a warning in the Unity Console and is ignored. See [Style Properties Reference](#style-properties-reference) for what's supported.

**Event Methods:**

```js
element.OnClick((e) => console.log("Clicked"));
element.OnDoubleClick((e) => console.log("Double-clicked"));
element.OnContextMenu((e) => console.log("Right-clicked"));
element.OnMouseDown((e) => console.log("Mouse down, button", e.button));
element.OnMouseUp((e) => console.log("Mouse up"));
element.OnMouseEnter((e) => console.log("Hover start"));
element.OnMouseLeave((e) => console.log("Hover end"));
element.OnMouseMove((e) => console.log("Moving", e.clientX, e.clientY));
element.OnKeyDown((e) => console.log("Key:", e.key));
element.OnKeyUp((e) => console.log("Key released"));
element.OnFocus((e) => console.log("Focused"));
element.OnBlur((e) => console.log("Lost focus"));
element.OnChange((e) => console.log("Value:", e.value));
element.OnWheel((e) => console.log("Scrolled", e.deltaY));

// Standard event listener API (On/Off are aliases)
element.AddEventListener("click", handler);
element.AddEventListener("click", handler, { stopPropagation: true });  // don't let it reach parent elements
element.RemoveEventListener("click", handler);
```

**Finding elements:** keep references to the elements you create. `element.QuerySelector("#" + other.id)` finds a descendant by its `id`, but type and class selectors don't match anything.

`AddClass`, `RemoveClass` and `HasClass` exist on most elements, but a world can't load a style sheet for classes to match, and `HasClass` always returns `false`. Style elements with `style` and `SetStyle` instead.

## UIButton

Clickable button. Properties: `text`, `tooltip`, `name`.

```js
const button = new BS.UIButton(panel);
button.text = "Click Me";
button.style.width = "200px";
button.style.height = "50px";
button.style.backgroundColor = "#4CAF50";
button.style.color = "#FFFFFF";
button.OnClick(() => console.log("Button clicked!"));
```

## UILabel

Text display. Properties: `text`, `tooltip`, `name`.

```js
const label = new BS.UILabel(panel);
label.text = "Hello World";
label.style.fontSize = "24px";
label.style.color = "#FFFFFF";
```

## UISlider

Value slider. Properties: `minValue` and `maxValue` (default 0 to 10), `value`, `tooltip`, `name`.

```js
const slider = new BS.UISlider(panel);
slider.SetRange(0, 100);     // min, max; keeps the value inside the range
slider.SetValue(50);         // clamped to the range
slider.style.width = "200px";
slider.OnChange((e) => console.log("Value:", e.value));
// slider.Reset() moves it to the middle of the range
```

Setting the value from your script (`value`, `SetValue`, `Reset`) doesn't fire `OnChange`; only the player dragging it does.

## UIToggle

Checkbox/toggle. Properties: `checked`, `tooltip`, `name`. It has no label of its own: put a [UILabel](#uilabel) next to it, or use `CreateToggleWithLabel` (see [UI Factory Helpers](#ui-factory-helpers)).

```js
const toggle = new BS.UIToggle(panel);
toggle.checked = true;
toggle.OnChange((e) => console.log("Checked:", e.value));
```

## UIScrollView

Scrollable container: elements you add to it scroll when they don't fit. Properties: `horizontalScrolling` and `verticalScrolling` (show or hide each scroll bar), `scrollPosition` (the scroll offset in pixels, set as `{ x, y }`), `scrollDecelerationRate` and `elasticity` (0 to 1), `tooltip`, `name`.

```js
const scrollView = new BS.UIScrollView(panel);
scrollView.style.width = "300px";
scrollView.style.height = "200px";
scrollView.horizontalScrolling = false;
for (let i = 0; i < 20; i++) {
    new BS.UILabel(panel, scrollView).text = "Row " + i;
}
scrollView.scrollPosition = { x: 0, y: 120 }; // scroll down 120 px
```

## UIVisualElement

Generic container for layout, like a `<div>`. Properties: `tooltip`, `name`.

```js
const container = new BS.UIVisualElement(panel);
container.style.flexDirection = "row";
container.style.justifyContent = "space-between";
container.style.padding = "10px";
const left = new BS.UILabel(panel, container);
left.text = "Left";
```

## UITextField

Text input field with placeholder, password masking, and read-only support. Constructed with the owning panel: `new BS.UITextField(panel, parent?)`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `value` | string | — | Current text |
| `placeholder` | string | — | Hint shown while empty |
| `maxLength` | number | — | Maximum character count |
| `isPasswordField` | boolean | — | Mask the input |
| `isReadOnly` | boolean | — | Block editing |
| `label` | string | — | Built-in label text |
| `tooltip` | string | — | Hover tooltip |
| `name` | string | — | Element name |

```js
const field = new BS.UITextField(panel);
field.placeholder = "Type a name...";
field.maxLength = 32;
field.OnChange((e) => console.log("Value:", e.value));

field.Focus();               // Give the field keyboard focus
field.Blur();                // Drop focus
field.SelectAll();           // Select the current contents
```

## UIEdgeLayer

A canvas element that draws a set of cubic-bezier edges — built for node-graph style UIs. Constructed with the owning panel: `new BS.UIEdgeLayer(panel, parent?)`. Coordinates are in the layer's local pre-transform space, so ancestor pan/zoom transforms apply without resending edge data.

```js
const layer = new BS.UIEdgeLayer(panel);

// setEdges() full-replaces the edge set in a single message
layer.setEdges([
    {
        id: "e1",
        x1: 20, y1: 40,        // Start point
        cx1: 80, cy1: 40,      // First control point
        cx2: 120, cy2: 160,    // Second control point
        x2: 180, y2: 160,      // End point
        color: "#88CCFF",
        width: 2,
        dashed: false,         // Optional
        selected: false,       // Optional
        arrow: true            // Optional arrowhead
    }
]);

layer.edges;   // Read-only view of the current edge set
```

## UIColorPicker

A native colour picker: a hue/saturation wheel with a draggable marker, brightness and opacity bars, editable hex / hsl / rgb fields, preset and recent swatches, and a presets-only mode. Constructed with the owning panel: `new BS.UIColorPicker(panel, parent?)`.

The picker is drawn by the app's own colour-picker package, which creator projects don't have, so in Play mode it shows as an empty element. It works in the app.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `value` | string | `#FFFFFFFF` | The colour as `#RRGGBBAA`. Writing it does not echo a change event |
| `mode` | string | `full` | `full`, or `presets` for the swatch-grid-only picker |
| `presets` | string | — | Comma-separated colour codes for the preset row; empty restores the defaults |
| `recent` | string | — | Comma-separated colour codes for the recent row; empty hides it |
| `showAlpha` | boolean | `true` | Show the opacity bar |
| `theme` | string | — | JSON palette and metrics (`surface`, `accent`, `text`, `wheelDiameter`, `fontSize`, …); a partial document is applied over the defaults |
| `tooltip` | string | — | Hover tooltip |
| `name` | string | — | Element name |

```js
const picker = new BS.UIColorPicker(panel);
picker.value = "#FF2E8CFF";
picker.presets = "#FFFFFF,#000000,#E53935,#1E88E5";
picker.theme = JSON.stringify({ accent: "#4CAF50", wheelDiameter: 240 });
picker.OnChange((e) => console.log("Colour:", e.value));   // live while dragging, then once on release
```

## UI Factory Helpers

`BS.UI` extends `UIPanel` with creator methods that pass the panel reference for you: `new BS.UI(resolution?, screenSpace?, meshInput?)` (defaults: `new BS.Vector2(512, 512)`, `false`, `false`).

| Method | Returns | Description |
|--------|---------|-------------|
| `CreateButton(parent?)` | UIButton | Button |
| `CreateLabel(text?, parent?)` | UILabel | Text label |
| `CreateElement(innerText?, parent?)` | UILabel | Alias of `CreateLabel` |
| `CreateToggle(parent?)` | UIToggle | Checkbox/toggle |
| `CreateSlider(min = 0, max = 100, parent?)` | UISlider | Slider with range preset |
| `CreateScrollView(parent?)` | UIScrollView | Scrollable container |
| `CreateVisualElement(parent?)` | UIVisualElement | Generic container |
| `CreateEdgeLayer(parent?)` | UIEdgeLayer | Bezier-edge canvas |
| `CreateColorPicker(parent?)` | UIColorPicker | Colour picker (wheel, bars, fields, swatches) |
| `CreateButtonWithText(text, tooltip?, parent?)` | UIButton | Button with text and optional tooltip |
| `CreateToggleWithLabel(labelText, checked = false, parent?)` | { toggle, label } | Toggle and a label in a container |
| `CreateSliderWithLabel(labelText, min = 0, max = 100, initialValue = 50, parent?)` | { slider, label } | Slider and a label reading "labelText: initialValue" (the label doesn't follow the slider) |
| `CreateVerticalContainer(parent?)` | UIVisualElement | A named container; elements stack vertically by default |
| `CreateHorizontalContainer(parent?)` | UIVisualElement | A named container; set `style.flexDirection = "row"` to lay it out in a row |
| `CreateCard(title, parent?)` | { container, titleLabel } | Container with a title label |

```js
const panelObj = new BS.GameObject({ name: "SettingsPanel", localPosition: new BS.Vector3(0, 1.5, 3) });
const ui = new BS.UI(new BS.Vector2(600, 400));
await panelObj.AddComponent(ui);

const card = ui.CreateCard("Settings");
const { toggle } = ui.CreateToggleWithLabel("Mute music", false, card.container);
const { slider } = ui.CreateSliderWithLabel("Volume", 0, 100, 50, card.container);
const apply = ui.CreateButtonWithText("Apply", "Save your changes", card.container);
apply.OnClick(() => console.log("applied"));
```

## Style Properties Reference

These style properties work; use the camelCase name with `style` and `SetStyles`, or either spelling with `SetStyle`. Values can be written the CSS way, with dashes (`flex-start`, `space-between`, `row-reverse`) or without.

**Layout:**
- `alignContent`, `alignItems`, `alignSelf` (`flex-start`, `flex-end`, `center`, `stretch`), `justifyContent` (`flex-start`, `flex-end`, `center`, `space-between`, `space-around`)
- `flexBasis`, `flexDirection` (`row`, `row-reverse`, `column`, `column-reverse`), `flexGrow`, `flexShrink`, `flexWrap` (`wrap`, `nowrap`)

**Size:**
- `width`, `height`, `minWidth`, `minHeight`, `maxWidth`, `maxHeight`

**Position:**
- `position` (`relative`, `absolute`)
- `left`, `top`, `right`, `bottom`

**Spacing:**
- `margin`, `marginLeft`, `marginRight`, `marginTop`, `marginBottom`
- `padding`, `paddingLeft`, `paddingRight`, `paddingTop`, `paddingBottom`
- `margin` and `padding` take one to four values, as in CSS: `"10px 20px"`

**Borders:**
- `borderWidth`, `borderLeftWidth`, `borderRightWidth`, `borderTopWidth`, `borderBottomWidth`
- `borderRadius`, `borderTopLeftRadius`, `borderTopRightRadius`, `borderBottomLeftRadius`, `borderBottomRightRadius`
- `borderColor`, `borderLeftColor`, `borderRightColor`, `borderTopColor`, `borderBottomColor`

**Background:**
- `backgroundColor`
- `backgroundImage`: `url("https://…/image.png")`, or `none`

**Text:**
- `color`, `fontSize`
- `fontWeight` and `fontStyle`: `normal`, `bold`, `italic` or `boldAndItalic` (they set the same thing, so the last one set wins)
- `textAlign`: `left`, `center` or `right`
- `textOverflow` (`clip`, `ellipsis`), `whiteSpace` (`normal`, `nowrap`)
- `unityTextOutlineColor`, `unityTextOutlineWidth`

**Display:**
- `display` (`flex`, `none`): `none` hides the element and takes it out of the layout
- `visibility` (`visible`, `hidden`), `overflow` (`visible`, `hidden`), `opacity`
- `element.visible = false` also hides an element and takes it out of the layout

**Transform:**
- `rotate`, `scale`, `translate`, `transformOrigin`

Not supported (they log a warning and do nothing): `backgroundSize`, `backgroundRepeat`, `backgroundPosition`, `lineHeight`, `letterSpacing`, `wordWrap`, `textShadow`, `cursor`, and the `transition…` properties.
