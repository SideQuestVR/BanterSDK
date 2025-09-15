using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.UI.Core
{
    /// <summary>
    /// Panel Pool Manager for UI Panels
    /// Manages allocation of PanelSettings IDs (0-19) for UI panels
    /// C# port of the TypeScript UIPanelPool class
    /// </summary>
    public static class UIPanelPool
    {
        private const int MAX_PANELS = 20;
        private static readonly bool[] panelPool = new bool[MAX_PANELS];
        private static readonly object lockObject = new object();
        private static bool initialized = false;
        
        /// <summary>
        /// Initialize the panel pool
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            if (initialized) return;
            
            lock (lockObject)
            {
                if (initialized) return;
                
                // Reset all panels to available
                for (int i = 0; i < MAX_PANELS; i++)
                {
                    panelPool[i] = false; // false = available
                }
                
                initialized = true;
                Debug.Log($"[UIPanelPool] Initialized panel pool with {MAX_PANELS} slots");
            }
        }
        
        /// <summary>
        /// Acquire the next available panel ID (0-19)
        /// </summary>
        /// <returns>Panel ID number or -1 if no panels available</returns>
        public static int AcquirePanel()
        {
            Initialize();
            
            lock (lockObject)
            {
                for (int i = 0; i < MAX_PANELS; i++)
                {
                    if (!panelPool[i]) // Available
                    {
                        panelPool[i] = true; // Mark as used
                        Debug.Log($"[UIPanelPool] Acquired panel: {i}");
                        return i;
                    }
                }
                
                Debug.LogWarning($"[UIPanelPool] No available panels! Maximum of {MAX_PANELS} panels allowed.");
                return -1; // No panels available
            }
        }
        
        /// <summary>
        /// Acquire a specific panel ID (used when manually assigning panel IDs)
        /// </summary>
        /// <param name="panelId">The specific panel ID to acquire (0-19)</param>
        /// <returns>true if successfully acquired, false if already in use or invalid</returns>
        public static bool AcquireSpecificPanel(int panelId)
        {
            if (!IsValidPanelId(panelId))
            {
                Debug.LogWarning($"[UIPanelPool] Invalid panel ID: {panelId}. Must be 0-{MAX_PANELS - 1}");
                return false;
            }
            
            Initialize();
            
            lock (lockObject)
            {
                if (panelPool[panelId])
                {
                    Debug.LogWarning($"[UIPanelPool] Panel {panelId} is already in use");
                    return false;
                }
                
                panelPool[panelId] = true; // Mark as used
                Debug.Log($"[UIPanelPool] Acquired specific panel: {panelId}");
                return true;
            }
        }
        
        /// <summary>
        /// Release a panel back to the pool
        /// </summary>
        /// <param name="panelId">The panel ID to release (0-19)</param>
        public static void ReleasePanel(int panelId)
        {
            if (panelId < 0 || panelId >= MAX_PANELS)
            {
                Debug.LogWarning($"[UIPanelPool] Invalid panel ID: {panelId}. Must be 0-{MAX_PANELS - 1}");
                return;
            }
            
            Initialize();
            
            lock (lockObject)
            {
                if (panelPool[panelId])
                {
                    panelPool[panelId] = false; // Mark as available
                    Debug.Log($"[UIPanelPool] Released panel: {panelId}");
                }
                else
                {
                    Debug.LogWarning($"[UIPanelPool] Panel {panelId} was not in use");
                }
            }
        }
        
        /// <summary>
        /// Check if a panel ID is currently in use
        /// </summary>
        /// <param name="panelId">The panel ID to check</param>
        /// <returns>true if the panel is in use, false if available</returns>
        public static bool IsPanelInUse(int panelId)
        {
            if (panelId < 0 || panelId >= MAX_PANELS)
            {
                return false;
            }
            
            Initialize();
            
            lock (lockObject)
            {
                return panelPool[panelId];
            }
        }
        
        /// <summary>
        /// Get the panel settings name for a given panel ID
        /// This matches the formatting used by the TypeScript UIPanelPool
        /// </summary>
        /// <param name="panelId">The panel ID (0-19)</param>
        /// <returns>The PanelSettings resource name</returns>
        public static string GetPanelSettingsName(int panelId)
        {
            if (panelId < 0 || panelId >= MAX_PANELS)
            {
                return "PanelSettings 0"; // Fallback
            }
            return $"PanelSettings {panelId}";
        }
        
        /// <summary>
        /// Validate that a panel ID is within the valid range
        /// </summary>
        /// <param name="panelId">The panel ID to validate</param>
        /// <returns>true if valid, false otherwise</returns>
        public static bool IsValidPanelId(int panelId)
        {
            return panelId >= 0 && panelId < MAX_PANELS;
        }
        
        /// <summary>
        /// Get pool status for debugging
        /// </summary>
        /// <returns>Object with pool status information</returns>
        public static PoolStatus GetPoolStatus()
        {
            Initialize();
            
            var available = new List<int>();
            var inUse = new List<int>();
            
            lock (lockObject)
            {
                for (int i = 0; i < MAX_PANELS; i++)
                {
                    if (panelPool[i])
                    {
                        inUse.Add(i);
                    }
                    else
                    {
                        available.Add(i);
                    }
                }
            }
            
            return new PoolStatus
            {
                Available = available.ToArray(),
                InUse = inUse.ToArray(),
                Total = MAX_PANELS
            };
        }
        
        /// <summary>
        /// Get the maximum number of panels supported
        /// </summary>
        public static int MaxPanels => MAX_PANELS;
    }
    
    /// <summary>
    /// Pool status information for debugging
    /// </summary>
    [System.Serializable]
    public struct PoolStatus
    {
        public int[] Available;
        public int[] InUse;
        public int Total;
        
        public override string ToString()
        {
            return $"Pool Status: {InUse.Length}/{Total} in use, {Available.Length} available";
        }
    }
}