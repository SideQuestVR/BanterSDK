using System;
using System.Collections.Generic;

[Serializable]
public class YtThumbnail
{
    public string url;
    public int width;
    public int height;
}

[Serializable]
public class YtThumbnailContainer
{
    public List<YtThumbnail> thumbnails;
}
