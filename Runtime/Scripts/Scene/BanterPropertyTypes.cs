using System;
using UnityEngine;
using Banter.SDK;
using PropertyName = Banter.SDK.PropertyName;

[Serializable]
public struct BanterVector4
{
    public float x;
    public float y;
    public float z;
    public float w;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Vector4;
    }
    public string Serialise()
    {
        // return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x + MessageDelimiters.SECONDARY + y + MessageDelimiters.SECONDARY + z + MessageDelimiters.SECONDARY + w;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}{MessageDelimiters.SECONDARY}{y}{MessageDelimiters.SECONDARY}{z}{MessageDelimiters.SECONDARY}{w}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 2) return;
        n = (PropertyName)int.Parse(parts[0]);
        x = NumberFormat.Parse(parts[2]);
        y = NumberFormat.Parse(parts[3]);
        z = NumberFormat.Parse(parts[4]);
        w = NumberFormat.Parse(parts[5]);
    }

    public static explicit operator BanterVector4(Vector4 v)
    {
        return new BanterVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }

    public static explicit operator BanterVector4(Quaternion v)
    {
        return new BanterVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
}


[Serializable]
public struct BanterVector2
{
    public float x;
    public float y;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Vector2;
    }
    public string Serialise()
    {
        //return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x + MessageDelimiters.SECONDARY + y;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}{MessageDelimiters.SECONDARY}{y}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 4)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse vector2: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = NumberFormat.Parse(parts[2]);
        y = NumberFormat.Parse(parts[3]);
    }

    public static explicit operator BanterVector2(Vector2 v)
    {
        return new BanterVector2() { x = v.x, y = v.y };
    }
}

[Serializable]
public struct BanterVector3
{
    public float x;
    public float y;
    public float z;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Vector3;
    }
    public string Serialise()
    {
        //return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x + MessageDelimiters.SECONDARY + y + MessageDelimiters.SECONDARY + z;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}{MessageDelimiters.SECONDARY}{y}{MessageDelimiters.SECONDARY}{z}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 5)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse vector3: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = NumberFormat.Parse(parts[2]);
        y = NumberFormat.Parse(parts[3]);
        z = NumberFormat.Parse(parts[4]);
    }

    public static explicit operator BanterVector3(Vector3 v)
    {
        return new BanterVector3() { x = v.x, y = v.y, z = v.z };
    }
}

[Serializable]
public struct BanterFloat
{
    public float x;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Float;
    }
    public string Serialise()
    {
        // return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 2)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse float: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = NumberFormat.Parse(parts[1]);
    }

    public static explicit operator BanterFloat(float v)
    {
        return new BanterFloat() { x = v };
    }
}

[Serializable]
public struct BanterInt
{
    public int x;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Int;
    }
    public string Serialise()
    {
        // return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 2)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse int: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = int.Parse(parts[1]);
    }

    public static explicit operator BanterInt(int v)
    {
        return new BanterInt() { x = v };
    }
}

[Serializable]
public struct BanterBool
{
    public bool x;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Bool;
    }
    public string Serialise()
    {
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{(x ? "1" : "0")}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 2)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse bool: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = int.Parse(parts[1]) == 1;
    }

    public static explicit operator BanterBool(bool v)
    {
        return new BanterBool() { x = v };
    }
}
[Serializable]
public struct BanterString
{
    public string x;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.String;
    }
    public string Serialise()
    {
        // return (int)n + MessageDelimiters.SECONDARY + GetShortType() + MessageDelimiters.SECONDARY + x;
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}";
    }
    public void Deserialise(string str)
    {
        var parts = str.Split(MessageDelimiters.SECONDARY);
        if (parts.Length < 2)
        {
            LogLine.Do(Color.red, "[BanterTypes]", "Could not parse string: " + str);
            return;
        }
        n = (PropertyName)int.Parse(parts[0]);
        x = parts[1];
    }

    public static explicit operator BanterString(string v)
    {
        return new BanterString() { x = v };
    }
}

[Serializable]
public struct BanterStruct
{
    public PropertyName n;

    public static implicit operator BanterStruct(BanterString v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterBool v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterInt v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterFloat v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterVector2 v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterVector3 v)
    {
        return new BanterStruct() { n = v.n };
    }

    public static implicit operator BanterStruct(BanterVector4 v)
    {
        return new BanterStruct() { n = v.n };
    }

}