using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Banter
{
    public class RotateLoading : MonoBehaviour
    {
        public Transform spinner;
        public float speed = 1f;
        public void MoveInFront()
        {
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, Camera.main.transform.eulerAngles.y, transform.eulerAngles.z);
        }

        void Update()
        {
            if (!spinner)
            {
                return;
            }
            spinner.Rotate(0, 0, -3 * speed);
        }
    }
}