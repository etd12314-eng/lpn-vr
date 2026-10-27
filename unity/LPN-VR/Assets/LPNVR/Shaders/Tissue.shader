// Doku materyali — util.js tissueMaterial() karşılığı:
//  * alet ucu basınca yumuşak doku çökmesi (vertex)
//  * koter / makas diseksiyon delikleri, eksizyon krateri, elipsoid dışı kesme (fragment discard)
//  * iskemi / konjesyon renk tonu, delik kenarında kararma (yanık/kesik kenarı)
// Aynı shader; delik/deformasyon kullanılmayan tüm anatomi ve alet materyallerinde de kullanılır.
Shader "LPN/Tissue"
{
    Properties
    {
        _BaseMap ("Doku", 2D) = "white" {}
        _Color ("Renk", Color) = (1,1,1,1)
        _Emission ("Işıma", Color) = (0,0,0,0)
        _Roughness ("Pürüzlülük", Range(0,1)) = 0.4
        _Metallic ("Metaliklik", Range(0,1)) = 0
        _MapScale ("Doku ölçeği", Vector) = (1,1,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LpnCommon.hlsl"

            #define MAX_HOLES 128

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _Color, _Emission, _MapScale;
            float _Roughness, _Metallic;
            // Doku etkileşim durumu (TissueMat.cs her kare/olayda ayarlar; nesne uzayında)
            float4 _Tips[2];       // xyz uç, w aktif
            float _Sigma, _Deform;
            float4 _Holes[MAX_HOLES];
            float _HoleCount;
            float4 _Crater;        // xyz merkez, w yarıçap (0 = yok)
            float4 _Tint;
            float4 _PlaneCut;      // w > 0: _Ellip elipsoidinin dışını at (krater kasesi)
            float4 _Ellip;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float3 nWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 posOS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz;
                float3 n = v.normalOS;
                o.posOS = p;
                if (_Deform > 0.5)
                {
                    float s2 = _Sigma * _Sigma;
                    [unroll] for (int i = 0; i < 2; i++)
                    {
                        float4 t = _Tips[i];
                        if (t.w > 0.5)
                        {
                            float3 d = t.xyz - p;
                            float along = dot(d, n);
                            float lat = length(d - n * along);
                            float w = exp(-(lat * lat) / s2);
                            if (along < 0.0 && along > -0.022) p += n * along * w;
                            else if (along >= 0.0 && along < 0.004) p += n * (along - 0.004) * 0.25 * w;
                        }
                    }
                }
                o.posWS = TransformObjectToWorld(p);
                o.nWS = TransformObjectToWorldNormal(n);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.uv = v.uv * _MapScale.xy;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge = 1.0;
                int hc = (int)_HoleCount;
                [loop] for (int k = 0; k < MAX_HOLES; k++)
                {
                    if (k >= hc) break;
                    float dd = distance(i.posOS, _Holes[k].xyz) - _Holes[k].w;
                    if (dd < 0.0) discard;
                    edge = min(edge, dd);
                }
                if (_Crater.w > 0.0)
                {
                    float dc = distance(i.posOS, _Crater.xyz) - _Crater.w;
                    if (dc < 0.0) discard;
                    edge = min(edge, dc * 0.5);
                }
                if (_PlaneCut.w > 0.0)
                {
                    float3 q = i.posOS / _Ellip.xyz;
                    if (dot(q, q) > 1.0) discard;
                }
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 albedo = tex.rgb * _Color.rgb * _Tint.rgb;
                albedo *= lerp(0.25, 1.0, smoothstep(0.0, 0.0018, edge));
                float3 N = normalize(i.nWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 V = normalize(GetCameraPositionWS() - i.posWS);
                float3 col = LpnShade(albedo, _Metallic, _Roughness, N, i.posWS, V) + _Emission.rgb;
                return half4(LpnACES(col), _Color.a * tex.a);
            }
            ENDHLSL
        }
    }
}
