Shader "Custom/Water"
{
    Properties
    {
        _ShallowColor  ("Shallow Color",   Color)       = (0.20, 0.65, 0.85, 0.72)
        _DeepColor     ("Deep Color",      Color)       = (0.02, 0.18, 0.48, 0.95)
        _FresnelPower  ("Fresnel Power",   Float)       = 3.0
        _Smoothness    ("Smoothness",      Range(0, 1)) = 0.92
        _WaveScale     ("Wave Scale",      Float)       = 0.08
        _WaveSpeed     ("Wave Speed",      Float)       = 0.35
        _WaveAmplitude ("Wave Amplitude",  Float)       = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _FresnelPower;
                float  _Smoothness;
                float  _WaveScale;
                float  _WaveSpeed;
                float  _WaveAmplitude;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            // Suma de senos evaluada en coordenadas de mundo (consistente aunque el plano se mueva).
            float WaveY(float2 xz, float t)
            {
                float2 p = xz * _WaveScale;
                return (sin(p.x       + t)       * cos(p.y * 0.71 + t * 0.89)
                      + sin(p.x * 1.5 - t * 0.6) * sin(p.y * 1.30 + t * 0.70) * 0.50
                      + sin(p.x * 2.3 + t * 1.1) * cos(p.y * 2.00 - t * 0.80) * 0.25)
                      * _WaveAmplitude;
            }

            Varyings vert(Attributes IN)
            {
                // Obtener posición mundial antes del desplazamiento (XZ del plano = XZ del mundo).
                float3 worldXZ = TransformObjectToWorld(IN.positionOS.xyz);
                float  t       = _Time.y * _WaveSpeed;

                // Desplazar Y en espacio objeto (el plano no tiene rotación → equivalente a mundo).
                IN.positionOS.y += WaveY(worldXZ.xz, t);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);

                Varyings OUT;
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float  t  = _Time.y * _WaveSpeed;
                float2 xz = IN.positionWS.xz;

                // Normal analítica por diferencia central de la función de ondas.
                // n = normalize(-∂f/∂x, 1, -∂f/∂z)
                float eps = 0.5;
                float dX  = WaveY(xz + float2(eps, 0), t) - WaveY(xz - float2(eps, 0), t);
                float dZ  = WaveY(xz + float2(0, eps), t) - WaveY(xz - float2(0, eps), t);
                float3 normalWS = normalize(float3(-dX, 1.0, -dZ));

                float3 viewDir  = normalize(GetCameraPositionWS() - IN.positionWS);

                // Fresnel: zona central = profundo/oscuro, bordes = superficial/claro.
                float  fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                float4 col     = lerp(_ShallowColor, _DeepColor, fresnel);

                // Luz
                Light  mainLight = GetMainLight();
                float  NdotL     = saturate(dot(normalWS, mainLight.direction));
                float3 ambient   = SampleSH(normalWS); // responde al gradiente del DayNightCycle
                float3 diffuse   = mainLight.color * NdotL * 0.70;

                // Especular Blinn-Phong con brillo en zonas de fresnel alto
                float3 halfDir   = normalize(mainLight.direction + viewDir);
                float  spec      = pow(saturate(dot(normalWS, halfDir)), _Smoothness * 256.0)
                                 * fresnel * 2.0;

                col.rgb = col.rgb * (diffuse + ambient) + mainLight.color * spec;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
