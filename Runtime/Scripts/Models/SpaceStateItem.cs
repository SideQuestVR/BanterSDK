
using System;
using Banter.SDK;

[Serializable]
public class SpaceStateItem
{
    public string key;
    public string value;
    public override string ToString()
    {
        return key + MessageDelimiters.TERTIARY + value;
    }
}