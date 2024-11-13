# Contribution Guidelines

[Back to documentation](/README.md?id=banter-sdk-contributions)

### Introduction

You can contribute by submitting your own components to be added to the Banter SDK so you can use them in your spaces. There are two types of components 
you can create, a Banter component (`BanterComponentBase`) or a Unity component (`UnityComponentBase`). A Banter component is usually a custom component designed to achieve functionality not already 
available in Unity whereas a Unity component is usually more of a direct mapping between a component in Unity and a Banter component.

There is also a number of Attributes you will need to use to define how your component will work - don't worry we can help you with these 
if you get them wrong. Below is some info about how they work.


### Prerequisites

Here's some important things to check before you begin.

#### Process

You can fork the SDK repository on github at `https://github.com/SideQuestVR/BanterSDK`, create new components which must be placed in `Runtime/Scripts/Scene/Components`. You can then open a Pull Request into the main repo with your contributions included and following the conventions described below including all of the required code.
You can then install the package from your new fork instead of the one in the documentation. You will need to move the SDK as it will be in an immutable folder. In order to make changes and not have them be overridden, you will need to close Unity move the package from the 
`Library/PackageCache` folder into the `Packages` folder so you can make edits. Start up the Unity Editor again and now you can make edits. 

#### Disclaimer

Contributing to the Banter SDK does not grant you any ownership over the SDK in part or at all, your contributions if accepted will become part of the code used in Banter which is owned solely by SideQuest. 
We also may decide not to accept your contributions and can do so without reason.  

### Class Attributes 

Here are the components you will need to add to the class.

#### RequireComponent Attribute

This attribute is added to all Banter components to ensure that the `BanterObjectId` component is added along with the Banter component. This ID 
component is important to be able to address this object and access it from `JavaScript` and in the scene generally. 

```cs
[RequireComponent(typeof(BanterObjectId))]
```

#### WatchComponent Attribute

This attribute is used during the code generation process to identify the class to be used for generation, and to identify the type of the component being mapped. 
The type part of the `WatchComponent` attribute is only used for Unity components and not Banter components.

```cs
// For a BanterComponentBase
[WatchComponent]
// or for a UnityComponentBase
[WatchComponent(typeof(Transform))] // Replace with the Type of the component you are mapping from.
```

### Property Attributes

These attributes are used to describe your properties, and whether their values are accessible and whether changes to the property should be streamed to `JavaScript` or not.

#### See Attribute

The `See` attribute marks the property as see-able from `JavaScript`. This means this property will be created by the code generation on the `JavaScript` equivalent of the component.

