using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter
{
    public class DontDestroyOnLoad : MonoBehaviour
    {
        void Start()
        {
            DontDestroyOnLoad(this);
        }
    }
}
