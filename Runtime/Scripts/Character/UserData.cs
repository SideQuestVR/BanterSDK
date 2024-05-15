using System.Collections.Generic;
using Banter;
using TMPro;
using UnityEngine;

public class UserData : MonoBehaviour {
    public new string name;
    public string id;
    public string uid;
    public string color;
    public bool isLocal;
#if !BANTER_EDITOR
    public TextMeshPro nameTag;
#endif
    public Transform Head;
    public Transform LeftHand;
    public Transform RightHand;
    public Transform Body;
    public Transform Cockpit;
    Dictionary<string, string> props = new Dictionary<string, string>();
    BanterScene scene;
    void Start(){
        scene = BanterScene.Instance();
#if !BANTER_EDITOR
        name = NameGenerator.Generate();
        id = System.Guid.NewGuid().ToString();
        uid = System.Guid.NewGuid().ToString();
        //instance = System.Guid.NewGuid().ToString();
        nameTag.text = name;
        scene.AddUser(this);
#endif
    }
    public void SetProps(string[] props){
        foreach(var prop in props) {
            
#if !BANTER_EDITOR
            var parts = prop.Split(MessageDelimiters.TERTIARY);
            if(parts.Length == 2){
                this.props[parts[0]] = parts[1];
            }else{
                Debug.LogError("Invalid prop: " + prop);
            }
#else
            // Debug.LogError("SetUserProps not implemented yet: " + prop);
#endif
        }
    }

    public void Attach(UnityAndBanterObject go, AttachmentType hand){
        go.banterObject.previousParent = go.gameObject.transform.parent;
        switch(hand){
            case AttachmentType.Head:
                go.gameObject.transform.SetParent(Head, false);
                break;
            case AttachmentType.LeftHand:
                go.gameObject.transform.SetParent(LeftHand, false);
                break;
            case AttachmentType.RightHand:
                go.gameObject.transform.SetParent(RightHand, false);
                break;
            case AttachmentType.Body:
                go.gameObject.transform.SetParent(Body, false);
                break;
            case AttachmentType.Cockpit:
                go.gameObject.transform.SetParent(Cockpit, false);
                break;
        }
    }

    void OnDestroy() {
        scene.RemoveUser(this);
    }
}
