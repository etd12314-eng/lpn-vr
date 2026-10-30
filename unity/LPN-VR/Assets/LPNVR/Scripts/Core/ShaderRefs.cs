// Çalışma zamanında Shader.Find ile bulunan özel shader'ların build'e girmesini garanti eden referans listesi.
// Assets/LPNVR/Resources/LPNShaders.asset olarak ProjectSetup tarafından oluşturulur.
using UnityEngine;

namespace LPNVR
{
    public class ShaderRefs : ScriptableObject
    {
        public Shader[] shaders;
    }
}
