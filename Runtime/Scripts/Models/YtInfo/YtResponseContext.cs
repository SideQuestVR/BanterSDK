using System;
using System.Collections.Generic;

[Serializable]
public class YtResponseContext
{
    public YtStreamingData streamingData;
    public YtPlayabilityStatus playabilityStatus;
    public YtVideoDetails videoDetails;
}