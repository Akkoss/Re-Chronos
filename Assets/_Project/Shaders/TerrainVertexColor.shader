Shader "Custom/TerrainVertexColor"
{
    Properties
    {
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ── PASS 1: iluminación ────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // ← Mesh.colors[i]
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                Light  mainLight = GetMainLight();
                float3 N         = normalize(IN.normalWS);
                float3 L         = normalize(mainLight.direction);

                // Difusa Lambertiana + luz ambiente mínima para que las zonas
                // en sombra no queden completamente negras.
                float lighting = 0.15 + saturate(dot(N, L));

                half3 albedo = IN.color.rgb * _Brightness;
                return half4(albedo * lighting * mainLight.color, 1.0);
            }
            ENDHLSL
        }

        // ── PASS 2: sombras ────────────────────────────────────────────────
        // Reemplaza "UsePass Universal Render Pipeline/Lit/ShadowCaster"
        // porque ese nombre de pass cambió en URP 17 (Unity 6) y causaba
        // que el SubShader completo se marcara como inválido → rosa/magenta.
        //
        // Esta implementación inline es equivalente y más robusta: no depende
        // de la versión de URP ni del nombre interno de ningún pass ajeno.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest  LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // URP escribe _LightDirection antes de ejecutar los shadow passes.
            float3 _LightDirection;

            struct AttrShadow
            {
                float4 posOS   : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(AttrShadow IN) : SV_POSITION
            {
                float3 posWS    = TransformObjectToWorld(IN.posOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // ApplyShadowBias desplaza el vértice en la dirección de la luz
                // para evitar shadow acne (el terreno "sangrando" su propia sombra).
                posWS = ApplyShadowBias(posWS, normalWS, _LightDirection);

                // Clamp de profundidad para evitar artefactos en cascadas de sombra.
                float4 posHCS = TransformWorldToHClip(posWS);
                #if UNITY_REVERSED_Z
                    posHCS.z = min(posHCS.z, posHCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posHCS.z = max(posHCS.z, posHCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return posHCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
