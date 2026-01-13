namespace Banter.UI.Bridge
{
    /// <summary>
    /// Interface for UI elements that can handle dynamic property setting
    /// </summary>
    public interface IUIPropertySetter
    {
        /// <summary>
        /// Sets a property on the UI element by name
        /// </summary>
        /// <param name="propertyName">The name of the property to set</param>
        /// <param name="propertyValue">The string value to set</param>
        /// <returns>True if the property was found and set, false otherwise</returns>
        bool SetProperty(string propertyName, string propertyValue);
    }
}