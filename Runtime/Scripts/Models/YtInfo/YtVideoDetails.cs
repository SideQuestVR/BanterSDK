using System;

[Serializable]
public class YtVideoDetails
{
    public string videoId;
    public string title;
    public string lengthSeconds;
    public string channelId;
    public string shortDescription;
    public string author;
    public string viewCount;
    public YtThumbnailContainer thumbnail;
}