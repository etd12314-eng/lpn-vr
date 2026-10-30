// Laparoskopi monitörü: hafif fıçı distorsiyonu + vinyet (main.js screenMat karşılığı).
// Görüntü laparoskop kamerasında zaten pozlanıp ton eşlendiği için burada yalnız geometri efekti uygulanır.
Shader "LPN/Monitor"
{
    Properties { _MainTex ("RT", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vr { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Vr vert(A v)
            {
                Vr o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Vr i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 c = (i.uv - 0.5) * float2(1.7778, 1.0);
                float r2 = dot(c, c);
                float2 uv = 0.5 + (i.uv - 0.5) * (1.0 - 0.05 * r2);
                float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                col *= smoothstep(1.15, 0.55, length(c));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
