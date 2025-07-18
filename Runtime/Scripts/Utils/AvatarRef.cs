using UnityEngine;
public class AvatarRef : MonoBehaviour
{
    public GameObject avatarGameObject;

    private static AvatarRef _Instance;

    public static AvatarRef Instance
    {
        get
        {
            if (_Instance == null)
            {
                var existing = GameObject.Find("__AvatarRef");
                if (existing != null)
                {
                    _Instance = existing.GetComponent<AvatarRef>();
                    if( _Instance == null)
                    {
                        existing.name = "__AvatarRef_backup?";
                        MakeInstance();
                    }
                }
                else
                {
                    MakeInstance();
                }
            }
            return _Instance;
        }
    }

    public static void  MakeInstance()
    {
        var gameObject = new GameObject("__AvatarRef");
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        var store = gameObject.AddComponent<AvatarRef>();
        _Instance = store;
    }

    public void SetAvatarGameObject(GameObject avatarGameObject)
    {
        this.avatarGameObject = avatarGameObject;
    }
}