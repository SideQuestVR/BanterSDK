using System;
using UnityEngine;
using Banter.SDK;
using PropertyName = Banter.SDK.PropertyName;
using Unity.VisualScripting;

[Serializable]
[RenamedFrom("Banter.SDK.BanterVector5")]
public struct BSVector5
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


    public static explicit operator BSVector5(JointLimits v)
    {
        return new BSVector5() { x = v.bounciness, y = v.bounceMinVelocity, z = v.contactDistance, w = v.min, v = v.max };
    }
    public static explicit operator JointLimits(BSVector5 v)
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
[RenamedFrom("Banter.SDK.BanterVector4")]
public struct BSVector4
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


    public static explicit operator BSVector4(Quaternion v)
    {
        return new BSVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator Quaternion(BSVector4 v)
    {
        return new Quaternion() { x = v.x, y = v.y, z = v.z, w = v.w };
    }

    public static explicit operator BSVector4(Vector4 v)
    {
        return new BSVector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator Vector4(BSVector4 v)
    {
        return new Vector4() { x = v.x, y = v.y, z = v.z, w = v.w };
    }
    public static explicit operator BSVector4(JointDrive v)
    {
        return new BSVector4() { x = v.positionSpring, y = v.positionDamper, z = v.maximumForce, w = v.useAcceleration ? 1 : 0 };
    }
    public static explicit operator JointDrive(BSVector4 v)
    {
        return new JointDrive() { positionSpring = v.x, positionDamper = v.y, maximumForce = v.z, useAcceleration = !(v.w == 0) };
    }
}


[Serializable]
[RenamedFrom("Banter.SDK.BanterVector2")]
public struct BSVector2
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

    public static explicit operator BSVector2(Vector2 v)
    {
        return new BSVector2() { x = v.x, y = v.y };
    }
    public static explicit operator Vector2(BSVector2 v)
    {
        return new Vector2() { x = v.x, y = v.y };
    }
}

[Serializable]
[RenamedFrom("Banter.SDK.BanterVector3")]
public struct BSVector3
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

    public static explicit operator BSVector3(Vector3 v)
    {
        return new BSVector3() { x = v.x, y = v.y, z = v.z };
    }
    public static explicit operator BSVector3(SoftJointLimit v)
    {
        return new BSVector3() { x = v.limit, y = v.bounciness, z = v.contactDistance };
    }
    public static explicit operator SoftJointLimit(BSVector3 v)
    {
        return new SoftJointLimit() { limit = v.x, bounciness = v.y, contactDistance = v.z };
    }
    public static explicit operator Vector3(BSVector3 v)
    {
        return new Vector3() { x = v.x, y = v.y, z = v.z };
    }
}

[Serializable]
[RenamedFrom("Banter.SDK.BanterFloat")]
public struct BSFloat
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

    public static explicit operator BSFloat(float v)
    {
        return new BSFloat() { x = v };
    }

    public static explicit operator float(BSFloat v)
    {
        return v.x;
    }

    
}

[Serializable]
[RenamedFrom("Banter.SDK.BanterInt")]
public struct BSInt
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

    public static explicit operator BSInt(int v)
    {
        return new BSInt() { x = v };
    }
    public static explicit operator int(BSInt v)
    {
        return v.x;
    }
}

[Serializable]
[RenamedFrom("Banter.SDK.BanterBool")]
public struct BSBool
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

    public static explicit operator BSBool(bool v)
    {
        return new BSBool() { x = v };
    }
    public static explicit operator bool(BSBool v)
    {
        return v.x;
    }
}
[Serializable]
[RenamedFrom("Banter.SDK.BanterString")]
public struct BSString
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

    public static explicit operator BSString(string v)
    {
        return new BSString() { x = v };
    }
    public static explicit operator string(BSString v)
    {
        return v.x;
    }
}

[Serializable]
[RenamedFrom("Banter.SDK.BanterStruct")]
public struct BSStruct
{
    public PropertyName n;

    public static implicit operator BSStruct(BSString v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSBool v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSInt v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSFloat v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSVector2 v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSVector3 v)
    {
        return new BSStruct() { n = v.n };
    }

    public static implicit operator BSStruct(BSVector4 v)
    {
        return new BSStruct() { n = v.n };
    }

}