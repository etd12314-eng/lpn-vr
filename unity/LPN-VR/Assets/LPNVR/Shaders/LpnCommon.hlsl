// LPN-VR ortak aydınlatma: web sürümündeki Three.js MeshStandardMaterial modeline denk
// (Lambert difüz + GGX spekular, tek ışık + yarıküre ortam ışığı, ACES Filmic ton eşleme).
// Işık parametreleri her kamera için LightRig.cs tarafından global olarak ayarlanır:
// laparoskop kamerasında skopa bağlı spot ışık, VR/oda görüşünde yönlü oda ışığı.
#ifndef LPN_COMMON_INCLUDED
#define LPN_COMMON_INCLUDED

float4 _LpnLightPos;    // xyz konum (spot) ; w: 0 = yönlü, 1 = spot
float4 _LpnLightDir;    // xyz: ışığın baktığı yön (dünya)
float4 _LpnLightColor;  // rgb * yoğunluk
float4 _LpnSpot;        // x menzil, y cos(koni), z cos(penumbra), w azalma üssü
float4 _LpnSky;         // rgb * yoğunluk
float4 _LpnGround;      // rgb * yoğunluk
float _LpnExposure;

#define LPN_PI 3.14159265

float3 LpnRRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 LpnACES(float3 color)
{
    color *= _LpnExposure / 0.6;
    float3 c = float3(0.59719, 0.07600, 0.02840) * color.r + float3(0.35458, 0.90834, 0.13383) * color.g + float3(0.04823, 0.01566, 0.83777) * color.b;
    c = LpnRRTAndODTFit(c);
    c = float3(1.60475, -0.10208, -0.00327) * c.r + float3(-0.53108, 1.10813, -0.07276) * c.g + float3(-0.07367, -0.00605, 1.07602) * c.b;
    return saturate(c);
}

float3 LpnShade(float3 albedo, float metallic, float roughness, float3 N, float3 posWS, float3 V)
{
    float3 diffuseColor = albedo * (1.0 - metallic);
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    roughness = clamp(roughness, 0.0525, 1.0);
    float alpha = roughness * roughness;

    float3 L; float atten = 1.0;
    if (_LpnLightPos.w < 0.5)
    {
        L = -normalize(_LpnLightDir.xyz);
    }
    else
    {
        float3 toL = _LpnLightPos.xyz - posWS;
        float d = length(toL);
        L = toL / max(d, 1e-5);
        float falloff = 1.0 / max(pow(d, _LpnSpot.w), 0.01);
        if (_LpnSpot.x > 0) { float r = saturate(1.0 - pow(d / _LpnSpot.x, 4.0)); falloff *= r * r; }
        float angleCos = dot(-L, normalize(_LpnLightDir.xyz));
        atten = falloff * smoothstep(_LpnSpot.y, _LpnSpot.z, angleCos);
    }
    float dotNL = saturate(dot(N, L));
    float3 irradiance = dotNL * _LpnLightColor.rgb * atten;

    float3 H = normalize(L + V);
    float dotNV = saturate(dot(N, V)), dotNH = saturate(dot(N, H)), dotVH = saturate(dot(V, H));
    float fresnel = exp2((-5.55473 * dotVH - 6.98316) * dotVH);
    float3 F = f0 * (1.0 - fresnel) + fresnel;
    float a2 = alpha * alpha;
    float gv = dotNL * sqrt(a2 + (1.0 - a2) * dotNV * dotNV);
    float gl = dotNV * sqrt(a2 + (1.0 - a2) * dotNL * dotNL);
    float Vis = 0.5 / max(gv + gl, 1e-6);
    float den = dotNH * dotNH * (a2 - 1.0) + 1.0;
    float D = a2 / (LPN_PI * den * den);

    float3 hemi = lerp(_LpnGround.rgb, _LpnSky.rgb, 0.5 * N.y + 0.5);
    float3 col = (irradiance + hemi) * diffuseColor / LPN_PI + irradiance * F * (Vis * D);
    return col;
}

#endif
