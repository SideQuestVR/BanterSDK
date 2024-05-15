using UnityEngine;

public class FixLegacyShaders : MonoBehaviour{

    private Material[] thisMaterial;
    private string[] shaderNames;
    void Start ()
    {
        var renderer = GetComponent<Renderer>();
        if(renderer != null) {
            thisMaterial = renderer.sharedMaterials;  
            shaderNames =  new string[thisMaterial.Length];
            
            for (int i = 0; i < thisMaterial.Length; i++)
            {
                if(thisMaterial[i] != null) {
                    shaderNames[i] = thisMaterial[i].shader.name;
                }
            }          
        
            for( int i = 0; i < thisMaterial.Length; i++)
            {
                if(thisMaterial[i] != null) {
                    // if(shaders != null && shaders.ContainsKey(shaderNames[i])){
                    //     // Debug.Log($"replacing alt bundle shadername {thisMaterial[i].shader?.name}");
                    //     thisMaterial[i].shader = shaders[shaderNames[i]];
                    // } else {
                        var shadfound = Shader.Find(shaderNames[i]);
                        if (shadfound != null)
                        {
                            // Debug.Log($"replacing shadername {thisMaterial[i].shader?.name} with found shader");
                            thisMaterial[i].shader = shadfound;
                        }
                        //  else
                        // {
                        //     // Debug.Log($"Leaving shader {thisMaterial[i].shader?.name} alone, no replacement");
                        // }
                    // }
                }
            }     
        }     
    }
}