[Complete Example](https://github.com/SideQuestVR/BanterSDK/blob/main/Runtime/Scripts/Scene/Components/BanterTransform.cs)

**Parameters**
- `initial` - Sets the value that code generation will use when this property is declared in javascript, otherwise it is not initialised with a value.

**Code Sample**
```cs
[See(initial = "0,0,0")] public Vector3 position;
```


#### Watch Attribute

The `Watch` attribute has all the functionality of the `See` attribute, but it also marks that property to have changes to the property synced back to `JavaScript`. 
The `Watch` attribute has the same parameters as the `See` attribute,

[Complete Example](https://github.com/SideQuestVR/BanterSDK/blob/main/Runtime/Scripts/Scene/Components/BanterTransform.cs)

**Parameters**
- `initial` - Sets the value that code generation will use when this property is declared in javascript, otherwise it is not initialised with a value.

**Code Sample**
```cs
[Watch(initial = "0,0,0")] public Vector3 position;
```

### Method Attribute

The method attribute is used to expose a method to `JavaScript` so it can be called on the equivalent component in `JavaScript`. These methods should always 
have an underscore(`_`) at the start of their names - this is important as the code generation will add a method using the same name without the underscore.

[Complete Example](https://github.com/SideQuestVR/BanterSDK/blob/main/Runtime/Scripts/Scene/Components/BanterRigidbody.cs)

**Parameters**
- `overload` - Set a postfix to append to the method name for overload methods. This allows you to expose `C-Sharp` methods with the same name to `JavaScript`, 
where methods cannot have the same name.


**Code Sample**
```cs
[Method]
public void _AddForce(Vector3 force, ForceMode mode)
{
    _rigidbody.AddForce(force, mode);
}
[Method(overload = "Values")]
public void _AddForce(float x, float y, float z, ForceMode mode)
{
    _rigidbody.AddForce(x, y, z, mode);
}
```

### Code Comments

Code comments should be added in the github markdown flavour. These are concatenated at build time to include them in this documentation system! 
Clear documentation will make the approval process of your component a lot faster. See below for a full example.


### Complete Examples

Below are some complete examples of components you could submit for consideration. 


#### UnityComponentBase

Below is a sample of a Unity component being mapped into banter including documentation, it is usually pretty straight forward. When using this component, you should use the same names for your properties as the Unity component you are mapping to.

```cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Transform
    Add a transform to the object. Every object has a transform by default in Unity, this component sets up tracking for the transform properties.

    **Properties**
     - `position` - The position of the object.
     - `localPosition` - The local position of the object.
     - `rotation` - The rotation of the object.
     - `localRotation` - The local rotation of the object.
     - `localScale` - The local scale of the object.
     - `eulerAngles` - The euler angles of the object.
     - `localEulerAngles` - The local euler angles of the object.
     - `up` - The up vector of the object.
     - `forward` - The forward vector of the object.
     - `right` - The right vector of the object.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyTransform");
        const transform = await gameObject.AddComponent(new BS.Transform());
        transform.position = new BS.Vector3(1,1,1);
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent(typeof(Transform))]
    public class BanterTransform : UnityComponentBase
    {
        [Watch(initial = "0,0,0")] public Vector3 position;
        [Watch(initial = "0,0,0")] public Vector3 localPosition;
        [Watch(initial = "0,0,0,1")] public Quaternion rotation;
        [Watch(initial = "0,0,0,1")] public Quaternion localRotation;
        [Watch(initial = "1,1,1")] public Vector3 localScale;
        [Watch(initial = "0,0,0")] public Vector3 eulerAngles;
        [Watch(initial = "0,0,0")] public Vector3 localEulerAngles;
        [Watch(initial = "0,1,0")] public Vector3 up;
        [Watch(initial = "0,0,1")] public Vector3 forward;
        [Watch(initial = "1,0,0")] public Vector3 right;
    }
  }
}
```


#### BanterComponentBase

Below is a sample of a Banter component including documentation, it is more complex but also has more capability. At some point Unity components may need to be changed to Banter components to do more complex things. 

```cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Banter Kit Item
    Add a kit item to the scene. This component will wait until all asset bundles are loaded in the scene before trying to instantiate a kit item (prefab).

    **Properties**
     - `path` - The location of the prefab in the kit object - the same as the path in the asset bundle (always lower case).

    **Code Example**
    ```js
        const path = "assets/prefabs/mykititem.prefab";
        const gameObject = new BS.GameObject("MyKitItem"); 
        const kitItem = await gameObject.AddComponent(new BS.BanterKitItem(path));
    ```

    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterKitItem : BanterComponentBase
    {
        [See(initial = "")] public string path = "";
        GameObject item;
        public AssetBundle KitBundle;
        private async Task SetupComponent()
        {
            await new WaitUntil(() => scene.bundlesLoaded); // You can await something here if need be. 
            
            try
            {
                // Do Stuff here
                SetLoadedIfNot(); // This must be called once the component is fully loaded.
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message); // This should be installed if there is an error in the component. 
            }
        }

        public override void DestroyStuff()
        {
            // Tear everything down again.
        }
        public override void StartStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties) // This is called any time the properties on the component change. The list contains the ones that changed. 
        { 
            _ = SetupComponent // This prevents a lint warning that the async method is not awaited. 
        }
    }
}
```

### Final Thoughts

In some cases we may need to add special consideration for certain components to handle them as efficiently as possible. If you follow the above samples exactly then we should be able to add new components to be included in the next build, but in some cases it could take longer. We will endeavour to communicate as much as possible as things progress and if you want to feel free to ask about progress at the weekly space makers event. 
