using System;
[Serializable]
public class Kit{
  public string id;
  public string name;
  public string description;
  public string picture;
  public int item_count;
  public string created_at;
  public string kit_categories_id;
  public string users_id;
  public string windows;
  public string android;
  public int use_count;
  public KitUser user;
  public KitItem[] items;
}
    
[Serializable]
public class  KitUser{
  public string id;
  public string ext_id;
  public string name;
  public string bio;
  public string created_at;
  public string profile_pic;
  public string color;
}

[Serializable]
public class KitRows{
    public Kit[] rows;
}


[Serializable]
public class KitUserRows{
    public KitUser[] rows;
}
[Serializable]
public class KitItem{
  public string name;
  public string id;
  public string kits_id;
  public string path;
  public string picture;
  public string created_at;
}
[Serializable]
public class KitCategory{
  public string id;
  public string name;
  public string description;
  public string picture;
  public int item_count;
  public string created_at;
}

[Serializable]
public class KitCategoryRows{
    public KitCategory[] rows;
}
/*


{"rows":[{"id":"2","name":"Video","description":"Tools and objects to use with video content and playback.","picture":"https://cdn.sidequestvr.com/file/1979512/video.png","item_count":0,"created_at":"2025-03-17T11:34:32.499Z"},{"id":"3","name":"AI","description":"All sortts of things related to AI models and AI generation.","picture":"https://cdn.sidequestvr.com/file/1979513/ai.png","item_count":0,"created_at":"2025-03-17T11:34:32.499Z"}]}


*/