using System;
using System.Globalization;
using UnityEngine;

public class NumberFormat
{
    public static float Parse(string val)
    {
        // THis handles the differences between number seperators in different countries. Rather than paste 
        // `CultureInfo.InvariantCulture` everywhere I decided that 18 characters was better than 39. 

        // The name is becuase we discovered this when everything Elin touched was 1000x as powerful. 
        try
        {
            return float.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to parse " + val + " as a float: " + e.Message);
            return -1;
        }
    }
}