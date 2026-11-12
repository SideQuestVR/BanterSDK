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
        // NumberStyles.Float, no thousands separator: a stray comma decimal ("0,94") throws instead of reading as 94.
        // try
        // {
        return float.Parse(val, NumberStyles.Float, CultureInfo.InvariantCulture);
        // }
        // catch (Exception e)
        // {
        //     Debug.LogError("Failed to parse " + val + " as a float: " + e.Message);
        //     return null;
        // }
    }

    // The other direction: a number written onto the page bus. Concatenating a float uses the current
    // culture, which on comma-decimal locales sends "1,5" (the page's parseFloat reads 1).
    public static string Format(float val) => val.ToString(CultureInfo.InvariantCulture);

    public static string Format(float val, string format) => val.ToString(format, CultureInfo.InvariantCulture);

    // Culture-invariant TryParse: the page and the bridge always write numbers with a '.'. NumberStyles.Float
    // has no thousands separator (as in Parse), so a comma-decimal "0,5" is rejected rather than read as 5.
    public static bool TryParse(string val, out float result) =>
        float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    public static double ParseDouble(string val) => double.Parse(val, NumberStyles.Float, CultureInfo.InvariantCulture);
}