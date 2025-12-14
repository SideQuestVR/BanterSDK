using System;
using UnityEngine;
using Banter.SDK;
using PropertyName = Banter.SDK.PropertyName;

[Serializable]
public struct BanterVector5
{
    public float x;
    public float y;
    public float z;
    public float w;
    public float v;
    public PropertyName n;
    public int GetShortType()
    {
        return (int)PropertyType.Vector5;
    }
    public string Serialise()
    {
        return $"{(int)n}{MessageDelimiters.SECONDARY}{GetShortType()}{MessageDelimiters.SECONDARY}{x}{MessageDelimiters.SECONDARY}{y}{MessageDelimiters.SECONDARY}{z}{MessageDelimiters.SECONDARY}{w}{MessageDelimiters.SECONDARY}{v}";
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
        v = NumberFormat.Parse(parts[6]);
    }


    public static explicit operator BanterVector5(JointLimits v)
    {
        return new BanterVector5() { x = v.bounciness, y = v.bounceMinVelocity, z = v.contactDistance, w = v.min, v = v.max };
    }
    public static explicit operator JointLimits(BanterVector5 v)
    {
        return new JointLimits() { bounciness = v.x, bounceMinVelocity = v.y, contactDistance = v.z, min = v.w, max = v.v };
    }
}

public class Vector5
{
    public Vector5(float x, float y, float z, float w, float v)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
        this.v = v;
    }
    public float x;
    public float y;
    public float z;
    public float w;
    public float v;
    
}

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


    public static explicit operator BanterVector4(Quaternion v)
    {
        return new BanterVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator Quaternion(BanterVector4 v)
    {
        return new Quaternion() { x = v.x, y = v.y, z = v.z, w = v.w };
    }

    public static explicit operator BanterVector4(Vector4 v)
    {
        return new BanterVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator Vector4(BanterVector4 v)
    {
        return new Vector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator BanterVector4(JointDrive v)
    {
        return new BanterVector4() { x = v.positionSpring, y = v.positionDamper, z = v.maximumForce, w = v.useAcceleration ? 1 : 0 };
    }
    public static explicit operator JointDrive(BanterVector4 v)
    {
        return new JointDrive() { positionSpring = v.x, positionDamper = v.y, maximumForce = v.z, useAcceleration = !(v.w == 0) };
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
    public static explicit operator Vector2(BanterVector2 v)
    {
        return new Vector2() { x = v.x, y = v.y };
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
    public static explicit operator BanterVector3(SoftJointLimit v)
    {
        return new BanterVector3() { x = v.limit, y = v.bounciness, z = v.contactDistance };
    }
    public static explicit operator SoftJointLimit(BanterVector3 v)
    {
        return new SoftJointLimit() { limit = v.x, bounciness = v.y, contactDistance = v.z };
    }
    public static explicit operator Vector3(BanterVector3 v)
    {
        return new Vector3() { x = v.x, y = v.y, z = v.z };
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

    public static explicit operator float(BanterFloat v)
    {
        return v.x;
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
    public static explicit operator int(BanterInt v)
    {
        return v.x;
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
    public static explicit operator bool(BanterBool v)
    {
        return v.x;
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
    public static explicit operator string(BanterString v)
    {
        return v.x;
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