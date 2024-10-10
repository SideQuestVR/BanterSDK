using System.Collections.Generic;
using Banter.SDK;
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
    public Transform LeftHand;
    public Transform RightHand;
    public Transform Body;
    public Transform Cockpit;
    Dictionary<string, string> props = new Dictionary<string, string>();
    BanterScene scene;
    void Start()
    {
        scene = BanterScene.Instance();
#if !BANTER_EDITOR
        name = NameGenerator.Generate();
        id = System.Guid.NewGuid().ToString();
        uid = System.Guid.NewGuid().ToString();
        color = ColorUtility.ToHtmlStringRGB(Random.ColorHSV());
        //instance = System.Guid.NewGuid().ToString();
        nameTag.text = name;
        scene.AddUser(this);
#endif
    }
    public void SetProps(string[] props)
    {
        foreach (var prop in props)
        {

#if !BANTER_EDITOR
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
        scene.RemoveUser(this);
    }
}
