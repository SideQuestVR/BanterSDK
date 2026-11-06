using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSStreetView : BSComponentBase
    {
        [Tooltip("The panoId of the Street View location to be displayed.")]
        [See(initial = "")][SerializeField] internal string panoId = "";

        PhotoSphere photoSphere;
        GameObject streetViewObject;
        internal override void DestroyStuff()
        {
            if (photoSphere != null)
            {
                Destroy(photoSphere);
                photoSphere = null;
            }
            if (streetViewObject != null)
            {
                Destroy(streetViewObject);
                streetViewObject = null;
            }
        }
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has loaded the panoId set there yet (see
                // valuesApplied). Without one there is nothing to load, and it counts as loaded rather
                // than holding up the space.
                if (string.IsNullOrEmpty(panoId))
                {
                    SetLoadedIfNot();
                }
                else
                {
                    ReSetup();
                }
            }
        }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            if (photoSphere != null)
            {
                Destroy(photoSphere);
            }
            if (streetViewObject != null)
            {
                Destroy(streetViewObject);
            }
            streetViewObject = Instantiate(Resources.Load<GameObject>("StreetViewPrefab"), transform, false);
            photoSphere = streetViewObject.GetComponent<PhotoSphere>();
            photoSphere.Panoid = panoId;
            Action photoSphereCallback = null;
            photoSphereCallback = () =>
            {
                photoSphere.LoadCallback -= photoSphereCallback;
                SetLoadedIfNot();
            };
            photoSphere.LoadCallback += photoSphereCallback;
        }
    }
}