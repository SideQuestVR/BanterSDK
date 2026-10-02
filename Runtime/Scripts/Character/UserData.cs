using System.Collections.Generic;
using BS;
using TMPro;
using UnityEngine;

public class UserData : MonoBehaviour
{
    public new string name;
    public string id;
    public string uid;
    public string color;
    public bool isLocal;
    public bool isSpaceAdmin;
    public TextMeshPro nameTag;
    public Transform Head;
    Dictionary<string, string> props = new Dictionary<string, string>();
    BSScene scene;
    void Start()
    {
        scene = BSScene.Instance();
#if !GREENFIELD_PROJECT
        // A host (BSNetworkHost) stamps its users before they start: keep what it set, fill the rest.
        if (string.IsNullOrEmpty(name)) name = NameGenerator.Generate();
        if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(uid)) uid = System.Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(color)) color = ColorUtility.ToHtmlStringRGB(Random.ColorHSV());
        //instance = System.Guid.NewGuid().ToString();
        if (nameTag) nameTag.text = name;
        // The host announces its users itself, the moment it creates them.
        if (!scene.users.Contains(this)) scene.AddUser(this);
#endif
    }
    public void SetProps(string[] props)
    {
        foreach (var prop in props)
        {

#if !GREENFIELD_PROJECT
            var parts = prop.Split(MessageDelimiters.TERTIARY);
            if (parts.Length == 2)
            {
                this.props[parts[0]] = parts[1];
            }
            else
            {
                Debug.LogError("Invalid prop: " + prop);
            }
#else
            // Debug.LogError("SetUserProps not implemented yet: " + prop);
#endif
        }
    }

    void OnDestroy()
    {
#if !GREENFIELD_PROJECT
        // A user the host announced can be destroyed before its own Start ran.
        if (scene == null)
        {
            var current = BSScene.Current;
            if (current != null && current.users.Contains(this)) scene = current;
        }
#endif
        scene?.RemoveUser(this);
    }
}
