namespace Banter.SDK
{
    public class BanterUser
    {
        public string name;
        public string id;
        public string uid;
        public string color;
        public bool isLocal;
        public bool isSpaceAdmin;
       public BanterUser()
        {

        }
        public BanterUser(string name,string id,string uid,string color,bool isLocal,bool isSpaceAdmin)
        {
            this.name = name;
            this.id = id;
            this.uid = uid;
            this.color = color;
            this.isLocal = isLocal;
            this.isSpaceAdmin = isSpaceAdmin;

        }
    }
}
