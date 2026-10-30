// Parçacık noktaları (kan damlaları, koter dumanı) — Three.js PointsMaterial (sizeAttenuation) karşılığı.
// Her parçacık 4 köşeli bir dörtgen; köşeler görüş uzayında genişletilir (billboard).
Shader "LPN/Points"
{
    Properties
    {
        _Color ("Renk", Color) = (1,1,1,1)
        _Size ("Boyut (m)", Float) = 0.0026
        _Mode ("0 kan, 1 duman", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LpnCommon.hlsl"
            float4 _Color; float _Size, _Mode;
            struct A { float4 positionOS : POSITION; float2 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vr { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Vr vert(A v)
            {
                Vr o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 vs = TransformWorldToView(TransformObjectToWorld(v.positionOS.xyz));
                vs.xy += v.corner * _Size * 0.5;
                o.positionCS = TransformWViewToHClip(vs);
                o.uv = v.corner * 0.5 + 0.5;
                return o;
            }
            half4 frag(Vr i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r = distance(i.uv, float2(0.5, 0.5)) * 2.0;
                if (_Mode < 0.5)
                {
                    // canvas radyal gradyanı: #d04040 → #7a0606 → saydam
                    float3 c0 = float3(0.63, 0.05, 0.05), c1 = float3(0.19, 0.0, 0.0);
                    float3 c = r < 0.5 ? lerp(c0, c1, r / 0.5) : c1;
                    float a = r < 0.5 ? 1.0 : saturate(1.0 - (r - 0.5) / 0.5);
                    if (a < 0.2) discard;
                    return half4(LpnACES(c * 1.2), a);
                }
                float a = saturate(1.0 - r) * 0.8 * _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
